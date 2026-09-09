# LUMIS Staging on Railway (single origin) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put the locally-validated MVP into a staging environment served from **one Railway origin** (ASP.NET Core .NET 10 API + the React SPA from `wwwroot`, `/api/*` = backend, everything else = SPA fallback), backed by a dedicated Supabase `us-east-1` PostgreSQL project, with a persistent `/data` volume, and prove it green with the full public smoke plus a restart-persistence check.

**Architecture:**

```
Browser ── HTTPS ──▶ Railway (US East / Virginia, 1 instance)
                      ├─ ASP.NET Core .NET 10 (Kestrel, http://0.0.0.0:$PORT)
                      │   ├─ /api/*            → backend
                      │   ├─ /health, /health/ready
                      │   └─ every other path → wwwroot/index.html (SPA fallback)
                      └─ volume /data → /data/dpkeys (Data Protection) + /data/private (files)
                                     │
                                     ▼
                      Supabase PostgreSQL — STAGING project, region us-east-1
```

The application is deployed **as it is on branch `codex/reception-backend`, commit `ba00660`**. The only versioned additions are a repo-root `Dockerfile` and `.dockerignore` (TASK 1–2) and an operations runbook (`docs/operations/staging-railway-runbook.md`, appended across TASK 4–12). One conditional `Program.cs` change exists **only as a contingency** in TASK 8b and is executed only if TASK 8's forwarded-headers probe fails.

**Tech Stack:** .NET 10 (`net10.0`), Kestrel, EF Core + Npgsql, ASP.NET Core Identity (cookie auth), minimal APIs; React 18 + Vite SPA built by the existing `BuildClientApp` MSBuild target during `dotnet publish`; Docker multi-stage (`mcr.microsoft.com/dotnet/sdk:10.0` build, `mcr.microsoft.com/dotnet/aspnet:10.0` runtime, both Debian bookworm) with Node 22 copied from `node:22-bookworm-slim`; Railway (Dockerfile deploy); Supabase PostgreSQL; `dotnet ef` 10.0.11 CLI (already installed globally); `tools/GestaoPredio.AdminCli` for the first admin.

**Spec:** `docs/superpowers/specs/2026-09-08-lumis-staging-railway-design.md` (approved; spec commit `41b15e2`). The plan argues from the spec; read both. This plan **supersedes** any earlier Vercel-based deployment plan — there is no Vercel anywhere.

## Global Constraints

Every task's requirements implicitly include this section. Values copied verbatim from the spec.

- **Branch `codex/reception-backend`. No push. No production connection anywhere. No new feature. No application code change** — except the TASK 8b contingency, executed only after TASK 8 proves `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` does not behave as documented on Railway.
- **`ASPNETCORE_ENVIRONMENT=Production`** for the container and for every operator EF/CLI command.
- **Migrations never run at application startup; never `Down`; the head migration is `20260908210951_ProfessionalPresenceAndRescheduling` (12 migrations total).**
- **Region:** Railway **US East (Virginia)**; Supabase **us-east-1 (N. Virginia)**. The Npgsql command timeout stays at its hard-coded 5 s — no code change.
- **Supabase runtime connection:** **Direct Connection, port 5432** (preferred). **Fallback: Supavisor Session Mode, port 5432** — connection-string `Host` (and possibly `Username` → `postgres.<ref>`) change only. **Transaction Mode (6543) is excluded.**
- **Forwarded headers:** `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (no `Program.cs` edit). Validated by `GET https://<RAILWAY_HOST>/api/auth/csrf` → **200**. Accepting `X-Forwarded-*` from unregistered proxies is a **staging-only** decision.
- **Cookies unchanged:** `__Host-Lumis.Auth` (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, no `Domain`, 30-min sliding); `__Host-Lumis.Csrf` (`HttpOnly`, `Secure`, `SameSite=Strict`). **No `SameSite` relaxation. Antiforgery unchanged. No CORS** (`Cors:AllowedOrigins` stays `[]`; do not set `Cors__*`).
- **Notifications and Access Control:** `Notifications__Provider=Demo`, `AccessControl__Provider=Demo`. **No real WhatsApp (Meta), no real Intelbras** — leave `Notifications__Meta__*` and `AccessControl__Intelbras__*` unset.
- **Volume `/data`** → `Security__DataProtectionPath=/data/dpkeys`, `Storage__PrivateFilesPath=/data/private`. Container listens on `http://0.0.0.0:$PORT`. Data Protection keys are unencrypted at rest — **accepted for staging only**.
- **Secrets live only in Railway service variables and Supabase project settings.** Never in Git, never in the runbook, never echoed to a terminal that is logged. The only secret value is the full `ConnectionStrings__DefaultConnection` (it contains the DB password). The bootstrap admin password is typed interactively into the CLI and is never an argument, file, env var, or Git content.
- **Runtime-captured identifiers (not placeholders, not secrets):**
  - `RAILWAY_HOST` — the generated `*.up.railway.app` hostname. Captured in TASK 6, recorded in the runbook, used verbatim thereafter.
  - `SUPABASE_STAGING_HOST` — the Supabase Direct Connection host (`db.<project-ref>.supabase.co`). Captured in TASK 4. Not a secret by itself, but it only ever appears inside the secret connection string.
  - `$STAGING_CONN` — an operator **shell** variable holding the full connection string with password. Set with `read -rs STAGING_CONN` (no echo). Never written to a file, never committed, never `echo`ed.
