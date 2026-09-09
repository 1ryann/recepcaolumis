# LUMIS — Staging on Railway (single origin) — design

**Status:** approved — architecture and staging decisions final 2026-09-08. Implementation
not started.

**Supersedes:** the earlier "Vercel (frontend) + Railway (backend) + Supabase" staging
proposal (delivered as a chat preflight, not a committed doc). That proposal is
**withdrawn in full**. There is **no Vercel** in staging, no cross-provider proxy, no
`/api` rewrite, no cross-origin cookies, no CORS relaxation. This document is the only
staging design of record.

**Scope:** deploy the existing application (backend API + React SPA, exactly as it is on
branch `codex/reception-backend`, commit `ba00660`) to a single Railway service backed by a
dedicated Supabase PostgreSQL staging project. No feature work, no architecture change.

**No application code change.** Everything staging-specific is configuration. The deploy
adds only two repo files that are not application code — a root `Dockerfile` and a
`.dockerignore` (§2). The forwarded-headers concern that the withdrawn proposal handled
with a `Program.cs` edit is handled here entirely by the `ASPNETCORE_FORWARDEDHEADERS_ENABLED`
environment variable (§6).

---

## 1. Architecture

```
Browser
  │  HTTPS (Railway edge terminates TLS)
  ▼
Railway service  (1 instance)
  ├─ ASP.NET Core .NET 10 (Kestrel, plain HTTP on 0.0.0.0:$PORT inside the container)
  │    ├─ /api/*            → backend endpoints
  │    ├─ /health           → liveness
  │    ├─ /health/ready     → readiness (+ PostgreSQL probe)
  │    └─ every other path  → SPA fallback: wwwroot/index.html  (MapFallbackToFile)
  │         static assets   → wwwroot/assets/** (built ClientApp, served by UseStaticFiles)
  │  Volume mounted at /data
  │    ├─ /data/dpkeys      → Data Protection key ring
  │    └─ /data/private     → private file storage (professional photos)
  ▼
Supabase PostgreSQL — STAGING project only
```

**One origin.** The browser only ever talks to `https://<railway-host>`. The SPA is served
from the same origin that answers `/api/*`, so:

- Cookies stay first-party. `__Host-Lumis.Auth` (`SameSite=Lax`) and `__Host-Lumis.Csrf`
  (`SameSite=Strict`) work unchanged.
- No CORS. `Cors:AllowedOrigins` stays `[]`; `app.UseCors()` adds a no-op default policy.
- Antiforgery unchanged: header `X-CSRF-TOKEN` + `__Host-Lumis.Csrf`; the SPA fetches
  `/api/auth/csrf` and echoes the token on mutations.
- The existing strict CSP (`connect-src 'self'`, `script-src 'self'`) applied to backend
  responses is *correct* for this topology — the SPA is `'self'` to itself.

**How the single origin already works in the codebase (no change needed):**

- `recepcaototem/recepcaototem.csproj` has a `BuildClientApp` MSBuild target
  (`BeforeTargets="ComputeFilesToPublish"`) that runs `npm ci && npm run build` in
  `ClientApp/` and copies `ClientApp/dist/**` (minus source maps) into `wwwroot/` of the
  publish output.
- `Program.cs`: `app.UseDefaultFiles(); app.UseStaticFiles();` serve `wwwroot`; endpoints
  are mapped; `app.Map("/api/{**path}", () => Results.NotFound()).RequireAuthorization()`
  returns 401/404 for unknown API paths; `app.MapFallbackToFile("index.html").AllowAnonymous()`
  serves the SPA for everything else, so client-side routes (`/admin/salas`, `/profissional/agenda`,
  `/cliente/agendar`, `/totem/check-in`, …) resolve to `index.html` and React Router takes over.
- The built SPA has `import.meta.env.DEV === false`, so `src/App.tsx` renders
  `ProductionApp` — **no `DevelopmentApp`, no mock store, no `src/dev` data**. `client.ts`
  uses relative `/api/...` paths only (no `VITE_API_*`), which is exactly what a single
  origin needs.

