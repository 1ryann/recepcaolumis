# LUMIS — Staging on Railway (single origin) — design

**Status:** approved — architecture decided 2026-09-08. Implementation not started.

**Supersedes:** the earlier "Vercel (frontend) + Railway (backend) + Supabase" staging
proposal (delivered as a chat preflight, not a committed doc). That proposal is
**withdrawn in full**. There is **no Vercel** in staging, no cross-provider proxy, no
`/api` rewrite, no cross-origin cookies, no CORS relaxation. This document is the only
staging design of record.

**Scope:** deploy the existing application (backend API + React SPA, exactly as it is on
branch `codex/reception-backend`, commit `ba00660`) to a single Railway service backed by a
dedicated Supabase PostgreSQL staging project. No feature work, no architecture change. The
only code delta is a forwarded-headers registration (§6).

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

- Install **Node 22** (NodeSource `setup_22.x`, or `COPY --from=node:22-bookworm-slim`),
  because `dotnet publish recepcaototem` invokes the `BuildClientApp` target which needs
  `npm`. Node/npm are a **build-time** dependency only.
- `dotnet restore` (solution or `recepcaototem` + its project refs).
- Optionally run `npm run verify:production-bundle` inside `ClientApp/` (the repo's bundle
  safety check — rejects source maps, `src/dev`, mock datasets in `dist`). Recommended: run
  it and fail the build on violation.
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

- **One service**, **one instance** (`replicas = 1`). Filesystem-based Data Protection keys
  are not shared across replicas; a second instance would split the key ring and break
  cookie decryption. Multi-instance is a future concern that requires moving the key ring
  to the database/blob (out of scope, §11).
- **Persistent volume** mounted at `/data`.
  - `/data/dpkeys` → `Security__DataProtectionPath`.
  - `/data/private` → `Storage__PrivateFilesPath`.
  - The volume persists across deploys, so sessions survive a redeploy and uploaded
    professional photos are not lost.
- **Networking:** the container listens on `http://0.0.0.0:$PORT` (Railway injects `$PORT`).
  Railway's edge terminates TLS and forwards plain HTTP with `X-Forwarded-Proto: https`
  and `X-Forwarded-For: <client>`.
- **Health check path:** `/health/ready` (so a new deploy is only marked healthy once the
  container can reach Supabase). `/health` is available for a lighter liveness signal.
- **Build:** Dockerfile (auto-detected at repo root). Nixpacks not used.
- **Region:** choose the Railway region closest to the Supabase staging project region
  (see §10, open decision — affects DB latency vs the 5 s command timeout).
- **Domain:** the generated `*.up.railway.app` domain is acceptable for staging; a custom
  domain is optional. Whatever the final host is, it must be listed in `AllowedHosts` (§4).

---

## 4. Configuration (Railway environment variables)

All values are set in the **Railway service variables** UI. Nothing below goes into Git.
Nested keys use the `__` (double-underscore) form.

| Variable | Value | Why |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Hardened path: forces the Data Protection guard, enables HSTS + the HTTPS-required middleware, keeps notification/access-control providers on their fail-closed non-dev default. `Staging` as an env name is neither `IsDevelopment()` nor `IsProduction()` and would silently skip the DP guard. |
| `ASPNETCORE_URLS` | `http://0.0.0.0:$PORT` | Kestrel binds the Railway-assigned port over plain HTTP inside the container. TLS is the edge's job. |
| `ConnectionStrings__DefaultConnection` | `Host=<supabase-staging-host>;Port=5432;Database=postgres;Username=postgres;Password=<staging-db-password>;SSL Mode=Require;Trust Server Certificate=true;Include Error Detail=false;Maximum Pool Size=20` | Direct connection (5432), TLS required. `Maximum Pool Size` kept small for one instance against a shared Supabase project. `Include Error Detail=false` avoids leaking parameter values in exceptions. |
| `AllowedHosts` | the exact Railway host, e.g. `lumis-staging.up.railway.app` | `appsettings.json` ships `"localhost;127.0.0.1;[::1]"`; without an override the host-filtering middleware returns **400** for every request on the Railway domain. `*` is tolerable for staging but the explicit host is preferred. |
| `Security__DataProtectionPath` | `/data/dpkeys` | **Required** in `Production` (`Program.cs` throws at startup if unset). Absolute, writable, on the persistent volume. |
| `Storage__PrivateFilesPath` | `/data/private` | **Required** (`ValidateOnStart`). Absolute, writable, on the volume, must not overlap the content/web root. Pre-created by the container entrypoint. |
| `Scheduling__TimeZoneId` | `America/Porto_Velho` | Already in `appsettings.json`; kept explicit so the deploy is self-describing. Startup throws if missing/blank. |
| `Notifications__Provider` | `Demo` | Predictable, in-memory recorder, **zero external calls**. (Even the `Meta` provider is a fail-closed stub that never sends, but `Demo` is explicit and quiet.) |
| `AccessControl__Provider` | `Demo` | In-memory recorder, **no hardware/network call**. (The `Intelbras` provider is likewise a fail-closed stub.) |
| `Logging__LogLevel__Default` | `Information` | Optional. Raises staging visibility above the `Production` default of `Warning`. EF Core logging stays `None` (no SQL/parameter leakage). |
| `RateLimiting__PermitLimit` / `RateLimiting__WindowSeconds` | `120` / `60` | Optional; defaults already exist in `appsettings.json`. |

