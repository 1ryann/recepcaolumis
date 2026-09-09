# LUMIS Staging — Railway single origin — runbook

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

_Next: TASK 6 — create the Railway service. Not started._
