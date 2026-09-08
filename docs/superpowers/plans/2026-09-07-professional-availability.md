# Professional Availability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar disponibilidade semanal e exceções individuais do Professional, preservando intervalos ao alternar modos e centralizando os slots usados por todos os fluxos de novos agendamentos.

**Architecture:** `Professional` é o agregado de modo e coleção semanal, protegido por seu `xmin`; exceções são recursos independentes com `xmin`. Um serviço central de agendamento compõe agenda individual, `OperatingHours`, `IRoomAvailabilityService` e `IReservationConflictDetector`, e é chamado tanto para consulta quanto para confirmação sob os locks PostgreSQL existentes.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10, Npgsql/PostgreSQL, xUnit, ASP.NET Core Identity.

**Spec:** `docs/superpowers/specs/2026-09-07-professional-availability-design.md`

## Global Constraints

- Backend only; não alterar React/npm.
- Timezone operacional: `America/Porto_Velho`.
- Persistir horas civis como `TimeOnly/time without time zone`, datas civis como `DateOnly/date` e instantes como UTC.
- `INHERIT_GLOBAL` é o default e nunca copia `OperatingHours`.
- Trocar modos preserva `ProfessionalAvailabilityIntervals`; somente `PUT CUSTOM` com `days` substitui a coleção.
- Sem OperatingHours: consulta retorna zero slots; criação/reagendamento retorna `409 OPERATING_HOURS_NOT_CONFIGURED`.
- Alterações de agenda/exceção nunca modificam Reservations existentes.
- Não criar tabela Slot, job, cache distribuído ou integração externa.
- Migration somente em `localhost/LumisDev`, após guard explícito; nunca Supabase ou produção.
- Toda produção de código segue RED → GREEN → refactor, com o RED observado.

---

### Task 1: Modelo de domínio e mapeamento EF

**Files:**
- Create: `src/GestaoPredio.Domain/Professionals/ProfessionalAvailabilityMode.cs`
- Create: `src/GestaoPredio.Domain/Professionals/ProfessionalAvailabilityInterval.cs`
- Create: `src/GestaoPredio.Domain/Professionals/ProfessionalAvailabilityException.cs`
- Modify: `src/GestaoPredio.Domain/Professionals/Professional.cs`
- Create: `src/GestaoPredio.Infrastructure/Persistence/Configurations/ProfessionalAvailabilityIntervalConfiguration.cs`
- Create: `src/GestaoPredio.Infrastructure/Persistence/Configurations/ProfessionalAvailabilityExceptionConfiguration.cs`
- Modify: `src/GestaoPredio.Infrastructure/Persistence/Configurations/ProfessionalConfiguration.cs`
- Modify: `src/GestaoPredio.Infrastructure/Persistence/ApplicationDbContext.cs`
- Test: `tests/GestaoPredio.UnitTests/ProfessionalAvailabilityDomainTests.cs`
- Test: `tests/GestaoPredio.IntegrationTests/ModuleModelTests.cs`

**Interfaces:**
- Produces: `ProfessionalAvailabilityMode`; `ProfessionalAvailabilityInterval.CreateDay(Guid professionalId, DayOfWeek dayOfWeek, IReadOnlyCollection<ProfessionalLocalTimeRange> ranges)`; `ProfessionalAvailabilityException.Create(Guid professionalId, DateOnly date, bool allDay, TimeOnly? startTime, TimeOnly? endTime, string? reason, DateTimeOffset occurredAt)`; `Professional.SetAvailabilityMode(ProfessionalAvailabilityMode mode, DateTimeOffset occurredAt)`; and DbSets/configuration.

- [ ] **Step 1: Write failing domain tests** for default `InheritGlobal`, valid split ranges, invalid/overlapping ranges, all-day and partial exceptions, reason normalization, and preservation of intervals as a persistence concern independent of mode.
- [ ] **Step 2: Run** `dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj --no-restore --filter FullyQualifiedName~ProfessionalAvailabilityDomainTests` and confirm RED because the types do not exist.
- [ ] **Step 3: Implement minimal domain types.** Use `DayOfWeek`, `TimeOnly`, `DateOnly`, `TimestampNormalizer`, reason max 300, and methods that reject invalid shapes. `Professional.SetAvailabilityMode` changes only mode/`UpdatedAt`.
- [ ] **Step 4: Run the focused unit tests** and confirm GREEN.
- [ ] **Step 5: Write failing EF model assertions** for table names, PostgreSQL types, defaults, checks, indexes, NoAction FKs and `xmin` on exceptions.
- [ ] **Step 6: Run** `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ModuleModelTests` and confirm RED for missing mappings.
- [ ] **Step 7: Implement configurations and DbSets**, applying them explicitly in `ApplicationDbContext`.
- [ ] **Step 8: Re-run focused unit/model tests** and confirm GREEN.
- [ ] **Step 9: Commit** `feat: add professional availability model`.

