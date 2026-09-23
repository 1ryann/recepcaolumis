# LUMIS Staging — Railway single origin — runbook

> **DESATIVADO em 2026-09-22. Não siga este runbook.**
> O projeto Supabase `lumis-staging` (ref `xpblbvrmljtvyltvvnpd`, `us-east-1`) foi **apagado**, e o deploy
> migrou do Railway para o VPS em 2026-09-21. Nada em `deploy.yml` ou no código aponta para staging.
> Este arquivo fica como registro histórico do que foi feito em 2026-09-09.
>
> **Motivo da desativação:** manter dois projetos Supabase quase idênticos — distinguíveis só pela região e
> por 20 caracteres de ref — causou três quase-acidentes no mesmo dia, incluindo um `DROP INDEX` destinado ao
> staging executado em produção. Ensaios de migration agora são feitos no Postgres local
> (`localhost:5432/LumisDev`), que é onde a suíte de integração já roda.
>
> Produção hoje: `lumis-production`, ref `nnkcpzbsrudxyrkgdvkz`, `sa-east-1`.

> Operator record. Contains **no secrets** — no passwords, no full connection strings.
> The staging DB credentials live only in the operator's local `dotnet user-secrets`
> (project `recepcaototem`) and, from TASK 7, in Railway service variables.

- Spec: `docs/superpowers/specs/2026-09-08-lumis-staging-railway-design.md` (commit `41b15e2`)
- Plan: `docs/superpowers/plans/2026-09-08-lumis-staging-railway.md`
- App image validated locally at commit `4b1f134` (Dockerfile `eb839fc`, `.dockerignore` `4b1f134`)

---

## TASK 4 — Supabase staging project

- Date: 2026-09-09
- Project name: `lumis-staging`
- **Project ref: `xpblbvrmljtvyltvvnpd`** (identifier, not a secret)
- Region: **`us-east-1`** (East US / North Virginia)
- Database name: `postgres`
- **Direct connection:** `db.xpblbvrmljtvyltvvnpd.supabase.co` : `5432`
  - **IPv6-only** — the host publishes an AAAA record only (`2600:1f18:5905:…`), no A record.
  - Requires working IPv6 egress from the client. The operator machine has **no** IPv6 route to it
    (`Test-NetConnection` and `psql` both time out), so migrations were applied via the Session pooler (see TASK 5).
  - For the Railway runtime this means **Outbound IPv6 must be enabled** (TASK 6) to use the Direct host,
    **or** the runtime connection points at the Session pooler instead. Decided in TASK 6/7.
- **Supavisor Session Mode (fallback + used for TASK 5):** `aws-0-us-east-1.pooler.supabase.com` : `5432`,
  user `postgres.xpblbvrmljtvyltvvnpd`. Has A records; reachable over IPv4 from the operator machine.
- **Transaction Mode (port 6543): NOT USED** — excluded by the spec.
- `unaccent` extension: reported available (`installed_version = null`) before migrations; **installed by the
  baseline migration** in TASK 5 (see below).
- Credentials storage (no values here):
  - `dotnet user-secrets` key `ConnectionStrings:Staging` — Direct 5432 string (runtime-representative).
  - `dotnet user-secrets` key `ConnectionStrings:StagingMigration` — Session pooler 5432 string (used to apply migrations).
  - From TASK 7: Railway service variable `ConnectionStrings__DefaultConnection` (secret).

---

## TASK 5 — Migrations applied to staging

- Date: 2026-09-09
- Environment: `ASPNETCORE_ENVIRONMENT=Production`
- Preflight — `dotnet ef migrations list --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --no-connect`:
  12 migrations in the assembly, head = `20260908210951_ProfessionalPresenceAndRescheduling`.
- Apply (Up only, no target migration, **no `Down`**) —
  `dotnet ef database update --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --connection <ConnectionStrings:StagingMigration>`
  - Connection path: **Supavisor Session Mode 5432** (`aws-0-us-east-1.pooler.supabase.com`, user `postgres.xpblbvrmljtvyltvvnpd`).
    Direct 5432 was used only for pre-checks; it is IPv6-only and unreachable from the operator machine.
  - Result: EF exit code `0`, `Done.`, all 12 `Applying migration '…'` lines, **no partial-migration error**.
    (The initial `SELECT … FROM "__EFMigrationsHistory"` "Failed executing DbCommand" line is expected —
    the history table did not exist yet; EF then created it and proceeded.)