**No connection to production.** Staging uses its own Supabase project, its own Railway
service, its own Data Protection key ring, its own initial admin. The
`tools/GestaoPredio.DataMigration` importer (SQL Server → Supabase, Supabase-host guarded)
is **not used** — staging starts from an empty schema. `Migration:*` user-secrets on
developer machines point at other environments and are irrelevant here.

---

## 2. Build — Dockerfile (multi-stage)

Railway builds from a repo-root `Dockerfile`. Multi-stage:

**Stage `build` — `mcr.microsoft.com/dotnet/sdk:10.0` (Debian bookworm):**

- Make **Node 22** available in this stage by copying it from the **official
  `node:22-bookworm-slim` image** — `COPY --from=node:22-bookworm-slim /usr/local/bin/ /usr/local/bin/`
  and `COPY --from=node:22-bookworm-slim /usr/local/lib/node_modules/ /usr/local/lib/node_modules/`.
  Do **not** use a NodeSource install script unless the copy approach proves insufficient.
  Node/npm are a **build-time** dependency only — `dotnet publish recepcaototem` invokes the
  `BuildClientApp` MSBuild target, which runs `npm ci && npm run build` in `ClientApp/`.
- Validate the toolchain early in the stage: `node --version` (expect `v22.x`) and
  `npm --version` must both succeed; a missing/wrong Node fails the build here rather than
  deep inside `dotnet publish`.
- `dotnet restore` (solution or `recepcaototem` + its project refs).
- Run `npm ci` then **`npm run verify:production-bundle`** inside `ClientApp/` — the repo's
  bundle safety check (rejects source maps, `src/dev`, mock datasets in `dist`). **A
  violation must fail the Docker build** (non-zero exit; do not `|| true` it). This runs
  before, or as part of, the publish so a leaked mock/dev artifact never reaches the image.
- `dotnet publish recepcaototem/recepcaototem.csproj -c Release -o /app/publish`
  → produces `recepcaototem.dll` + `wwwroot/` (SPA) + `web.config` (IIS artifact, ignored
  on Kestrel).

**Stage `runtime` — `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian bookworm):**

- **Debian, not Alpine.** The image ships ICU + `tzdata`, required because `Program.cs`
  calls `OperationalTimeZone.Resolve("America/Porto_Velho")` →
  `TimeZoneInfo.FindSystemTimeZoneById(...)` at startup and **throws** if the zone is not
  found. Do **not** set `InvariantGlobalization=true`.
- `COPY --from=build /app/publish .`
- Do **not** hardcode `EXPOSE`/a fixed port in a way that overrides `$PORT`; Kestrel binds
  from `ASPNETCORE_URLS` (§4).
- Entry: create the volume subdirs, then run the app:
  `sh -c 'mkdir -p /data/dpkeys /data/private && exec dotnet recepcaototem.dll'`
  (the `mkdir` is required because in `ASPNETCORE_ENVIRONMENT=Production` the private-file
  storage validator refuses a non-existent directory instead of creating it).

**`.dockerignore`:** `**/bin`, `**/obj`, `**/node_modules`, `.git`, `artifacts/`,
`**/dist`, `**/.vite`, `**/appsettings.*.Local.json`, `.env`, `.env.*`, `**/secrets.json`,
`**/TestResults`, `docs/` (optional).

**Timezone verification (must pass in the built image):**
`docker run --rm <image> sh -c 'TZ=America/Porto_Velho date'` resolves, and the app logs
`Application started` without a `TimeZoneNotFoundException`.

---

## 3. Railway

- **Region: US East (Virginia).** Chosen to sit next to the Supabase `us-east-1`
  (N. Virginia) project so DB round-trips stay well inside the fixed 5 s Npgsql command
  timeout (§4, §8).
- **One service**, **one instance** (`replicas = 1`). Filesystem-based Data Protection keys
  are not shared across replicas; a second instance would split the key ring and break
  cookie decryption. Multi-instance is a future concern that requires moving the key ring
  to the database/blob (out of scope, §11).
- **Outbound IPv6: enabled.** The service's egress to Supabase may use IPv6. The connection
  string (§4) uses the Supabase hostname (not a literal IP), so name resolution picks the
  available family; IPv6 egress must be on for the direct-connection host to resolve/route.
- **Persistent volume** mounted at `/data`.
  - `/data/dpkeys` → `Security__DataProtectionPath`.
  - `/data/private` → `Storage__PrivateFilesPath`.
  - The volume persists across deploys, so sessions survive a redeploy and uploaded
    professional photos are not lost.
- **Networking:** the container listens on `http://0.0.0.0:$PORT` (Railway injects `$PORT`).
  Railway's edge terminates TLS and forwards plain HTTP with `X-Forwarded-Proto: https`
  and `X-Forwarded-For: <client>`. See §6 for how the app is told to honour those.