### Task 2: Cálculo civil da agenda efetiva

**Files:**
- Create: `src/GestaoPredio.Application/Availability/ProfessionalAvailabilityEvaluator.cs`
- Test: `tests/GestaoPredio.UnitTests/ProfessionalAvailabilityEvaluatorTests.cs`

**Interfaces:**
- Consumes: weekly `OperatingHourInterval`, `ProfessionalAvailabilityInterval`, and dated exceptions.
- Produces: `GetEffectiveRanges(ProfessionalAvailabilityMode mode, DayOfWeek dayOfWeek, DateOnly date, IReadOnlyCollection<OperatingHourInterval> operatingHours, IReadOnlyCollection<ProfessionalAvailabilityInterval> customIntervals, IReadOnlyCollection<ProfessionalAvailabilityException> exceptions)` and `Contains(IReadOnlyCollection<ProfessionalLocalTimeRange> effectiveRanges, TimeOnly startTime, TimeOnly endTime)` using half-open intervals.

- [ ] **Step 1: Write failing tests** proving inheritance, CUSTOM limitation, split intervals, empty day, all-day exception, partial subtraction, adjacency, global intersection after shrink and restoration after expansion.
- [ ] **Step 2: Run** the evaluator test class and confirm RED because the evaluator is absent.
- [ ] **Step 3: Implement the evaluator** as a pure service. Convert neither weekly times nor exception dates to machine-local time; accept the operational civil date and subtract exception ranges deterministically.
- [ ] **Step 4: Run focused tests** and confirm GREEN.
- [ ] **Step 5: Refactor only duplicate range operations**, retaining passing tests.
- [ ] **Step 6: Commit** `feat: calculate professional effective availability`.

### Task 3: Serviço central de slots e disponibilidade de intervalo

**Files:**
- Create: `src/GestaoPredio.Application/Availability/IAppointmentAvailabilityService.cs`
- Create: `src/GestaoPredio.Infrastructure/Availability/PostgreSqlAppointmentAvailabilityService.cs`
- Modify: `recepcaototem/Program.cs`
- Modify: `recepcaototem/Features/Customers/CustomerSchedulingEndpoints.cs`
- Modify: `recepcaototem/Features/Totem/TotemEndpoints.cs`
- Test: `tests/GestaoPredio.IntegrationTests/ProfessionalAvailabilitySchedulingTests.cs`

**Interfaces:**
- Produces: `FindSlotsAsync(professionalId, date, durationMinutes, ct)` and `FindAvailableRoomAsync(professionalId, startAt, endAt, excludedReservationId, ct)`.
- Reuses: `IRoomAvailabilityService`, `IReservationConflictDetector`, `OperatingHoursEvaluator`, and existing transaction boundaries.

- [ ] **Step 1: Write failing integration tests** for INHERIT_GLOBAL, CUSTOM, exceptions, identical Customer/Totem slots, absent OperatingHours fail-closed, RoomBlock, Reservation, Lease and LeaseOccurrence.
- [ ] **Step 2: Run the focused class** and confirm RED because no central service exists.
- [ ] **Step 3: Implement the central service** with 15-minute grid, duration 15–480 and active Professional/Room filtering. Slot lookup may own a read transaction; interval confirmation requires an existing mutation transaction.
- [ ] **Step 4: Replace the loop in `CustomerSchedulingEndpoints.Availability`** with the central service and make Totem delegate to the same operation.
- [ ] **Step 5: Register the service in DI** and run focused tests until GREEN.
- [ ] **Step 6: Confirm no duplicate slot loop remains** with `rg "minute < 24|AvailabilitySlotResponse" recepcaototem src`.
- [ ] **Step 7: Commit** `refactor: centralize appointment availability`.