- **Versioned changes:** each is its own small commit on `codex/reception-backend`. Commit message trailer: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`. **No push.** Runbook commits contain no secrets.
- **TDD / verification:** for the versioned code/config tasks (1, 2, 8b) each ends with an executable check that must pass before the commit. `dotnet build` clean and the relevant `dotnet test` / `npm` checks green before each commit. Operator tasks (4–7, 9–12) carry explicit expected results, failure criteria, verifications, and a runbook commit.

---

## File Structure

| File | Task | Responsibility |
|---|---|---|
| `Dockerfile` (repo root) — **create** | 1 | Multi-stage build: SDK 10 + Node 22 build stage that runs `dotnet publish` (which builds the SPA via the existing csproj target) and `npm run verify:production-bundle`; minimal `aspnet:10.0` Debian runtime that creates `/data/dpkeys` + `/data/private` and starts `dotnet recepcaototem.dll`. |
| `.dockerignore` (repo root) — **create** | 2 | Keep the build context small and secret-free while leaving everything `dotnet publish` needs (`recepcaototem.sln`, `recepcaototem/**` incl. `ClientApp/src`, `src/**`, `tools/**` optional). |
| `docs/operations/staging-railway-runbook.md` — **create, appended** | 4, 5, 6, 7, 8, 9, 10, 11, 12 | Operator record of what was created and every verification result. **No secrets.** Finalised in TASK 12. |
| `recepcaototem/Program.cs` — **modify (CONTINGENCY ONLY)** | 8b | Explicit `ForwardedHeadersOptions` + `app.UseForwardedHeaders()` gated to non-Development, using `KnownIPNetworks`. Executed **only** if TASK 8's `/api/auth/csrf` probe returns `400 HTTPS_REQUIRED` and diagnosis confirms the env toggle is ineffective. |

Infrastructure created by operator tasks (not repo files): one Supabase staging project (`us-east-1`); one Railway service (US East / Virginia) with one instance, Outbound IPv6, and a persistent `/data` volume.

---

## TASK 1: Multi-stage `Dockerfile`

**Files:**
- Create: `Dockerfile` (repo root: `C:/Users/ryan-/OneDrive/Documents/projetos/recepcaolumis/.worktrees/reception-backend/Dockerfile`)

**Interfaces — Produces:** an image whose runtime layer contains `recepcaototem.dll` + `wwwroot/index.html` + hashed `wwwroot/assets/*`, no source maps, no `src/dev`, and whose entrypoint creates `/data/dpkeys` and `/data/private` then execs `dotnet recepcaototem.dll`. Consumed by TASK 3 (local validation) and TASK 6/8 (Railway).

- [ ] **Step 1: Precheck the toolchain**

Run: `docker version` — Expected: client and server versions print (Docker Desktop running).
Run: `git -C . rev-parse --abbrev-ref HEAD` — Expected: `codex/reception-backend`.
Run: `git status --porcelain` — Expected: empty (clean tree).
Failure: Docker not running → start Docker Desktop; wrong branch → stop, do not proceed.

- [ ] **Step 2: Write `Dockerfile`**

Create `Dockerfile` at the repo root with exactly this content:

```dockerfile
# syntax=docker/dockerfile:1

# ---- Node 22 (build-time only) --------------------------------------------------
FROM node:22-bookworm-slim AS node

# ---- Build: .NET SDK 10 + Node 22 --------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ENV DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
# Bring Node 22 + npm into the SDK image (no NodeSource).
COPY --from=node /usr/local/bin/ /usr/local/bin/
COPY --from=node /usr/local/lib/node_modules/ /usr/local/lib/node_modules/
RUN node --version && npm --version
WORKDIR /src
# Restore first for layer caching.
COPY recepcaototem.sln ./
COPY recepcaototem/recepcaototem.csproj recepcaototem/
COPY src/ src/
COPY tools/ tools/
COPY tests/ tests/
RUN dotnet restore recepcaototem/recepcaototem.csproj
# Bring in the rest (ClientApp sources etc.).
COPY . .
# Publish. The csproj BuildClientApp target runs `npm ci && npm run build` in ClientApp/
# and copies ClientApp/dist/** (minus .map) into the publish output's wwwroot/.
RUN dotnet publish recepcaototem/recepcaototem.csproj -c Release -o /app/publish /p:UseAppHost=false
# Production bundle safety gate: fail the build if a dev/mock artifact leaked into dist.
RUN cd recepcaototem/ClientApp && npm run verify:production-bundle
# Prove the SPA is in the publish output.
RUN test -f /app/publish/wwwroot/index.html && ls /app/publish/wwwroot/assets/*.js >/dev/null

# ---- Runtime: ASP.NET 10 (Debian bookworm) --------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /app
COPY --from=build /app/publish ./
# /data is mounted as a volume at runtime; ensure the two subdirs exist before the app
# validates them (Production refuses a missing Storage__PrivateFilesPath).
ENTRYPOINT ["/bin/sh", "-c", "mkdir -p /data/dpkeys /data/private && exec dotnet recepcaototem.dll"]
```

Notes captured in the file above (do not add TODOs): Debian base images (ICU + `tzdata` present for `America/Porto_Velho`); `InvariantGlobalization` is **not** set; no `EXPOSE`/fixed port so `ASPNETCORE_URLS=http://0.0.0.0:$PORT` from Railway wins; `UseAppHost=false` keeps the publish output to `recepcaototem.dll` + managed deps.

- [ ] **Step 3: Build the image**

Run: `docker build -t lumis-staging:local .`
Expected: build completes; the build log shows `v22.` from `node --version`, a line from `npm --version`, and `Production bundle verifier passed: ...` from `verify:production-bundle`; the final `test -f /app/publish/wwwroot/index.html` step succeeds.
Failure criteria:
- Base image is Alpine, or `InvariantGlobalization` appears → wrong; fix the Dockerfile.
- `verify:production-bundle` throws (`Production manifest references development-only code` / `contains demonstration storage marker` / `development API proxy target`) → **do not `|| true` it**; the SPA build is producing a dev/mock bundle — stop and investigate the ClientApp build, not the Dockerfile.
- `npm ci`/`npm run build` fails inside `dotnet publish` → Node not on PATH in the build stage → fix the `COPY --from=node` lines.
- `test -f .../wwwroot/index.html` fails → the `BuildClientApp` target did not run or did not copy dist → confirm the `dotnet publish` project path and that `ClientApp/` was copied into the context.

- [ ] **Step 4: Inspect the runtime image**

Run: `docker run --rm --entrypoint sh lumis-staging:local -c 'ls -1 wwwroot | head; echo ---; ls wwwroot/assets/*.js | head; echo ---; ls -la /app/recepcaototem.dll'`
Expected: `index.html` listed under `wwwroot`; at least one hashed `wwwroot/assets/index-*.js`; `recepcaototem.dll` present.
Run: `docker run --rm --entrypoint sh lumis-staging:local -c 'grep -rl "src/dev/\|localhost:7266\|AppStore" wwwroot/assets || echo NONE'`
Expected: `NONE` (no dev/mock/localhost markers in the served bundle).
Failure: any marker found → the bundle is not the production build; stop.

- [ ] **Step 5: Commit**

```bash
git add Dockerfile
git commit -m "build: multi-stage Dockerfile for Railway staging (SDK 10 + Node 22, aspnet:10.0 runtime)"
```
Expected: one commit on `codex/reception-backend`. No push.

---

## TASK 2: `.dockerignore`

**Files:**
- Create: `.dockerignore` (repo root)

**Interfaces — Consumes:** the `Dockerfile` from TASK 1 (the `COPY . .` line). **Produces:** a build context without `bin/`, `obj/`, `node_modules/`, `dist/`, `.git/`, `.worktrees/`, secrets, or IDE cruft, while keeping `recepcaototem.sln`, `recepcaototem/` (incl. `ClientApp/` sources), `src/`, `tools/`, `tests/`.

- [ ] **Step 1: Write `.dockerignore`**

Create `.dockerignore` at the repo root with exactly:

```gitignore
# VCS / IDE / worktrees
.git
.gitignore
.vs
.vscode
.idea
.worktrees
.playwright-mcp

# Build outputs (regenerated inside the image)
**/bin
**/obj
**/node_modules
**/dist
**/.vite
artifacts
TestResults

# Docs and specs are not needed to build the app
docs

# Secrets / local-only config — must never enter the image
.env
.env.*
!.env.example
*.pfx
*.p12
*.key
**/appsettings.Local.json
**/appsettings.*.Local.json
**/secrets.json