**Deliberately NOT set** (leave unset): `Cors__AllowedOrigins__*` (single origin — no CORS);
`Notifications__Meta__*`; `AccessControl__Intelbras__*`; `ASPNETCORE_FORWARDEDHEADERS_ENABLED`
(handled in code, §6); `Rescheduling__PublicBaseUrl` (only used to build the WhatsApp
reschedule URL — irrelevant while `Notifications__Provider=Demo` sends nothing; if wanted
later, set it to `https://<railway-host>`).

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
- **Data Protection at rest:** on Linux the key ring is written to `/data/dpkeys`
  **unencrypted** (`ProtectKeysWithDpapi()` is Windows-only and skipped). Acceptable for a
  staging environment on an isolated volume. Hardening for production (certificate/KMS
  protector, or DB-backed keys) is out of scope here and noted in §11.
- **HTTPS enforcement:** `Program.cs` rejects non-HTTPS requests with `400 { "code": "HTTPS_REQUIRED" }`
  in non-Development (except `/health` and `/health/ready`). With §6 in place this evaluates
  the real external scheme, so legitimate HTTPS traffic passes and any plain-HTTP path that
  bypasses the edge is still rejected.
- **`UseHsts()`** runs in non-Development.
- **Response security headers** (`X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
  strict `Content-Security-Policy`) are applied globally and remain correct for a single origin.

---

## 6. Forwarded headers (Railway edge)

**Problem.** The app has no `UseForwardedHeaders`. Behind Railway's TLS-terminating edge:

- `HttpContext.Request.IsHttps` is `false` → the HTTPS-required middleware returns **400 on
  every request**.
- `HttpContext.Connection.RemoteIpAddress` is the edge's address → the global rate limiter,
  which partitions on `…:{RemoteIpAddress}`, collapses all traffic into one bucket, and
  audit entries record the wrong IP.

**Why the env-only toggle is not enough.** `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
enables the middleware but leaves `KnownNetworks`/`KnownProxies` at their loopback default;
Railway's edge is not on loopback relative to the container, so the forwarded headers are
ignored.

**Approach for staging (safest that actually works on Railway).** Register
`ForwardedHeadersOptions` explicitly and add the middleware first in the pipeline, gated to
non-Development so local dev is untouched:

```csharp
// with the other builder.Services.Configure(...) calls
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;          // exactly one hop: Railway edge → container
    options.KnownNetworks.Clear();     // the edge IP is dynamic and not on loopback
    options.KnownProxies.Clear();
});

// immediately after `var app = builder.Build();`, BEFORE UseMiddleware<GlobalExceptionMiddleware>()
if (!app.Environment.IsDevelopment())
    app.UseForwardedHeaders();
```

- `ForwardLimit = 1` means only the single closest `X-Forwarded-*` value is honoured — a
  client-supplied `X-Forwarded-For`/`-Proto` further up the chain is discarded. On Railway
  the container is reachable only through the edge, so trusting exactly that one hop is
  correct.
- After this runs, `Request.IsHttps` reflects `X-Forwarded-Proto: https` and
  `Request.Scheme`/`RemoteIpAddress` are the client's. The HTTPS-required check then passes
  for real HTTPS traffic and still blocks a genuine plain-HTTP request.

**Production caveat (documented, not for staging):** do **not** ship `KnownProxies.Clear()`
to a future production environment without knowing the edge is the only ingress. For
production, pin `KnownNetworks`/`KnownProxies` to the platform's published proxy CIDRs (or
keep the container private and set a fixed, audited `ForwardLimit`), and never set
`ForwardLimit = null`.

This is the **only** code change in this design.

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

- A **dedicated Supabase project for staging**. No shared credentials, no network path to
  the production database. The connection string (§4) uses the direct endpoint on 5432
  with `SSL Mode=Require`.
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

No step below is executed by this document. Order:

1. **Prepare code.** On a branch off `codex/reception-backend`: add the forwarded-headers
   registration (§6), the `Dockerfile` + `.dockerignore` (§2). Build locally
   (`dotnet build`, `dotnet test`, `docker build`), verify the timezone check in the image.
   Do not push.
2. **Create the Supabase staging project.** Record the direct 5432 connection string and
   the `postgres` password. Pick the Supabase region.
3. **Apply migrations** to the staging DB from the operator machine (§8). Verify
   `__EFMigrationsHistory` head and the `unaccent` extension.