### Post-apply validation (psql via the Session pooler)

| Check | Result |
|---|---|
| `__EFMigrationsHistory` exists | yes |
| Migration row count | **12** |
| Head migration | **`20260908210951_ProfessionalPresenceAndRescheduling`** |
| `unaccent` extension | installed, schema **`extensions`**, version `1.1` |
| `extensions` schema | present |
| `public` base tables | **30** (was `0` before this task) |
| Identity tables | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles` (+ claims/logins/tokens) present |
| Core domain tables | `Professionals`, `Rooms`, `Leases`, `Reservations`, `Customers`, `Visits` present |
| Operating hours | `OperatingHoursSchedules`, `OperatingHourIntervals`, `RoomBlocks` present |
| Availability | `ProfessionalAvailabilityIntervals`, `ProfessionalAvailabilityExceptions` present |
| Presence / Reschedule | `ProfessionalPresence`, `ProfessionalPresenceTokens`, `RescheduleTokens` present |
| Target project | `xpblbvrmljtvyltvvnpd` (connection routed by `postgres.xpblbvrmljtvyltvvnpd`; project had 0 tables/0 migrations immediately before) |
| Production project `nftzridorqewvysttedy` (radalead) | **not touched** |

Full `public` base-table list (30):
`AspNetRoleClaims, AspNetRoles, AspNetUserClaims, AspNetUserLogins, AspNetUserRoles, AspNetUserTokens, AspNetUsers,
AuditEntries, CheckInTokens, Customers, FinancialCharges, LeaseOccurrences, Leases, OperatingHourIntervals,
OperatingHoursSchedules, PrivateFiles, ProfessionalAvailabilityExceptions, ProfessionalAvailabilityIntervals,
ProfessionalPresence, ProfessionalPresenceTokens, ProfessionalRegistrationRequests, Professionals, RescheduleTokens,
Reservations, RoomBlocks, Rooms, Tenants, VisitTransitions, Visits, __EFMigrationsHistory`

### Runtime connection mode — decision pending (TASK 6/7)

- Direct 5432 host `db.xpblbvrmljtvyltvvnpd.supabase.co` is **IPv6-only**.
- From Railway, either enable **Outbound IPv6** and use the Direct host, **or** set
  `ConnectionStrings__DefaultConnection` to the **Session pooler** (`aws-0-us-east-1.pooler.supabase.com:5432`,
  user `postgres.xpblbvrmljtvyltvvnpd`).
- **Transaction Mode 6543 remains excluded.**

---

## TASK 6 / 7 — Railway service + env vars (operator, out of band)

Executed by the operator directly in Railway (not from this repo session). Recorded here from the
operator's confirmation; the Railway env-var **values** are not in this runbook.

- Public URL: `https://lumis-staging.up.railway.app`
- Env toggle in effect: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (see TASK 8 — the `/api/auth/csrf` probe is 200).
- Runtime DB connection mode: operator to record here which was used —
  Direct 5432 (needs Railway Outbound IPv6) **or** Supavisor Session Mode 5432. `<FILL: operator>`
- Transaction Mode 6543: not used.

---

## TASK 8 — First deploy validation

Date: 2026-09-09

### External checks (run from the operator machine against the public URL)