### Task 4: Revalidação central em todas as mutações de Reservation

**Files:**
- Modify: `recepcaototem/Features/Customers/CustomerSchedulingEndpoints.cs`
- Modify: `recepcaototem/Features/Totem/TotemEndpoints.cs`
- Modify: `recepcaototem/Features/Reception/ReceptionEndpoints.cs`
- Modify: `recepcaototem/Features/Reservations/ReservationEndpoints.cs`
- Modify: `recepcaototem/Features/Reservations/ReservationDecisionEndpoints.cs`
- Modify: `recepcaototem/Features/Reservations/ProfessionalReservationEndpoints.cs`
- Test: `tests/GestaoPredio.IntegrationTests/ProfessionalAvailabilityReservationMutationTests.cs`

**Interfaces:**
- Consumes: `IAppointmentAvailabilityService.FindAvailableRoomAsync(Guid professionalId, DateTimeOffset startAt, DateTimeOffset endAt, Guid? requiredRoomId, Guid? excludedReservationId, CancellationToken cancellationToken)` after resource locks.
- Produces: uniform `409 OPERATING_HOURS_NOT_CONFIGURED` or `409 PROFESSIONAL_UNAVAILABLE` without partial writes.

- [ ] **Step 1: Write failing tests** for Customer create/reschedule, Totem, Reception assisted booking, Admin create/reschedule and Professional request paths outside individual availability and without OperatingHours.
- [ ] **Step 2: Run the focused class** and confirm RED because existing paths bypass individual availability.
- [ ] **Step 3: Replace local room/conflict orchestration** with the central service after existing deterministic locks. Preserve DTOs, status transitions, QR revocation and audit behavior.
- [ ] **Step 4: Ensure requests that already carry a fixed Room validate that Room**, while Customer/Totem/Reception assisted booking continue resolving Room server-side.
- [ ] **Step 5: Run focused mutation tests** and confirm GREEN, including assertions that failed attempts create no Reservation or success audit.
- [ ] **Step 6: Commit** `feat: enforce professional availability on bookings`.

### Task 5: APIs da semana para Professional e Operations

**Files:**
- Create: `recepcaototem/Features/Availability/ProfessionalAvailabilityContracts.cs`
- Create: `recepcaototem/Features/Availability/ProfessionalAvailabilityEndpoints.cs`
- Create: `src/GestaoPredio.Application/Availability/ProfessionalAvailabilityAudit.cs`
- Modify: `recepcaototem/Program.cs`
- Modify: `src/GestaoPredio.Domain/Auditing/AuditActions.cs`
- Modify: `src/GestaoPredio.Domain/Auditing/AuditEntry.cs`
- Test: `tests/GestaoPredio.IntegrationTests/ProfessionalAvailabilityApiTests.cs`

**Interfaces:**
- Produces: own and Operations GET/PUT contracts from the spec, `days`, `effectiveDays`, aggregate concurrency token and warning count.

- [ ] **Step 1: Write failing API tests** for policies, own identity resolution, Operations target, seven-day validation, invalid time/overlap, outside-global rejection, invalid/stale token and antiforgery.
- [ ] **Step 2: Add failing preservation tests:** CUSTOM with ranges → INHERIT_GLOBAL leaves rows; INHERIT_GLOBAL ignores rows; CUSTOM without `days` reactivates; CUSTOM with `days` replaces.
- [ ] **Step 3: Run the focused class** and confirm RED because routes are absent.
- [ ] **Step 4: Implement strict DTO parsing and shared handlers** for `/api/professional/availability` and `/api/admin/professionals/{id}/availability`.
- [ ] **Step 5: In PUT**, open transaction, lock Professional, compare aggregate `xmin`, validate current OperatingHours, update mode, replace rows only when `days` is present, count future blocking Reservations outside effective availability, audit, save and commit.
- [ ] **Step 6: Run focused API tests** and confirm GREEN.
- [ ] **Step 7: Commit** `feat: add professional weekly availability APIs`.

### Task 6: APIs de exceções, concorrência e aviso