- **Health check path:** `/health/ready` (so a new deploy is only marked healthy once the
  container can reach Supabase). `/health` is available for a lighter liveness signal.
- **Build:** Dockerfile (auto-detected at repo root). Nixpacks not used.
- **Domain: the Railway-generated `*.up.railway.app` host.** No custom domain for staging.
  Its exact hostname is the single value used for `AllowedHosts` and
  `Rescheduling__PublicBaseUrl` (§4).

---

## 4. Configuration (Railway environment variables)

All values are set in the **Railway service variables** UI. Nothing below goes into Git.
Nested keys use the `__` (double-underscore) form.

| Variable | Value | Why |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Hardened path: forces the Data Protection guard, enables HSTS + the HTTPS-required middleware, keeps notification/access-control providers on their fail-closed non-dev default. `Staging` as an env name is neither `IsDevelopment()` nor `IsProduction()` and would silently skip the DP guard. |
| `ASPNETCORE_URLS` | `http://0.0.0.0:$PORT` | Kestrel binds the Railway-assigned port over plain HTTP inside the container. TLS is the edge's job. |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | Handles the reverse-proxy concern with **no code change** — see §6. |
| `ConnectionStrings__DefaultConnection` | `Host=<supabase-staging-host>;Port=5432;Database=postgres;Username=postgres;Password=<staging-db-password>;SSL Mode=Require;Trust Server Certificate=true;Include Error Detail=false;Maximum Pool Size=20` | **Direct connection**, port 5432, TLS required. See §8 for the direct-vs-Supavisor decision and fallback. `Maximum Pool Size` kept small for one instance. `Include Error Detail=false` avoids leaking parameter values in exceptions. |
| `AllowedHosts` | the **exact** Railway-generated hostname, e.g. `lumis-staging.up.railway.app` (no scheme, no path, no wildcard) | `appsettings.json` ships `"localhost;127.0.0.1;[::1]"`; without an override the host-filtering middleware returns **400** for every request on the Railway domain. The final decision is to pin the exact host, not `*`. |
| `Security__DataProtectionPath` | `/data/dpkeys` | **Required** in `Production` (`Program.cs` throws at startup if unset). Absolute, writable, on the persistent volume. |
| `Storage__PrivateFilesPath` | `/data/private` | **Required** (`ValidateOnStart`). Absolute, writable, on the volume, must not overlap the content/web root. Pre-created by the container entrypoint. |
| `Scheduling__TimeZoneId` | `America/Porto_Velho` | Already in `appsettings.json`; kept explicit so the deploy is self-describing. Startup throws if missing/blank. |
| `Rescheduling__PublicBaseUrl` | `https://<exact-railway-host>` (same host as `AllowedHosts`, with the `https://` scheme, no trailing slash) | Base for the login-free reschedule link (`{PublicBaseUrl}/reagendar/{token}`). No message is actually sent while `Notifications__Provider=Demo`, but the Demo recorder captures the URL, so setting it makes the smoke test's captured link correct and single-origin. |
| `Notifications__Provider` | `Demo` | Predictable, in-memory recorder, **zero external calls**. (Even the `Meta` provider is a fail-closed stub that never sends, but `Demo` is explicit and quiet.) |
| `AccessControl__Provider` | `Demo` | In-memory recorder, **no hardware/network call**. (The `Intelbras` provider is likewise a fail-closed stub.) |
| `Logging__LogLevel__Default` | `Information` | Optional. Raises staging visibility above the `Production` default of `Warning`. EF Core logging stays `None` (no SQL/parameter leakage). |
| `RateLimiting__PermitLimit` / `RateLimiting__WindowSeconds` | `120` / `60` | Optional; defaults already exist in `appsettings.json`. |