| Path | Status | Expected |
|---|---|---|
| `/health` | 200 | 200 |
| `/health/ready` | 200 | 200 (container reached its Postgres) |
| `/api/auth/csrf` | 200, body `{"token":"CfDJ8…"}` | **200** — the forwarded-headers gate. `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is working; **TASK 8b contingency NOT needed.** |
| `/api/auth/session` | 401 | 401 (anonymous) |
| `/api/does-not-exist` | 401 | 401 |
| `/login` | 200 — real Lumis login screen | 200 |
| `/` | 200 — placeholder "Módulo ainda não disponível" | see note |

- Security headers present on responses: `Strict-Transport-Security: max-age=2592000`, full CSP
  (`default-src 'self'; …; frame-ancestors 'none'`), `Referrer-Policy: no-referrer`,
  `X-Content-Type-Options: nosniff`, `Cache-Control: no-store, no-cache`.
- No `5xx` on any probe.
- **Future adjustment (not a blocker):** `/` renders the "Módulo ainda não disponível" placeholder instead of a
  real home. Tracked as a home/initial-route change for later; does not affect staging validation.

### Railway-side checks — operator to confirm (no Railway CLI in the repo session)

| Check | How | Result `<FILL: operator>` |
|---|---|---|
| No `500` / startup exception in deploy + runtime logs | `railway logs` (or the Railway dashboard log view) | |
| Data Protection keys persisted on the volume | `railway run ls -la /data/dpkeys` → expect `key-*.xml` | |
| Private file storage present | `railway run ls -la /data/private` → exists, writable | |
| `ConnectionStrings__DefaultConnection` points at `xpblbvrmljtvyltvvnpd` | Railway → Variables (value not recorded here) | |

Indirect confirmation already available: `/api/auth/csrf` returns a valid Data-Protection-protected antiforgery
token (DP is functioning); `/health/ready` 200 means the container connected to a healthy Postgres; the staging DB
`xpblbvrmljtvyltvvnpd` holds the full 12-migration schema (TASK 5) and, from TASK 9, the provisioned roles.

---

## TASK 9 — Bootstrap roles + first admin

Date: 2026-09-09
Run from the operator machine against **lumis-staging only**, via the Supavisor Session Mode 5432 connection
(`ConnectionStrings:StagingMigration` in `dotnet user-secrets`; the Direct host is IPv6-only and unreachable here).
`ASPNETCORE_ENVIRONMENT=Production`. CLI published to git-ignored `artifacts/tools/AdminCli/` (not committed, not in the image).

### provision-roles — DONE

- `dotnet artifacts/tools/AdminCli/GestaoPredio.AdminCli.dll provision-roles` → exit 0, "Roles de autenticação provisionadas."
- Validated via psql: `AspNetRoles` count **5** — `ADMINISTRADOR`, `CUSTOMER`, `GERENTE`, `PROFESSIONAL_APPLICANT`, `PROFISSIONAL`.
- `AspNetUsers` = 0, `AuditEntries` = 0 at this point.

### bootstrap-admin — DONE (operator, interactive)

`bootstrap-admin` prompts for display name, e-mail, and password (no-echo, ×2) — no non-interactive mode,
so it was run by the operator. The password was typed interactively; it is not a CLI argument, a file,
an env var, or committed anywhere.

- Command (from the worktree root):
  ```
  ConnectionStrings__DefaultConnection="$(dotnet user-secrets list --project recepcaototem | sed -n 's/^ConnectionStrings:StagingMigration = //p')" \
  ASPNETCORE_ENVIRONMENT=Production \
  dotnet artifacts/tools/AdminCli/GestaoPredio.AdminCli.dll bootstrap-admin
  ```
  Output: `Administrador inicial criado.` (exit 0).
- Validation (via psql against `xpblbvrmljtvyltvvnpd`):
  - `AspNetRoles` = 5.
  - Role mappings: `ADMINISTRADOR` = 1, `CUSTOMER` = 1 (a customer account was also self-registered during setup), users without a role = 0.
  - `AuditEntries`: `BOOTSTRAP_ADMIN_CREATED` = 1, `Result = SUCCEEDED`, `OccurredAt` 2026-09-09 19:19:30 UTC. Also present: `CUSTOMER_CREATED` / `CUSTOMER_ACCOUNT_LINKED` / `LOGIN_SUCCEEDED`.
- Domain data at end of TASK 9: `Professionals` 0, `Rooms` 0, `OperatingHoursSchedules` 0, `ProfessionalAvailabilityIntervals` 0, `Customers` 1, `Reservations` 0, `Visits` 0, `Tenants` 0.

_Stop point reached: first `ADMINISTRADOR` created. TASK 10 (public smoke) is the next task._

---

## TASK 10 — Public smoke test

Date: 2026-09-09 · Target: `https://lumis-staging.up.railway.app` · Driven in the Claude Browser pane; operator performs each login and watches Railway logs.

_In progress — results appended as the flow runs._