**Files:**
- Modify: `recepcaototem/Features/Availability/ProfessionalAvailabilityContracts.cs`
- Modify: `recepcaototem/Features/Availability/ProfessionalAvailabilityEndpoints.cs`
- Modify: `src/GestaoPredio.Application/Availability/ProfessionalAvailabilityAudit.cs`
- Test: `tests/GestaoPredio.IntegrationTests/ProfessionalAvailabilityExceptionApiTests.cs`

**Interfaces:**
- Produces: own/Operations GET, POST, PUT and DELETE; each mutation shares validation and warning calculation.

- [ ] **Step 1: Write failing tests** for all-day/partial creation, optional reason, invalid shape, overlap, adjacency, listing bounds/order, own-resource IDOR and Operations access.
- [ ] **Step 2: Add failing concurrency tests** for invalid/stale exception token and two simultaneous overlapping creates serialized by the Professional lock.
- [ ] **Step 3: Add failing warning tests** proving creation/update reports future approved Reservations outside availability without modifying them; deletion increases availability.
- [ ] **Step 4: Run the focused class** and confirm RED.
- [ ] **Step 5: Implement shared exception handlers** with `from/to` required and maximum 366 days, strict DELETE body token, row lock, overlap validation, own `xmin`, audit and transaction rollback.
- [ ] **Step 6: Run focused exception tests** and confirm GREEN.
- [ ] **Step 7: Verify audit records distinguish self and `_BY_OPERATIONS` without reason/PII payloads.**
- [ ] **Step 8: Commit** `feat: add professional availability exceptions`.

### Task 7: Migration PostgreSQL aditiva e segurança local

**Files:**
- Create: EF-generated migration `ProfessionalAvailability` and its designer under `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/` (EF assigns the timestamp prefix)
- Modify: `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/ApplicationDbContextModelSnapshot.cs`
- Modify: `tests/GestaoPredio.IntegrationTests/MigrationSafetyTests.cs`

**Interfaces:**
- Consumes: final EF model from Tasks 1–6.
- Produces: additive schema and default `INHERIT_GLOBAL` for existing Professionals.

- [ ] **Step 1: Write failing migration safety assertions** for allowed tables/column/indexes/checks/FKs and absence of Drops/Slot structures.
- [ ] **Step 2: Run the focused migration safety test** and confirm RED because migration is absent.
- [ ] **Step 3: Generate migration** with `dotnet ef migrations add ProfessionalAvailability --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --context ApplicationDbContext --output-dir Persistence/Migrations/PostgreSql`.
- [ ] **Step 4: Inspect generated Up/Down and SQL**. Up may only add `Professionals.AvailabilityMode`, constraints, two tables, indexes and NoAction FKs.
- [ ] **Step 5: Run migration/model tests** and confirm GREEN.
- [ ] **Step 6: Read the local connection metadata without printing credentials**, require `Host=localhost` and `Database=LumisDev`, and abort for Supabase or production names.
- [ ] **Step 7: Apply migration only to guarded LumisDev** with `dotnet ef database update` and list applied migrations.
- [ ] **Step 8: Commit** `feat: add professional availability migration`.

### Task 8: Verificação consolidada do backend

**Files:**
- Modify only files required by objective failures found in verification, always after a reproducing RED test.

**Interfaces:**
- Confirms every public and authenticated scheduling surface uses the same semantics.

- [ ] **Step 1: Run focused tests** for domain evaluator, APIs, exceptions, scheduling and migration safety.
- [ ] **Step 2: Run** `dotnet test recepcaototem.sln --no-restore -m:1 -p:BuildInParallel=false --disable-build-servers` and record passed/failed totals.
- [ ] **Step 3: Run** `dotnet build recepcaototem.sln --no-restore -m:1 -p:BuildInParallel=false --disable-build-servers` and record warnings/errors.
- [ ] **Step 4: Generate the migration SQL** between the prior migration and `ProfessionalAvailability`; scan for `DROP`, `TRUNCATE`, destructive `ALTER`, Supabase and production database identifiers.
- [ ] **Step 5: Run** `dotnet ef migrations list`, `git diff --check`, `git status --short` and `git log --oneline`.
- [ ] **Step 6: If verification exposes a real defect, add a failing test, make the minimum fix, rerun the affected focused test and repeat the relevant final command once.**
- [ ] **Step 7: Commit any verification fix separately** with a focused message; otherwise leave no extra commit.