**Deliberately NOT set** (leave unset): `Cors__AllowedOrigins__*` (single origin — no CORS);
`Notifications__Meta__*`; `AccessControl__Intelbras__*`.

**One-off tooling (operator machine, not Railway):** the EF migration step and the
`GestaoPredio.AdminCli` bootstrap step each read `ConnectionStrings__DefaultConnection`
(= the staging connection) and `ASPNETCORE_ENVIRONMENT=Production` from the operator's
shell environment. See §8, §9.

---

## 5. Security

- **Same-origin is preserved.** No cookie attribute changes. `__Host-Lumis.Auth`:
  `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, no `Domain`, 30-minute sliding expiration.
  `__Host-Lumis.Csrf`: `HttpOnly`, `Secure`, `SameSite=Strict`. The `__Host-` prefix is
  satisfied (HTTPS at the edge, `Path=/`, no `Domain`).
- **`SameSite` is not relaxed.** No `SameSite=None`.
- **Antiforgery is unchanged.** No new bypass, no header/cookie change.
- **CORS is not enabled.** `Cors:AllowedOrigins` stays empty; the default policy is a no-op.
- **Meta WhatsApp is off.** `Notifications__Provider=Demo`; `Notifications__Meta__*` unset.
  The Meta adapter has no send implementation regardless.
- **Intelbras is off.** `AccessControl__Provider=Demo`; `AccessControl__Intelbras__*` unset.
  The Intelbras adapter has no protocol implementation regardless.
- **Secrets live only in Railway (env vars) and Supabase (project settings).** Nothing new
  is committed. `.gitignore` already covers `.env*`, `*.pfx`, `appsettings.*.Local.json`,
  `**/secrets.json`, `artifacts/`. Committed `appsettings*.json` contain zero credentials.
  The initial admin password is entered interactively into `GestaoPredio.AdminCli`
  (no-echo) and never stored in an argument, file, or variable.
- **Data Protection at rest — staging-only acceptance.** On Linux the key ring is written
  to `/data/dpkeys` **unencrypted** (`ProtectKeysWithDpapi()` is Windows-only and skipped).
  This is **explicitly accepted for staging only**, on the isolated `/data` volume of a
  single-tenant Railway service. Production must revisit key protection (certificate/KMS
  protector, or a DB/blob-backed key ring) — out of scope here, noted in §11.
- **HTTPS enforcement:** `Program.cs` rejects non-HTTPS requests with `400 { "code": "HTTPS_REQUIRED" }`
  in non-Development (except `/health` and `/health/ready`). With `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
  (§6) this evaluates the real external scheme from `X-Forwarded-Proto`, so legitimate HTTPS
  traffic passes.