# Container build inputs themselves
Dockerfile
.dockerignore
```

- [ ] **Step 2: Rebuild and confirm nothing needed was excluded**

Run: `docker build -t lumis-staging:local .`
Expected: build succeeds exactly as in TASK 1 Step 3 (same `verify:production-bundle` pass, same `wwwroot/index.html` check).
Run: `docker build -t lumis-staging:local . 2>&1 | grep -iE "transferring context|load build context"`
Expected: the transferred context is small (tens of MB, not hundreds) — `node_modules`/`bin`/`obj`/`.git` are excluded.
Failure criteria:
- `dotnet restore`/`dotnet publish` fails with a missing `.csproj` / `.sln` / `src/**` file → the `.dockerignore` is too aggressive; remove the offending pattern.
- `npm ci` fails because `ClientApp/package.json` or `ClientApp/src` is missing → same; do not exclude `recepcaototem/ClientApp` sources.

- [ ] **Step 3: Commit**

```bash
git add .dockerignore
git commit -m "build: .dockerignore for the Railway staging image context"
```
No push.

---

## TASK 3: Local container validation

**Files:** none (verification only). No commit unless a fix to TASK 1/2 is required (then a separate `fix:` commit).

**Interfaces — Consumes:** `lumis-staging:local` from TASK 1–2. **Produces:** evidence the image serves the SPA + `/api` + health on one origin, contains no mocks, and that the repo's own build/tests are still green.

- [ ] **Step 1: Regression guards on the source tree**

Run: `git diff --check` — Expected: no output (no whitespace errors / conflict markers).
Run: `dotnet build` — Expected: `0 Erro(s)`.
Run: `dotnet test tests/GestaoPredio.UnitTests` — Expected: all passing (baseline: 247).
Run: `cd recepcaototem/ClientApp && npm run verify:production-bundle` — Expected: `Production bundle verifier passed: ...` (requires `dist/` from a prior `npm run build`; if absent, run `npm run build` first).
Run (from `recepcaototem/ClientApp`): `npx vitest run src/features/availability src/api src/auth src/components src/pages/admin/Visits.test.tsx` — Expected: all passing.
Failure: any failure here is unrelated to Docker and blocks the task; investigate before continuing.

- [ ] **Step 2: Run the container against the local dev DB**

The app binds and serves even with an unreachable DB (`/health` stays healthy, `/health/ready` goes unhealthy). Point it at the local `LumisDev` so `/health/ready` can also be verified. Use throwaway values; nothing here is a secret.

```bash
docker run --rm -d --name lumis-stg-local -p 8099:8099 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_URLS=http://0.0.0.0:8099 \
  -e ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
  -e AllowedHosts=localhost \
  -e Security__DataProtectionPath=/data/dpkeys \
  -e Storage__PrivateFilesPath=/data/private \
  -e Scheduling__TimeZoneId=America/Porto_Velho \
  -e Notifications__Provider=Demo \
  -e AccessControl__Provider=Demo \
  -e "ConnectionStrings__DefaultConnection=Host=host.docker.internal;Port=5432;Database=LumisDev;Username=postgres;Password=<local-dev-pw>;SSL Mode=Prefer;Trust Server Certificate=true" \
  -v lumis-stg-data:/data \
  lumis-staging:local
sleep 5
docker logs lumis-stg-local
```
Expected in logs: JSON lines; `Now listening on: http://0.0.0.0:8099`; `Application started`; `Hosting environment: Production`; **no `TimeZoneNotFoundException`**, no unhandled exception, no `HTTPS_REQUIRED` at startup.
Failure: `TimeZoneNotFoundException` → wrong base image; `PrivateFileStorageOptions` validation failure → the entrypoint `mkdir` did not run or `/data` not writable; a `Security:DataProtectionPath` startup throw → env var not passed.

- [ ] **Step 3: Verify single-origin behaviour**

```bash
curl -s  -o /dev/null -w "health           %{http_code}\n" http://localhost:8099/health
curl -s  -o /dev/null -w "health/ready      %{http_code}\n" http://localhost:8099/health/ready
curl -s  -o /dev/null -w "SPA root          %{http_code}\n" http://localhost:8099/
curl -s  -o /dev/null -w "SPA deep route    %{http_code}\n" http://localhost:8099/admin/salas
curl -s  -o /dev/null -w "api session       %{http_code}\n" http://localhost:8099/api/auth/session
curl -s  -o /dev/null -w "api csrf          %{http_code}\n" http://localhost:8099/api/auth/csrf
curl -s  -o /dev/null -w "api unknown       %{http_code}\n" http://localhost:8099/api/does-not-exist
curl -s http://localhost:8099/ | grep -o '<div id="root">' | head -1
curl -s http://localhost:8099/admin/salas | grep -o '<div id="root">' | head -1
```
Expected:
- `health 200`, `health/ready 200` (local DB reachable), `SPA root 200`, `SPA deep route 200`, `api session 401`, `api csrf 200`, `api unknown 401`.
- Both `curl … | grep '<div id="root">'` print `<div id="root">` — i.e. `/` and `/admin/salas` both serve the SPA shell (fallback works).
Failure criteria:
- `api csrf` returns `400` with `{"code":"HTTPS_REQUIRED"}` **inside the container over plain HTTP** — that is expected here (there is no `X-Forwarded-Proto`); it is **not** a failure of this task. It only matters over the Railway HTTPS edge (TASK 8). Re-run with `-H 'X-Forwarded-Proto: https'` and confirm it becomes `200` — this demonstrates the env toggle path works:
  `curl -s -o /dev/null -w "%{http_code}\n" -H 'X-Forwarded-Proto: https' http://localhost:8099/api/auth/csrf` → **200**.
- `health/ready 503` → local Postgres not running or wrong password; acceptable to skip the readiness assertion locally, but note it.
- `SPA deep route` returns `404` or JSON → `MapFallbackToFile` not serving; the image's `wwwroot` is wrong.

- [ ] **Step 4: Confirm no mock/dev code in the served bundle**

```bash
docker exec lumis-stg-local sh -c 'grep -rl "src/dev/\|AppStore\|atrium_professionals\|localhost:7266" wwwroot/assets || echo NONE'
docker exec lumis-stg-local sh -c 'cat wwwroot/manifest.json | grep -o "src/dev/" || echo "manifest clean"'
```
Expected: `NONE` and `manifest clean`.
Failure: any hit → the production bundle gate did not catch a dev artifact; stop and fix the ClientApp build before proceeding (do **not** patch the Dockerfile to hide it).

- [ ] **Step 5: Tear down and record**

```bash
docker rm -f lumis-stg-local
docker volume rm lumis-stg-data
```
If Steps 2–4 required a Dockerfile/.dockerignore fix: apply it, re-run this whole task, then:
```bash
git add Dockerfile .dockerignore
git commit -m "fix: <one-line reason the staging image build needed adjusting>"
```
Otherwise: no commit for this task.

---

## TASK 4: Prepare the Supabase staging project (operator)

**Files:**
- Create: `docs/operations/staging-railway-runbook.md`

**Interfaces — Produces:** `SUPABASE_STAGING_HOST` (recorded), a confirmed `unaccent` extension, and the full connection string held only as an operator shell variable / Railway secret.

- [ ] **Step 1: Create the project**

In the Supabase dashboard: **New project**, name `lumis-staging` (or similar), **region `East US (North Virginia)` / `us-east-1`**, strong generated database password. This project is **exclusively for staging** — it shares no credentials, no schema, and no network path with any production database.
Expected result: project provisions; **Project Settings → Database** shows a **Direct connection** string `postgresql://postgres:[YOUR-PASSWORD]@db.<project-ref>.supabase.co:5432/postgres` and a **Session pooler** string on port `5432` (`...@aws-0-us-east-1.pooler.supabase.com:5432/postgres`, user `postgres.<project-ref>`).
Failure criteria: region is not `us-east-1` → delete and recreate; only a `6543` (Transaction) pooler is offered for runtime → still record the Direct + Session strings; 6543 is excluded regardless.

- [ ] **Step 2: Capture connection details safely**

In the operator shell (a local terminal that is **not** logged/screen-shared):
```bash
read -rs -p "Paste the Supabase Direct connection string (postgresql://...): " SUPABASE_URL; echo
# Convert to the Npgsql key/value form the app uses, keeping the password only in the variable:
export STAGING_CONN="$(python - <<'PY'
import os,urllib.parse as u
p=u.urlparse(os.environ["SUPABASE_URL"])
print(f"Host={p.hostname};Port={p.port or 5432};Database={p.path.lstrip('/')};Username={u.unquote(p.username)};Password={u.unquote(p.password)};SSL Mode=Require;Trust Server Certificate=true;Include Error Detail=false;Maximum Pool Size=20")
PY
)"
export SUPABASE_STAGING_HOST="$(printf '%s' "$SUPABASE_URL" | sed -E 's|.*@([^:/]+).*|\1|')"
echo "SUPABASE_STAGING_HOST=$SUPABASE_STAGING_HOST"   # host only — safe to see
```
Expected: `SUPABASE_STAGING_HOST` prints as `db.<project-ref>.supabase.co`. `STAGING_CONN` is set but **never printed**.
Failure: `python` unavailable → build the key/value string by hand in an editor buffer that is not saved; the password must not land in shell history (`read -rs` avoids that) or any file.

- [ ] **Step 3: Confirm `unaccent` availability**

The `20260905234344_PostgreSqlBaseline` migration issues `CREATE EXTENSION IF NOT EXISTS unaccent SCHEMA extensions;`, so TASK 5 installs it. Pre-check the project can host it:
```bash
psql "$STAGING_CONN" -tAc "select extname from pg_available_extensions where name='unaccent';"
```
Expected: prints `unaccent` (it is available on Supabase to the `postgres` role).
Failure: empty result → the extension is not available on this plan; **stop** and resolve with Supabase before migrating (the app's professional/customer search depends on `extensions.unaccent`).

- [ ] **Step 4: Start the runbook and commit (no secrets)**

Create `docs/operations/staging-railway-runbook.md`:

```markdown
# LUMIS Staging — Railway single origin — runbook

> Operator record. Contains **no secrets**. The DB connection string (with password) lives
> only in the operator shell (`$STAGING_CONN`) and, from TASK 7, in Railway service variables.

Spec: docs/superpowers/specs/2026-09-08-lumis-staging-railway-design.md (commit 41b15e2)
Plan: docs/superpowers/plans/2026-09-08-lumis-staging-railway.md
App:  branch codex/reception-backend, commit ba00660

## TASK 4 — Supabase staging project
- Date: <yyyy-mm-dd>
- Project name: lumis-staging
- Region: us-east-1 (East US / North Virginia)
- Project ref: <project-ref>            (identifier, not a secret)
- Direct connection host: db.<project-ref>.supabase.co : 5432
- Session pooler host:    aws-0-us-east-1.pooler.supabase.com : 5432  (fallback, user postgres.<project-ref>)
- Transaction pooler (6543): NOT USED
- unaccent available to postgres role: YES
- Connection string stored as: operator shell $STAGING_CONN (this task); Railway secret ConnectionStrings__DefaultConnection (TASK 7)
```

```bash
git add docs/operations/staging-railway-runbook.md
git commit -m "docs(ops): staging runbook — Supabase staging project record"
```
No push.

---

## TASK 5: Apply migrations to staging (operator, from the operator machine)

**Files:**
- Modify: `docs/operations/staging-railway-runbook.md` (append)

**Interfaces — Consumes:** `$STAGING_CONN` from TASK 4. **Produces:** the full schema in the staging DB with `__EFMigrationsHistory` head `20260908210951_ProfessionalPresenceAndRescheduling`, `unaccent` installed.

- [ ] **Step 1: Confirm the target is fresh and is staging**

```bash
psql "$STAGING_CONN" -tAc "select current_database(), inet_server_addr();"
psql "$STAGING_CONN" -tAc "select count(*) from information_schema.tables where table_schema='public';"
psql "$STAGING_CONN" -tAc "select to_regclass('public.\"__EFMigrationsHistory\"');"
```
Expected: database `postgres` on the Supabase host; table count `0` (brand-new project); `__EFMigrationsHistory` is `NULL` (does not exist yet).
Failure criteria: the host is not `SUPABASE_STAGING_HOST` → wrong target, **stop**; tables already exist → this is not a fresh project; do not run migrations blindly — reconcile first.

- [ ] **Step 2: Apply migrations Up**

From `C:/Users/ryan-/OneDrive/Documents/projetos/recepcaolumis/.worktrees/reception-backend`, with `ASPNETCORE_ENVIRONMENT=Production` in the environment:

```bash
ASPNETCORE_ENVIRONMENT=Production dotnet ef database update \
  --project src/GestaoPredio.Infrastructure \
  --startup-project recepcaototem \
  --context ApplicationDbContext \
  --connection "$STAGING_CONN"
```
Expected: EF logs each migration `Applying migration '2026...'`, ending at `20260908210951_ProfessionalPresenceAndRescheduling`, then `Done.`
Failure criteria:
- Connection refused / timeout to the Direct host → **do not** proceed; go to Step 4 (Supavisor Session Mode fallback) and retry this step with the fallback `STAGING_CONN`.
- Any migration errors mid-way → capture the full output, do **not** run `Down`, stop and report.
- `CREATE EXTENSION unaccent` permission denied → stop (TASK 4 Step 3 should have caught this).

Alternative (review-then-run) if a direct `database update` is not permitted by policy:
```bash
ASPNETCORE_ENVIRONMENT=Production dotnet ef migrations script --idempotent \
  --project src/GestaoPredio.Infrastructure --startup-project recepcaototem \
  --context ApplicationDbContext -o artifacts/sql/staging-up.sql
# review artifacts/sql/staging-up.sql (additive only; no DROP/destructive ALTER), then:
psql "$STAGING_CONN" -1 -f artifacts/sql/staging-up.sql
```
(`artifacts/` is git-ignored; the script is not committed.)

- [ ] **Step 3: Verify the schema**

```bash
psql "$STAGING_CONN" -tAc 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId";'
psql "$STAGING_CONN" -tAc "select count(*) from \"__EFMigrationsHistory\";"
psql "$STAGING_CONN" -tAc "select extname, extnamespace::regnamespace::text from pg_extension where extname='unaccent';"
psql "$STAGING_CONN" -tAc "select count(*) from information_schema.columns where table_name='Reservations' and column_name='CancellationReason';"
psql "$STAGING_CONN" -tAc "select count(*) from information_schema.tables where table_schema='public' and table_name in ('ProfessionalPresence','ProfessionalPresenceTokens','RescheduleTokens');"
```
Expected:
- 12 rows, last = `20260908210951_ProfessionalPresenceAndRescheduling`.
- count `12`.
- `unaccent | extensions`.
- `1` (the presence/rescheduling migration column landed).
- `3` (the three new tables exist).
Failure: head is not `20260908210951_...`, or count ≠ 12, or `unaccent` missing / wrong schema, or the column/table checks ≠ 1/3 → **stop**, do not deploy.

- [ ] **Step 4: (only if Step 2 failed to connect) switch to Supavisor Session Mode**

Rebuild `STAGING_CONN` with the **Session pooler** host (port 5432) and user `postgres.<project-ref>`:
```bash
export STAGING_CONN="Host=aws-0-us-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<same-password>;SSL Mode=Require;Trust Server Certificate=true;Include Error Detail=false;Maximum Pool Size=20"
```
Re-run Step 2 and Step 3 with this value. Record in the runbook that the **Session pooler** is in use and why. **Never** use port 6543.

- [ ] **Step 5: Append to the runbook and commit**

Append:
```markdown
## TASK 5 — Migrations
- Date: <yyyy-mm-dd>
- Command: dotnet ef database update (Up) via <Direct 5432 | Supavisor Session 5432>
- __EFMigrationsHistory head: 20260908210951_ProfessionalPresenceAndRescheduling  (12 rows)
- unaccent: installed in schema extensions
- Down: NOT run
- Runtime connection mode chosen: <Direct 5432 | Supavisor Session 5432>  (recorded here for TASK 7)
```
```bash
git add docs/operations/staging-railway-runbook.md
git commit -m "docs(ops): staging runbook — migrations applied, head 20260908210951"
```
No push.

---

## TASK 6: Create the Railway service (operator)

**Files:**
- Modify: `docs/operations/staging-railway-runbook.md` (append)

**Interfaces — Produces:** `RAILWAY_HOST` (the `*.up.railway.app` hostname), a running-but-unconfigured service, a `/data` volume.

- [ ] **Step 1: Create the service from the Dockerfile**

Railway → **New Project** → **Deploy from GitHub repo** → this repo, branch `codex/reception-backend`. Railway detects the repo-root `Dockerfile` (builder = Dockerfile; do not use Nixpacks).
Project/environment: name it `lumis-staging`.
**Region: `US East (Virginia)` / `us-east4` (or the closest US-East option Railway offers).**
Failure criteria: builder resolves to Nixpacks → set it to Dockerfile explicitly; region is not US East → change it before the first deploy.

- [ ] **Step 2: Service settings**

- **Instances / replicas: 1** (`Settings → Deploy → Replicas = 1`).
- **Outbound IPv6: enabled** (`Settings → Networking → Outbound IPv6`).
- **Health check path:** `/health/ready` (`Settings → Deploy → Healthcheck Path`). Healthcheck timeout ≥ 30 s.
- **Restart policy:** on-failure (default is fine).
- **Public domain:** generate the Railway domain (`Settings → Networking → Public Networking → Generate Domain`). Record it as `RAILWAY_HOST` (e.g. `lumis-staging-production.up.railway.app`). **No custom domain.**

- [ ] **Step 3: Create and mount the volume**

`Settings → Volumes → New Volume`, mount path **`/data`**, size ≥ 1 GB. Attach to this service.
Expected: the volume shows mounted at `/data`.
Failure: no volume support on the plan → **stop**; the design requires a persistent `/data` (Data Protection keys + private files).

- [ ] **Step 4: Append to the runbook and commit**

Append:
```markdown
## TASK 6 — Railway service
- Date: <yyyy-mm-dd>
- Project/env: lumis-staging
- Region: US East (Virginia)
- Replicas: 1
- Outbound IPv6: enabled
- Volume: /data (>= 1 GB), persistent
- Healthcheck path: /health/ready
- RAILWAY_HOST: <the-generated-host>.up.railway.app     (used verbatim in TASK 7/8/10/11)
- Custom domain: none
```
```bash
git add docs/operations/staging-railway-runbook.md
git commit -m "docs(ops): staging runbook — Railway service, RAILWAY_HOST recorded"
```
No push.

---

## TASK 7: Configure Railway environment variables (operator)

**Files:**
- Modify: `docs/operations/staging-railway-runbook.md` (append — **without the connection string value**)

**Interfaces — Consumes:** `RAILWAY_HOST` (TASK 6), `STAGING_CONN` (TASK 4/5, secret). **Produces:** a fully configured service ready to deploy.

- [ ] **Step 1: Set the required variables**

In `Settings → Variables`, add exactly these. `$PORT` is Railway-provided — write it literally as `$PORT` (Railway expands it).

| Variable | Value | Secret? |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | no |
| `ASPNETCORE_URLS` | `http://0.0.0.0:$PORT` | no |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | no |
| `ConnectionStrings__DefaultConnection` | the full value of `$STAGING_CONN` (the chosen mode from TASK 5 Step 5) | **YES — secret** |
| `AllowedHosts` | `RAILWAY_HOST` exactly, e.g. `lumis-staging-production.up.railway.app` (no scheme, no path, no `*`) | no |
| `Security__DataProtectionPath` | `/data/dpkeys` | no |
| `Storage__PrivateFilesPath` | `/data/private` | no |
| `Scheduling__TimeZoneId` | `America/Porto_Velho` | no |
| `Notifications__Provider` | `Demo` | no |
| `AccessControl__Provider` | `Demo` | no |
| `Rescheduling__PublicBaseUrl` | `https://RAILWAY_HOST` (with scheme, no trailing slash) | no |

**Do not set:** any `Cors__*`, any `Notifications__Meta__*`, any `AccessControl__Intelbras__*`, `ASPNETCORE_HTTP_PORTS` (leave `ASPNETCORE_URLS` to own the port).
Optional: `Logging__LogLevel__Default=Information` (staging visibility).

- [ ] **Step 2: Sanity-check the values**

- `AllowedHosts` has no `https://`, no trailing slash, no `*`.
- `Rescheduling__PublicBaseUrl` is `https://` + the **same** host as `AllowedHosts`, no trailing slash.
- `ConnectionStrings__DefaultConnection` `Host=` matches the mode recorded in the runbook (Direct `db.<ref>.supabase.co` **or** Session `aws-0-us-east-1.pooler.supabase.com`), `Port=5432`, `SSL Mode=Require`.
Failure: a `Port=6543` anywhere → wrong; fix to 5432 (Direct or Session).

- [ ] **Step 3: Append to the runbook and commit (redacted)**

Append (the connection string value is **not** written — only its shape and that it is a Railway secret):
```markdown
## TASK 7 — Railway env vars
- Date: <yyyy-mm-dd>
- Set (non-secret): ASPNETCORE_ENVIRONMENT=Production, ASPNETCORE_URLS=http://0.0.0.0:$PORT,
  ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, AllowedHosts=<RAILWAY_HOST>,
  Security__DataProtectionPath=/data/dpkeys, Storage__PrivateFilesPath=/data/private,
  Scheduling__TimeZoneId=America/Porto_Velho, Notifications__Provider=Demo,
  AccessControl__Provider=Demo, Rescheduling__PublicBaseUrl=https://<RAILWAY_HOST>
- Set (SECRET, value not recorded): ConnectionStrings__DefaultConnection  (Supabase <Direct 5432 | Session 5432>)
- Not set: Cors__*, Notifications__Meta__*, AccessControl__Intelbras__*
```
```bash
git add docs/operations/staging-railway-runbook.md
git commit -m "docs(ops): staging runbook — Railway env vars (connection string redacted)"
```
No push.

---

## TASK 8: First deploy + forwarded-headers probe (operator)

**Files:**
- Modify: `docs/operations/staging-railway-runbook.md` (append)

**Interfaces — Consumes:** the configured service. **Produces:** a running staging deployment with `/health` 200, `/health/ready` 200, and — the critical gate — `GET /api/auth/csrf` 200.

- [ ] **Step 1: Deploy**

Trigger a deploy (push is out of scope; use Railway's "Deploy" / redeploy from the connected branch's current commit, or `railway up` from the operator machine). Watch the build and deploy logs.
Expected: image builds (same `verify:production-bundle` pass), container starts; deploy logs show JSON lines including `Now listening on: http://0.0.0.0:<PORT>`, `Application started`, `Hosting environment: Production`.
Failure criteria: **any 500 or unhandled exception at startup**; `TimeZoneNotFoundException`; `Security:DataProtectionPath must be configured` (env var missing); `PrivateFileStorageOptions` validation failure (`/data` not writable — check the volume mount and the entrypoint `mkdir`).

- [ ] **Step 2: Health**

```bash
curl -s -w "\n%{http_code}\n" https://RAILWAY_HOST/health
curl -s -w "\n%{http_code}\n" https://RAILWAY_HOST/health/ready
```
Expected: both `{"status":"Healthy"}` with `200`.
Failure — `/health/ready` `Unhealthy`/`503`: the container cannot reach Supabase. Check, in order: `ConnectionStrings__DefaultConnection` value; Outbound IPv6 enabled; `SSL Mode=Require`; Supabase network restrictions. **If the Direct host is the failure, switch `ConnectionStrings__DefaultConnection` `Host` to the Supavisor Session Mode host (port 5432)** and redeploy; re-run this step. Never 6543.

- [ ] **Step 3: `/data` writability and Data Protection**

```bash
curl -s https://RAILWAY_HOST/api/auth/csrf -c /tmp/lumis-cookies.txt -w "\n%{http_code}\n"
```
Expected: `200` and a JSON body `{"token":"..."}`; `/tmp/lumis-cookies.txt` now holds a `__Host-Lumis.Csrf` cookie with `Secure`.
This request forces the app to create a Data Protection key on first use. Confirm it landed on the volume:
Railway shell (`railway run` / the service's shell) → `ls -la /data/dpkeys` → expected: one or more `key-*.xml` files. `ls -la /data/private` → exists, writable.
Failure: no key file under `/data/dpkeys` → the key ring is not persisting (wrong `Security__DataProtectionPath`, or the volume is not mounted).

- [ ] **Step 4: CRITICAL — forwarded-headers gate**

The `curl` in Step 3 already exercised it. Restate the assertion:
```bash
curl -s -o /dev/null -w "%{http_code}\n" https://RAILWAY_HOST/api/auth/csrf
```
Expected: **`200`**.

If it is **`400`** with body `{"code":"HTTPS_REQUIRED"}`:
1. **Do not apply a workaround yet.** Prove the env toggle is not working as documented:
   - Add a temporary diagnostic only if needed: confirm the request truly arrives over HTTPS at the edge (`curl -I https://RAILWAY_HOST/health` shows HTTP/2 200) and that Railway is sending `X-Forwarded-Proto` (Railway's Envoy edge does).
   - Confirm `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is exactly `true` (not `True`/`1`/quoted) in `Settings → Variables`, and that the deploy that is live actually has it (redeploy if it was added after the running deploy).
   - Check the deploy logs for a startup line about Forwarded Headers / known proxies.
2. Only after that diagnosis confirms the toggle is ineffective on Railway, proceed to **TASK 8b** (a separate, minimal `Program.cs` change per the spec contingency). Do not skip the diagnosis.

- [ ] **Step 5: No-500 sweep + append to the runbook and commit**

```bash
for p in /health /health/ready / /admin/salas /api/auth/session /api/auth/csrf /api/does-not-exist ; do
  printf "%-24s " "$p"; curl -s -o /dev/null -w "%{http_code}\n" "https://RAILWAY_HOST$p" ; done
```
Expected: `200 200 200 200 401 200 401` — and **no `5xx` anywhere**.
Append:
```markdown
## TASK 8 — First deploy
- Date: <yyyy-mm-dd>
- Deploy: OK, no 500 at startup, Hosting environment: Production
- /health: 200   /health/ready: 200   (DB mode in use: <Direct 5432 | Session 5432>)
- /data/dpkeys: key-*.xml present (Data Protection persisting on the volume)
- /data/private: exists, writable
- GET /api/auth/csrf: 200   (forwarded headers OK via ASPNETCORE_FORWARDEDHEADERS_ENABLED=true)
- TASK 8b contingency: <not needed | applied, see commit ...>
```
```bash
git add docs/operations/staging-railway-runbook.md
git commit -m "docs(ops): staging runbook — first deploy healthy, forwarded-headers probe passed"
```
No push.

---

## TASK 8b: Forwarded-headers contingency — CONTINGENCY ONLY

**Run this task only if TASK 8 Step 4 returned `400 HTTPS_REQUIRED` and Step 4's diagnosis confirmed `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is ineffective on Railway.** Otherwise skip entirely.

**Files:**
- Modify: `recepcaototem/Program.cs`
- Create: `tests/GestaoPredio.IntegrationTests/ForwardedHeadersTests.cs`

**Interfaces — Produces:** an explicit `ForwardedHeadersOptions` registration + `app.UseForwardedHeaders()` gated to non-Development, so a request carrying `X-Forwarded-Proto: https` is treated as HTTPS and is not rejected by the `HTTPS_REQUIRED` middleware.

- [ ] **Step 1: Write the failing test**

`tests/GestaoPredio.IntegrationTests/ForwardedHeadersTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.TestHost;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ForwardedHeadersTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Csrf_endpoint_is_reachable_when_the_edge_marks_the_request_https()
    {
        await factory.ResetAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        var response = await factory.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // not 400 HTTPS_REQUIRED
    }
}
```

Note: `ModulesApiFactory` sets `builder.UseEnvironment("Testing")`, and the `HTTPS_REQUIRED` middleware runs for non-Development, so this test exercises the real gate. If the test harness's `TestServer` does not surface the scheme flip, gate the middleware in `Program.cs` on `!IsDevelopment()` (already the case) and rely on the explicit `UseForwardedHeaders()` added in Step 3.

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter FullyQualifiedName~ForwardedHeadersTests`
Expected: FAIL — `400` instead of `200` (the `X-Forwarded-Proto` header is not honoured without explicit registration).

- [ ] **Step 3: Implement the explicit registration in `Program.cs`**

Add, with the other `builder.Services.*` calls (near the antiforgery/data-protection block):

```csharp
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    // Railway's edge address is dynamic; the container is only reachable through it.
    // KnownNetworks is obsolete in .NET 10 — use KnownIPNetworks.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
```

And immediately after `var app = builder.Build();`, before `app.UseMiddleware<GlobalExceptionMiddleware>();`:

```csharp
if (!app.Environment.IsDevelopment())
    app.UseForwardedHeaders();
```

- [ ] **Step 4: Run — expect PASS + no regression**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter FullyQualifiedName~ForwardedHeadersTests` → PASS.
Run: `dotnet build` → `0 Erro(s)`.
Run: `dotnet test tests/GestaoPredio.UnitTests` → all passing (247).
Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter "FullyQualifiedName~AuthApi|FullyQualifiedName~AntiforgeryTests|FullyQualifiedName~SecurityTests"` → all passing.
Run: `git diff --check` → no output.

- [ ] **Step 5: Commit, rebuild image, redeploy, re-probe**

```bash
git add recepcaototem/Program.cs tests/GestaoPredio.IntegrationTests/ForwardedHeadersTests.cs
git commit -m "fix: honour X-Forwarded-Proto/-For explicitly (Railway edge; ASPNETCORE_FORWARDEDHEADERS_ENABLED insufficient)"
```
No push. Then rebuild `lumis-staging:local` (TASK 1 Step 3), redeploy on Railway (TASK 8 Step 1), and repeat TASK 8 Step 4 — `GET https://RAILWAY_HOST/api/auth/csrf` must now be `200`.
Update the runbook's TASK 8 entry: `TASK 8b contingency: applied, commit <sha>`.

---

## TASK 9: Bootstrap roles + first admin (operator, from the operator machine)

**Files:**
- Modify: `docs/operations/staging-railway-runbook.md` (append — **no password**)

**Interfaces — Consumes:** `$STAGING_CONN` (the mode chosen in TASK 5). **Produces:** the five auth roles and exactly one `ADMINISTRADOR` user in the staging DB. Runs **after** TASK 5.

- [ ] **Step 1: Publish the CLI**

```bash
dotnet publish tools/GestaoPredio.AdminCli/GestaoPredio.AdminCli.csproj -c Release -o artifacts/tools/AdminCli
```
Expected: `artifacts/tools/AdminCli/GestaoPredio.AdminCli.dll` produced. (`artifacts/` is git-ignored; the CLI is not committed and not part of the Railway image.)

- [ ] **Step 2: Provision roles**

```bash
export ConnectionStrings__DefaultConnection="$STAGING_CONN"
export ASPNETCORE_ENVIRONMENT=Production
dotnet artifacts/tools/AdminCli/GestaoPredio.AdminCli.dll provision-roles
```
Expected: `Roles de autenticação provisionadas.` (exit 0).
Verify:
```bash
psql "$STAGING_CONN" -tAc 'select "Name" from "AspNetRoles" order by "Name";'
```
Expected: `ADMINISTRADOR`, `CUSTOMER`, `GERENTE`, `PROFESSIONAL_APPLICANT`, `PROFISSIONAL` (5 rows).
Failure: `ConnectionStringGuard` rejects the connection → confirm `ASPNETCORE_ENVIRONMENT=Production` is exported (the guard only restricts Development/Testing).

- [ ] **Step 3: Bootstrap the first admin**

```bash
dotnet artifacts/tools/AdminCli/GestaoPredio.AdminCli.dll bootstrap-admin
```
Enter, at the interactive prompts: display name; e-mail; **password (typed, no echo)**; confirm password. The password must be ≥ 12 chars with an uppercase, a lowercase, a digit, and a non-alphanumeric character. **The password is never a CLI argument, never a file, never an env var, never committed.**
Expected: `Administrador inicial criado.` (exit 0).
Verify:
```bash
psql "$STAGING_CONN" -tAc 'select count(*) from "AspNetUsers";'
psql "$STAGING_CONN" -tAc 'select u."Email", r."Name" from "AspNetUsers" u join "AspNetUserRoles" ur on ur."UserId"=u."Id" join "AspNetRoles" r on r."Id"=ur."RoleId";'
psql "$STAGING_CONN" -tAc "select \"Action\",\"Result\" from \"AuditEntries\" where \"Action\" like '%ADMIN%' or \"Action\" like '%BOOTSTRAP%' order by \"OccurredAt\" desc limit 3;"
```
Expected: `AspNetUsers` count `1`; the user is mapped to `ADMINISTRADOR`; an audit row exists for the bootstrap.
Failure: `InvalidInput` → password policy not met, or e-mail malformed; retry. `AlreadyProvisioned` → an admin already exists (only expected on a re-run).

- [ ] **Step 4: Append to the runbook and commit (no password)**

Append:
```markdown
## TASK 9 — Bootstrap
- Date: <yyyy-mm-dd>
- provision-roles: OK (5 roles: ADMINISTRADOR, CUSTOMER, GERENTE, PROFESSIONAL_APPLICANT, PROFISSIONAL)
- bootstrap-admin: OK — first ADMINISTRADOR created (email: <the-admin-email>; password NOT recorded)
- AspNetUsers count: 1
- Audit row for bootstrap: present
```
```bash
git add docs/operations/staging-railway-runbook.md
git commit -m "docs(ops): staging runbook — roles provisioned and first admin created"
```
No push.

---

## TASK 10: Public smoke test (operator, via `https://RAILWAY_HOST`)

**Files:**
- Modify: `docs/operations/staging-railway-runbook.md` (append)

**Interfaces — Consumes:** the deployed service + the bootstrap admin. **Produces:** a pass/fail record of the full MVP flow on staging, mirroring the local smoke.

Run the flow in a real browser against `https://RAILWAY_HOST` (a clean profile / no extensions, to keep the console readable). Watch the browser Network + Console throughout.

- [ ] **Step 1: AUTH — GERENTE/ADMIN**
  - Open `https://RAILWAY_HOST/login`, sign in as the bootstrap admin.
  - Expected: `POST /api/auth/login → 204`, `GET /api/auth/session → 200`, the admin dashboard renders.
  - Check: the auth cookie in DevTools → `__Host-Lumis.Auth`, `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`, no `Domain`.

- [ ] **Step 2: OPERATING HOURS — load / save / persist**
  - `/admin/configuracoes` → `GET /api/admin/operating-hours 200`, `GET /api/admin/rooms?status=active 200`.
  - Make a change (e.g. toggle one day) → **Salvar horário** → `PUT /api/admin/operating-hours 200`.
  - **F5** → the change persisted (no "Descartar alterações"). Confirm at least one **active Room** exists (or create one in `/admin/salas` → `POST /api/admin/rooms 201`).

- [ ] **Step 3: PROFESSIONAL — login → CUSTOM availability → save → F5 → persist**
  - Sign out; sign in as a professional (create one first via `/admin/profissionais` if none is linked; link a user).
  - `/profissional/disponibilidade` → `GET /api/professional/availability 200`, `.../exceptions?from=&to= 200`.
  - Select **Usar horário personalizado**; add periods on one weekday (two non-overlapping periods, e.g. `09:00–12:00` and `14:00–17:00`); the "Estabelecimento:" hint shows the **building** hours (`GET .../availability` payload has `globalDays`).
  - **Salvar disponibilidade** → `PUT /api/professional/availability 200`. **F5** → mode + periods persisted; no form reset.

- [ ] **Step 4: CUSTOMER — login → slots → reserve → confirm**
  - Sign out; sign in as a customer (create via `/cliente/cadastro` or `/admin` user administration).
  - `/cliente/agendar` → choose that professional, a date on the configured weekday, `durationMinutes` — `GET /api/customer/availability 200` returns slots matching the CUSTOM availability ∩ operating hours ∩ an active room.
  - Pick a slot → **Confirmar agendamento** → `POST /api/customer/reservations 201`.
  - `/cliente/agendamentos` and the detail page show **Confirmado**, correct room, correct date/time.

- [ ] **Step 5: PROFESSIONAL — the reservation appears**
  - Sign in as the professional → `/profissional/agenda` → the reservation from Step 4 is listed (`APPROVED`, correct room + time). `GET /api/professional/reservations 200`.

- [ ] **Step 6: CUSTOMER — QR**
  - Sign in as the customer → open the reservation → **Gerar QR Code** → `POST /api/customer/reservations/{id}/check-in-token 200`; the QR renders; a "Válido até …" is shown.
  - The check-in window is `[StartAt − 1h, EndAt)`. If the reservation is not yet in that window, either book a nearer slot (Steps 3–4 with a time inside operating hours starting within the next hour) or note that Steps 6–9 run at the scheduled time.

- [ ] **Step 7: TOTEM — resolve → confirm**
  - `https://RAILWAY_HOST/totem/check-in` (anonymous) → **Digitar código** → paste the dev code → **Validar agendamento** → `POST /api/totem/check-in/resolve 200` (preview shows the professional + time).
  - **Confirmar chegada** → `POST /api/totem/check-in/confirm 200` → "Chegada registrada".

- [ ] **Step 8: VISIT — AGUARDANDO**
  - As GERENTE/ADMIN → `/admin/recepcao` → the visit appears in "Fila de atendimento" (Aguardando). Confirm via `psql "$STAGING_CONN" -tAc "select \"Status\" from \"Visits\" where \"ReservationId\"='<id>';"` → `Waiting`.

- [ ] **Step 9: RECEPTION — iniciar → EM_ATENDIMENTO → encerrar → ENCERRADA**
  - **Iniciar** → `POST /api/reception/visits/{id}/start 200` → row shows "Encerrar"; counters Aguardando−1 / Em atendimento+1.
  - **Encerrar** → `POST /api/reception/visits/{id}/end 200` → leaves the queue.
  - Confirm: `psql "$STAGING_CONN" -tAc "select \"Status\", \"ServiceStartedAt\" is not null, \"EndedAt\" is not null from \"Visits\" where \"Id\"='<visit-id>';"` → `Ended | t | t`.

- [ ] **Step 10: Whole-smoke assertions**

Throughout Steps 1–9, verify:
- **No `5xx`** on any request.
- **No `401` on an authenticated screen** (401 only at the login page / while anonymous is expected).
- **No request loop** — the `/api/reception/overview` + `/api/reception/visits` pair every ~10 s is the intended poll; anything growing unbounded is a fail.
- **No mock** — the dashboard and every screen show real data; `view-source` of the JS bundle has no `src/dev` / `AppStore` / `atrium_*` markers.
- **Cookies `Secure`** and sent on same-origin XHR; **antiforgery** works (mutations carry `X-CSRF-TOKEN`; no `INVALID_CSRF`).
- **Times render in `America/Porto_Velho`** (e.g. a 14:00 local reservation shows 14:00, not 18:00 UTC).

- [ ] **Step 11: Append to the runbook and commit**

Append a PASS/FAIL line per step (1–10) plus the assertion results. Include the reservation id and visit id used.
```bash
git add docs/operations/staging-railway-runbook.md
git commit -m "docs(ops): staging runbook — public smoke result"
```
No push.

---

## TASK 11: Restart / redeploy persistence check (operator)

**Files:**
- Modify: `docs/operations/staging-railway-runbook.md` (append)

**Interfaces — Produces:** proof that Data Protection keys and `/data/private` survive an instance restart, so sessions are not silently invalidated on redeploy.

- [ ] **Step 1: Establish a session and a private artifact**

- In the browser, sign in as the professional; upload a professional photo (`/admin/profissionais` → edit → photo → `PUT /api/admin/professionals/{id}/photo` or the professional's own photo endpoint) → `200`. Note the photo URL.
- Keep the browser tab open (holding the `__Host-Lumis.Auth` cookie).
- Railway shell: `ls /data/dpkeys` → note the `key-*.xml` file name(s); `ls /data/private` → note the stored file(s).

- [ ] **Step 2: Controlled restart**

Railway → the service → **Restart** (not a rebuild; a restart of the running deployment). Wait for `/health` → `200`.

- [ ] **Step 3: Verify persistence**

- Railway shell: `ls /data/dpkeys` → **the same `key-*.xml` file(s)** as before (not regenerated); `ls /data/private` → the same stored file(s).
- In the still-open browser tab, navigate to an authenticated screen (or hit `GET /api/auth/session`) → **`200`, still authenticated** (the cookie decrypted with the persisted key). Load the professional photo URL → `200`.
- `curl -s -o /dev/null -w "%{http_code}\n" https://RAILWAY_HOST/health/ready` → `200`.
Failure criteria:
- The browser session is now `401` / redirected to login after the restart → Data Protection keys did **not** persist (wrong `Security__DataProtectionPath` or the volume is not truly persistent) → **staging is not green**; fix before TASK 12.
- The uploaded photo 404s after restart → `/data/private` not persistent → same.

- [ ] **Step 4: (optional) redeploy check**

Trigger a redeploy (new image, same commit). After it is healthy, repeat Step 3's browser check — the session must still be valid, because the key ring on `/data/dpkeys` is unchanged across image swaps.

- [ ] **Step 5: Append to the runbook and commit**

Append:
```markdown
## TASK 11 — Restart / redeploy persistence
- Date: <yyyy-mm-dd>
- Pre-restart key file(s): key-<...>.xml ; /data/private file(s): <...>
- After Restart: same key file(s), same private file(s); browser session still 200 (authenticated); photo 200
- After redeploy (optional): session still 200
- /health/ready after restart: 200
- Result: PASS
```
```bash
git add docs/operations/staging-railway-runbook.md
git commit -m "docs(ops): staging runbook — restart/redeploy persistence PASS"
```
No push.

---

## TASK 12: Closeout

**Files:**
- Modify: `docs/operations/staging-railway-runbook.md` (finalise)

**Interfaces — Produces:** a single finalised record; an explicit statement that staging is **not** promoted to production.

- [ ] **Step 1: Finalise the runbook**

Append a `## Closeout` section:

```markdown
## Closeout
- Staging URL:            https://<RAILWAY_HOST>
- Railway region:         US East (Virginia)
- Railway instances:      1
- Railway volume:         /data (persistent) → /data/dpkeys, /data/private
- Supabase region:        us-east-1 (N. Virginia)
- Supabase runtime mode:  <Direct Connection 5432 | Supavisor Session Mode 5432>
- Migration head:         20260908210951_ProfessionalPresenceAndRescheduling  (12 migrations)
- Notifications provider:  Demo   (no real WhatsApp; Meta adapter is a no-op stub, Notifications__Meta__* unset)
- AccessControl provider:  Demo   (no real Intelbras; Intelbras adapter is a no-op stub, AccessControl__Intelbras__* unset)
- Forwarded headers:       ASPNETCORE_FORWARDEDHEADERS_ENABLED=true  (<no contingency | Program.cs contingency applied: commit ...>)
- Data Protection at rest: unencrypted on the /data volume — ACCEPTED FOR STAGING ONLY
- Public smoke:            <PASS | notes>
- Restart persistence:     <PASS | notes>

### Remaining risks to address before production (NOT done here)
- Data Protection key protection: certificate/KMS protector, or DB/blob-backed key ring (the latter enables multi-instance).
- Forwarded-headers trust: pin to the platform's proxy addresses/networks (KnownIPNetworks / KnownProxies; KnownNetworks is obsolete) instead of accepting from any upstream.
- Private files on ephemeral-adjacent storage: move to object storage.
- Npgsql command timeout fixed at 5 s: make configurable only if a real timeout is observed.
- Global `Cache-Control: no-store` also covers hashed SPA assets — exempt /assets/* for production.
- No CI/CD: staging deploys are manual.
- Separate production DB, domains, secrets, and a production admin bootstrap are still required.

**Staging is NOT promoted to production. Promotion is a separate, explicitly-approved effort.**
```
Fill every `<...>` from the earlier task records.

- [ ] **Step 2: Verify the runbook has no secrets**

Run: `grep -nE "Password=|password|postgres://|postgresql://|secret|token" docs/operations/staging-railway-runbook.md`
Expected: no line contains an actual credential (matches are only the words "password NOT recorded", "secret" in "Railway secret", etc.).
Failure: any real value present → redact and re-check before committing.

- [ ] **Step 3: Commit**

```bash
git add docs/operations/staging-railway-runbook.md
git commit -m "docs(ops): staging runbook — closeout; staging green, not promoted to production"
git log --oneline ba00660..HEAD
```
Expected: the log shows the Dockerfile/.dockerignore commits, the runbook commits, and (if it happened) the TASK 8b `fix:` commit. **No push.**

---

## Self-Review

**1. Spec coverage**

| Spec section | Task(s) |
|---|---|
| §1 Architecture — single origin, SPA from `wwwroot`, `/api/*` backend, fallback | 1 (image), 3 (proven locally), 8/10 (proven on Railway) |
| §2 Build — multi-stage, Debian runtime, Node 22 via `node:22-bookworm-slim`, `node/npm --version`, `dotnet restore`/`publish`, csproj builds ClientApp, `verify:production-bundle` fails build, minimal runtime, `/data` dirs at startup, `dotnet recepcaototem.dll` | 1 |
| §2 `.dockerignore` — exclude bin/obj/node_modules/.git/.worktrees/artifacts/secrets/dev; keep publish inputs | 2 |
| §3 Railway — US East (Virginia), 1 instance, Outbound IPv6, `/data` volume, generated host, `0.0.0.0:$PORT`, healthcheck `/health/ready` | 6 (create), 7 (`ASPNETCORE_URLS`), 8/11 (verify) |
| §4 Config — every env var incl. `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, `AllowedHosts`=exact host, `Rescheduling__PublicBaseUrl`; secret = connection string only; not-set list | 7 |
| §5 Security — cookies unchanged, no CORS, antiforgery unchanged, Meta/Intelbras off, secrets only in Railway/Supabase, DP-at-rest staging-only | 7 (providers), 10 (cookie/antiforgery/no-mock checks), 11 (DP persistence), 12 (record) |
| §6 Forwarded headers — env toggle, no `Program.cs` edit; `/api/auth/csrf` == 200; staging-only acceptance; contingency | 8 (probe), 8b (contingency only), 12 (record) |
| §7 Health — `/health` liveness, `/health/ready` readiness + PostgreSQL | 3 (local), 8 + 11 (Railway) |
| §8 Database — dedicated Supabase `us-east-1`, no prod path, Direct 5432 primary + Supavisor Session 5432 fallback, never 6543, migrations out of startup, head `20260908210951`, `unaccent` | 4, 5 |
| §9 Bootstrap — migrations → `provision-roles` → `bootstrap-admin`, interactive password only | 5 then 9 |
| §10 Deploy order | TASK order 1→12 (with 8a DB fallback inside 5/8 and 8b contingency inside 8) |
| §11 Out of scope | 12 "Remaining risks" section; no task attempts any of them |
| §12 Decisions status — no blocking decision; two implementation-time confirmations with no-code contingencies | 8 Step 4 (toggle) + 8b; 5 Step 4 / 8 Step 2 (Direct→Session) |
| §13 Self-review vs. Vercel — no Vercel, single origin, no code change (bar contingency) | whole plan; verified in this section's point 5 |

Every spec requirement maps to a task.

**2. Placeholder scan** — no "TBD"/"TODO"/"handle edge cases"/"similar to Task N". The `Dockerfile` and `.dockerignore` are given in full. Every operator step lists exact commands, the exact expected output, and explicit failure criteria. `<yyyy-mm-dd>`, `<project-ref>`, `RAILWAY_HOST`, `SUPABASE_STAGING_HOST`, `$STAGING_CONN`, `<the-admin-email>` are **runtime-captured identifiers or secrets**, defined once in Global Constraints — they are values that cannot exist until the resource is created, not unfilled placeholders. The runbook template lines with `<...>` are the operator's fill-ins during execution, each with a named source task.

**3. Type / name consistency** — `recepcaototem/recepcaototem.csproj`, `recepcaototem.sln`, `src/GestaoPredio.Infrastructure`, `ApplicationDbContext`, `tools/GestaoPredio.AdminCli/GestaoPredio.AdminCli.csproj`, `docs/operations/staging-railway-runbook.md`, `lumis-staging:local`, `/data/dpkeys`, `/data/private`, `RAILWAY_HOST`, `$STAGING_CONN`, `20260908210951_ProfessionalPresenceAndRescheduling` are used identically in every task. Env var names match `Program.cs` / `IdentityConfiguration.cs` / `NotificationServices.cs` / `AccessControlServices.cs` / `PrivateFileStorageOptions` exactly (`Security__DataProtectionPath`, `Storage__PrivateFilesPath`, `Scheduling__TimeZoneId`, `Notifications__Provider`, `AccessControl__Provider`, `Rescheduling__PublicBaseUrl`, `ConnectionStrings__DefaultConnection`, `AllowedHosts`, `ASPNETCORE_*`). The `verify:production-bundle` npm script and the `BuildClientApp` MSBuild target are referenced as they exist on the branch. `/api/auth/csrf` is the anonymous GET mapped in `AuthEndpoints`. Health paths `/health`, `/health/ready` match `Program.cs:163-164`.

**4. Scope** — no Vercel anywhere; no `/api` rewrite; no CORS; no `SameSite` change. No production connection — every operator command uses `$STAGING_CONN` and `SUPABASE_STAGING_HOST`, and TASK 5 Step 1 aborts if the host is not the staging host. No new feature. The only application code path is TASK 8b, executed only after TASK 8 proves the env toggle fails, and it ships with a failing-first integration test. Each versioned change is its own small commit; no push anywhere.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-09-08-lumis-staging-railway.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — a fresh subagent per task, review between tasks, fast iteration. Note that TASK 4–12 are operator/infra steps that require the human to act in the Supabase and Railway dashboards and on the operator machine; the subagent drives the repo tasks (1, 2, 3, and 8b if needed) and verifies/records the rest.

**2. Inline Execution** — execute tasks in this session using `superpowers:executing-plans`, with checkpoints for the operator steps.

**Which approach?**