4. **Create the Railway service** from the repo/Dockerfile. Choose the region closest to
   the Supabase region.
5. **Configure the volume and environment.** Mount a persistent volume at `/data`; set all
   variables from §4; set the health check path to `/health/ready`; set replicas to 1.
6. **Deploy** the backend + embedded SPA (single build, single service).
7. **Validate `/health`** over the public HTTPS URL → `{ "status": "Healthy" }`.
8. **Validate `/health/ready`** → `{ "status": "Healthy" }` (confirms Supabase reachability
   from Railway). If it is `Unhealthy`, check `ConnectionStrings__DefaultConnection`,
   Supabase network restrictions, and SSL mode.
9. **Bootstrap the admin** (§9): `provision-roles`, then `bootstrap-admin`.
10. **Full smoke test through the public URL** — the same flow validated locally
    (GERENTE → Operating Hours + Room; PROFISSIONAL → CUSTOM availability; CUSTOMER →
    booking → reservation; QR → Totem check-in → Visit `WAITING`; RECEPTION → start → end).
    Watch the Railway logs for `400 HTTPS_REQUIRED` (forwarded headers wrong) or a
    host-filter 400 (`AllowedHosts` wrong). Confirm login works (proves first-party cookies
    over the single origin), no 500s, no request loop, no console 401 on an authenticated
    screen.

---

## 11. Out of scope (explicitly deferred)

- Production deployment (separate DB, domains, secrets, Data Protection key protection,
  possibly multi-instance → DB/blob-backed key ring — a code change at that point).
- Certificate/KMS protection of the Data Protection key ring.
- Object storage for private files instead of a volume.
- CDN / long-lived caching for hashed SPA assets. Today the global
  `Cache-Control: no-store` header (set for all responses) also lands on
  `wwwroot/assets/**`, so the browser re-fetches the JS/CSS bundle on every load. This adds
  latency on staging but is not broken; a production optimization is to exempt the hashed
  `/assets/*` path from `no-store`.
- CI/CD pipeline (there is no `.github/workflows`). Staging deploys are manual per §10.
- Making `Npgsql` command timeout configurable (currently fixed at 5 s in `Program.cs`).
- Automated staging data reset tooling.

---

## 12. Open decisions (resolve before implementation)

1. **Railway ↔ Supabase regions.** The `Npgsql` command timeout is hard-coded at 5 s
   (`Program.cs`: `postgres.CommandTimeout(5)`). If the Railway region and the Supabase
   region are far apart, cold or heavier queries (availability slot generation, dashboard,
   operational alerts) can exceed it and surface as 500s. **Decision needed:** pick a
   Railway region adjacent to the chosen Supabase region; or accept a small code change to
   read `Database:CommandTimeoutSeconds` from config (currently listed as out of scope).
2. **Supabase connection: direct 5432 vs. session pooler.** This design assumes direct
   5432 with `Maximum Pool Size=20` for a single always-on instance. Confirm the staging
   Supabase plan's direct-connection limit is comfortably above 20, or switch to the
   session pooler endpoint (still port 5432-style semantics; **not** the 6543 transaction
   pooler, which has prepared-statement caveats with Npgsql).
3. **Public host / domain.** Use the generated `*.up.railway.app` host, or attach a custom
   staging domain now? The choice sets the exact `AllowedHosts` value and, if the reschedule
   flow is ever exercised on staging, `Rescheduling__PublicBaseUrl`.
4. **`verify:production-bundle` in the Docker build.** Run it (and fail the build on
   violation) or leave it as a local pre-deploy check? Recommendation: run it in the build
   stage.
5. **Data Protection keys unencrypted at rest on the volume.** Accept for staging (this
   design's assumption) or invest in certificate protection now? Recommendation: accept for
   staging; revisit for production.
6. **Node install method in the build stage.** NodeSource `setup_22.x` vs.
   `COPY --from=node:22-bookworm-slim`. Low stakes; pick one at implementation time.
7. **Where the migration + bootstrap commands run.** A developer/operator laptop with
   network access to Supabase, or a one-shot Railway job/shell. Either works; the design
   assumes the operator machine.

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
| Forwarded headers behind an edge proxy | **Still required.** §6 — the one code change, present in both designs. |
| `AllowedHosts` localhost default | **Still required** as an env override. §4. |
| `Security__DataProtectionPath` / `Storage__PrivateFilesPath` required, persistence | **Unchanged.** §3–§4, backed by the `/data` volume. |
| Migrations out of startup; `unaccent`; head migration | **Unchanged.** §8. |
| First admin via `GestaoPredio.AdminCli` | **Unchanged.** §9. |
| Meta/Intelbras must not fire | **Unchanged.** Fail-closed stubs + `Provider=Demo`. §5. |
| Debian (not Alpine); timezone; no `InvariantGlobalization` | **Unchanged.** §2. |

No residual dependency on Vercel remains anywhere in this document.