- **`UseHsts()`** runs in non-Development.
- **Response security headers** (`X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
  strict `Content-Security-Policy`) are applied globally and remain correct for a single origin.

---

## 6. Forwarded headers (Railway edge) — configuration only, no code change

**Problem.** The app has no `UseForwardedHeaders` in `Program.cs`. Behind Railway's
TLS-terminating edge, with nothing processing the forwarded headers:

- `HttpContext.Request.IsHttps` is `false` → the HTTPS-required middleware returns **400 on
  every request**.
- `HttpContext.Connection.RemoteIpAddress` is the edge's address → the global rate limiter,
  which partitions on `…:{RemoteIpAddress}`, collapses all traffic into one bucket, and
  audit entries record the edge IP instead of the client.

**Decision: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` — no `Program.cs` edit.**

Per the ASP.NET Core 10 documentation, this host-level environment variable makes the
framework insert the Forwarded Headers middleware at the front of the pipeline with:

- `ForwardedHeaders = XForwardedFor | XForwardedProto` (both `X-Forwarded-For` and
  `X-Forwarded-Proto` are consumed), and
- `KnownProxies` / `KnownNetworks` **cleared** — i.e. the middleware accepts the forwarded
  values without the caller having to register the platform edge's (dynamic) address. This
  is the documented cloud-hosting configuration.

After it runs, `Request.IsHttps` reflects `X-Forwarded-Proto: https`, `Request.Scheme` is
`https`, and `RemoteIpAddress` is the client. The HTTPS-required check then passes for real
external HTTPS traffic; the rate limiter partitions and audit IPs are per-client again.

The withdrawn proposal's explicit `builder.Services.Configure<ForwardedHeadersOptions>(…)` +
`app.UseForwardedHeaders()` in `Program.cs` is **removed** from this design. No application
code is touched.

**Staging-specific acceptance.** Clearing `KnownProxies`/`KnownNetworks` means the app
trusts `X-Forwarded-*` from **any** upstream that can reach the container, not only a
registered proxy. On Railway the container's `$PORT` bind is reachable only through the
platform edge, so for **staging** this is an accepted trade-off. It is recorded here as a
deliberate staging decision, not a default to carry forward.

**Production must revisit this** (out of scope, §11): restrict trust to the platform's
published proxy addresses/networks, or otherwise constrain ingress, rather than accepting
forwarded headers from unregistered proxies. If a future change ever needs the typed API in
.NET 10, note that `ForwardedHeadersOptions.KnownNetworks` is **obsolete** — use
`KnownIPNetworks` (and `KnownProxies`), and never `ForwardLimit = null`.

**Validation (part of the smoke, §10):** after deploy, `GET https://<host>/api/auth/csrf`
must return **200**, not `400 {"code":"HTTPS_REQUIRED"}`. A `400 HTTPS_REQUIRED` on an
HTTPS request means the toggle did not take effect as documented — treat that as a
blocking finding and re-open the explicit-registration option before proceeding.

---

## 7. Health

- **`/health`** — liveness. `Predicate = _ => false` (no checks run) → `Healthy` whenever
  the process is up. `AllowAnonymous`, rate-limiting disabled.
- **`/health/ready`** — readiness. Runs checks tagged `ready`; the only one is
  `DatabaseHealthCheck` → `IDatabaseProbe.CanConnectAsync` against PostgreSQL, 6-second
  timeout. `AllowAnonymous`. Returns `{ "status": "Healthy" | "Unhealthy" }`.
- Railway health check → `/health/ready`. It only tests *connectivity*, not schema, so it
  is safe to point Railway at it even before migrations are applied.

---

## 8. Database (Supabase staging)

- A **dedicated Supabase project for staging**, **region `us-east-1` (N. Virginia)** — same
  region as the Railway service (§3), so DB round-trips stay well inside the fixed 5 s
  Npgsql command timeout. No shared credentials, no network path to the production database.
- **Runtime connection: Direct Connection on port 5432**, `SSL Mode=Require`, as in §4.
  Preferred because a single always-on instance keeps a stable pool and there are no
  transaction-pooler prepared-statement caveats.
  - **Fallback (only if the direct connection fails to establish from Railway — e.g. IPv6
    egress/routing issues to the direct host):** Supabase **Supavisor in Session Mode**,
    also on **port 5432**. Session mode preserves session state and prepared statements, so
    Npgsql behaves the same; only `Host` (and possibly `Username`, which becomes
    `postgres.<project-ref>`) change in the connection string. `Maximum Pool Size=20` stays.
  - **Never use Transaction Mode (port 6543).** It breaks Npgsql prepared statements and
    session-scoped state and is explicitly excluded.
- The connection string uses the **hostname**, not a literal IP, so DNS selects the
  reachable address family (Railway "Outbound IPv6" is enabled, §3).
- **Migrations run out of the application startup.** `Program.cs` never calls
  `Migrate()`/`EnsureCreated()` (explicit comment at the end of the file). The staging
  schema is created by an operator step before the first deploy that needs it:

  1. Confirm a fresh/expected DB: `select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId";`
     (empty on a brand-new project).
  2. Apply from an operator machine, with `ConnectionStrings__DefaultConnection` set to the
     staging connection (or passed via `--connection`):
     `dotnet ef database update --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --context ApplicationDbContext --connection "<staging connection>"`
     — **or** generate the idempotent script
     (`dotnet ef migrations script --idempotent -o artifacts/sql/staging.sql`), review it,
     take a backup, and run it in the Supabase SQL editor.
  3. Verify the head migration is
     `20260908210951_ProfessionalPresenceAndRescheduling` and that all 12 migrations are
     recorded:
     `20260905234344_PostgreSqlBaseline`, `20260906034221_LeasesFoundation`,
     `20260906180400_ReservationsFoundation`, `20260906205535_VisitsFoundation`,
     `20260906232432_OperatingHoursAndRoomBlocks`, `20260907020228_FinancialChargesFoundation`,
     `20260907044031_CustomersAndCheckIn`, `20260907044154_CustomerLinks`,
     `20260907142242_ProfessionalDescription`, `20260907172715_ProfessionalRegistrationRequests`,
     `20260908042248_ProfessionalAvailability`, `20260908210951_ProfessionalPresenceAndRescheduling`.
- **Never run `Down` on staging automatically.** No release step invokes it. A schema reset,
  if ever needed, is a deliberate manual operation (drop the schema, re-run `database update`,
  re-run bootstrap).
- **`unaccent` extension.** `20260905234344_PostgreSqlBaseline` emits the Npgsql annotation
  `Npgsql:PostgresExtension:extensions.unaccent`, which EF renders as
  `CREATE EXTENSION IF NOT EXISTS unaccent SCHEMA extensions;`. Supabase ships the
  `extensions` schema and the `unaccent` extension is available to the `postgres` role, so
  the migration itself installs it. After migrating, confirm:
  `select extname, extnamespace::regnamespace from pg_extension where extname = 'unaccent';`
  → one row, namespace `extensions`. The `PostgreSqlText.Unaccent` DbFunction is mapped to
  `extensions.unaccent`; a missing extension surfaces as a runtime query failure on
  accent-insensitive searches (professionals/customers lookup).

---

## 9. Bootstrap (roles + first admin)

There is no startup seeder (by design). The initial identity is provisioned once, **after
migrations**, with `tools/GestaoPredio.AdminCli`:

1. `dotnet publish tools/GestaoPredio.AdminCli/GestaoPredio.AdminCli.csproj -c Release -o <dir>`
   on the operator machine (kept out of the deploy artifact).
2. Export `ConnectionStrings__DefaultConnection` = the staging connection and
   `ASPNETCORE_ENVIRONMENT=Production` in the operator shell.
   `ConnectionStringGuard` only restricts `Development`/`Testing` to `localhost:5432/LumisDev`;
   `Production` accepts the Supabase host.
3. `GestaoPredio.AdminCli provision-roles` → creates the five authentication roles
   (`ADMINISTRADOR`, `GERENTE`, `PROFISSIONAL`, `CUSTOMER`, `PROFESSIONAL_APPLICANT`).
4. `GestaoPredio.AdminCli bootstrap-admin` → prompts for display name, e-mail, and password
   (no-echo; the password is **not** accepted via argument, environment variable, or file).
   Creates the first `ADMINISTRADOR` and any missing roles in one audited transaction.
   Idempotent: it refuses to create a second administrator (`AlreadyProvisioned`).
   It does **not** create schema or run migrations.
   The password must satisfy the identity policy: ≥ 12 characters, with an uppercase, a
   lowercase, a digit, and a non-alphanumeric character.
5. Discard the operator package per local policy. Subsequent Gerente / Profissional /
   Customer accounts are created from the running app (admin user administration UI, or the
   public professional/customer registration flows).

---

## 10. Deploy order

No step below is executed by this document. The first implementation is run **from the
operator machine**. Order:

1. **Prepare code.** On a branch off `codex/reception-backend`: add **only** the root
   `Dockerfile` + `.dockerignore` (§2) — **no application code change**. Build locally
   (`dotnet build`, `dotnet test`, `docker build`); run `docker run --rm <image> sh -c 'TZ=America/Porto_Velho date'`
   and confirm the app starts without a `TimeZoneNotFoundException`. Do not push.
2. **Create the Supabase staging project** in **`us-east-1` (N. Virginia)**. Record the
   **Direct Connection** string (port 5432) and the `postgres` password; also note the
   Supavisor **Session Mode** host (port 5432) for the fallback (§8). Confirm the project's
   direct-connection limit is comfortably above `Maximum Pool Size=20`.
3. **Apply migrations** to the staging DB from the operator machine (§8) with
   `ConnectionStrings__DefaultConnection` = the staging connection and
   `ASPNETCORE_ENVIRONMENT=Production`. Verify `__EFMigrationsHistory` head =
   `20260908210951_ProfessionalPresenceAndRescheduling` (all 12 recorded) and the
   `unaccent` extension in schema `extensions`.
4. **Create the Railway service** from the repo `Dockerfile`, **region US East (Virginia)**,
   **replicas = 1**, **Outbound IPv6 enabled**.
5. **Configure the volume and environment.** Mount a persistent volume at `/data`; set all
   variables from §4 (including `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`,
   `AllowedHosts` = exact Railway host, `Rescheduling__PublicBaseUrl=https://<same host>`);
   set the health-check path to `/health/ready`.
6. **Deploy** the backend + embedded SPA (single build, single service).
7. **Validate `/health`** over the public HTTPS URL → `{ "status": "Healthy" }`.
8. **Validate `/health/ready`** → `{ "status": "Healthy" }` (confirms Supabase reachability
   from Railway). If `Unhealthy`: check the connection string, IPv6 egress, SSL mode, and
   Supabase network restrictions. If the **direct** connection is the failure, switch the
   `Host` to the Supavisor Session Mode host (§8) and redeploy.
9. **Validate forwarded headers** (§6): `GET https://<host>/api/auth/csrf` → **200**. A
   `400 {"code":"HTTPS_REQUIRED"}` on this HTTPS request is a blocking finding — the env
   toggle did not behave as documented; stop and re-open the explicit-registration
   contingency before continuing.
10. **Bootstrap the admin** (§9): `provision-roles`, then `bootstrap-admin`.
11. **Full smoke test through the public URL** — the flow validated locally
    (GERENTE → Operating Hours + Room; PROFISSIONAL → CUSTOM availability; CUSTOMER →
    booking → reservation; QR → Totem check-in → Visit `WAITING`; RECEPTION → start → end).
    Watch the Railway logs for `400 HTTPS_REQUIRED` or a host-filter 400. Confirm login
    works (first-party cookies over the single origin), no 500s, no request loop, no
    console 401 on an authenticated screen.

---

## 11. Out of scope (explicitly deferred)

- **Production deployment** — separate DB, domains, secrets, Data Protection key
  protection, forwarded-headers trust restriction, possibly multi-instance (→ DB/blob-backed
  key ring, a code change at that point).
- **Certificate/KMS protection of the Data Protection key ring.** Staging accepts
  unencrypted-at-rest keys on the `/data` volume (§5).
- **Forwarded-headers trust restriction.** Staging accepts `X-Forwarded-*` from any upstream
  via `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (§6). Production must pin trust to the
  platform's proxy addresses/networks (typed API in .NET 10: `KnownIPNetworks` /
  `KnownProxies`; `KnownNetworks` is obsolete).
- **`Npgsql` command timeout.** Kept at the hard-coded 5 s in `Program.cs`. Railway and
  Supabase are co-located in US East (§3, §8), so no change now. Only revisit — as a small
  config-driven change — if staging demonstrates a **real** timeout on a legitimate query.
- **Object storage for private files** instead of the `/data` volume.
- **CDN / long-lived caching for hashed SPA assets.** The global `Cache-Control: no-store`
  header also lands on `wwwroot/assets/**`, so the browser re-fetches the JS/CSS bundle on
  every load — extra latency on staging, not broken. A production optimization exempts the
  hashed `/assets/*` path.
- **CI/CD pipeline** (there is no `.github/workflows`). Staging deploys are manual per §10.
- **Automated staging data reset tooling.**

---

## 12. Decisions status

All architecture and staging decisions are **final** (§1–§10). **No blocking design
decision remains before the implementation plan.**

Two items are **confirmed during implementation**, not resolved on paper — neither blocks
writing the plan:

1. **`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` behaviour.** The design relies on the
   ASP.NET Core 10 documented behaviour that this toggle enables `X-Forwarded-For` +
   `X-Forwarded-Proto` and clears `KnownProxies`/`KnownNetworks` for cloud hosting. Verified
   empirically at deploy step §10.9 (`GET /api/auth/csrf` must be 200). **Contingency if it
   does not hold:** fall back to an explicit `builder.Services.Configure<ForwardedHeadersOptions>`
   + `app.UseForwardedHeaders()` gated to non-Development (using `KnownIPNetworks`, not the
   obsolete `KnownNetworks`) — a small, isolated code change, only if the env toggle proves
   insufficient.
2. **Supabase Direct Connection reachability from Railway** (IPv6 egress). Verified at
   §10.8. **Contingency:** switch `Host` to the Supavisor **Session Mode** endpoint (port
   5432) — a connection-string change only, no code, no schema change. Transaction Mode
   (6543) is excluded.

---

## 13. Self-review vs. the withdrawn Vercel proposal

| Concern in the earlier preflight | Status in this design |
|---|---|
| Cross-origin cookies (`SameSite=Lax`/`Strict` not sent cross-site) | **Removed.** Single origin; cookies are first-party; no attribute change. |
| Vercel `/api/*` rewrite proxy; `Set-Cookie` passthrough; two hops | **Removed.** No Vercel, no rewrite, no second proxy hop. |
| CORS origins + `AllowCredentials` for a split origin | **Removed.** `Cors:AllowedOrigins` stays `[]`; no CORS. |
| Antiforgery `SameSite=Strict` cookie failing cross-site | **Removed.** Antiforgery unchanged and works same-origin. |
| `client.ts` relative `/api` needing a proxy to reach the backend | **Resolved by topology.** Same origin serves `/api` and the SPA; relative paths are correct. |
| Two build/deploy targets (Vercel + Railway) | **Collapsed to one.** `dotnet publish` builds and embeds the SPA; one Railway service. |
| Forwarded headers behind an edge proxy | **Configuration only.** `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (§6). The withdrawn proposal's `Program.cs` edit is removed. **No application code change.** |
| `AllowedHosts` localhost default | **Still required** as an env override — the exact Railway host. §4. |
| `Security__DataProtectionPath` / `Storage__PrivateFilesPath` required, persistence | **Unchanged.** §3–§4, backed by the `/data` volume. |
| Migrations out of startup; `unaccent`; head migration | **Unchanged.** §8. |
| First admin via `GestaoPredio.AdminCli` | **Unchanged.** §9. |
| Meta/Intelbras must not fire | **Unchanged.** Fail-closed stubs + `Provider=Demo`. §5. |
| Debian (not Alpine); timezone; no `InvariantGlobalization` | **Unchanged.** §2. |
| Node availability for the SPA build during `dotnet publish` | `COPY --from=node:22-bookworm-slim` into the SDK build stage; `node`/`npm --version` validated in the build; NodeSource avoided. §2. |
| Region / DB latency vs the 5 s command timeout | **Resolved.** Railway US East (Virginia) + Supabase `us-east-1`; timeout kept at 5 s, no code change. §3, §8, §11. |

No residual dependency on Vercel remains anywhere in this document.
