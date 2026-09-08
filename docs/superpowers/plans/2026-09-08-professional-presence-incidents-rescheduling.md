# Professional presence, incidents & rescheduling — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add physical-presence tracking for professionals, a three-option "incident" flow that pulls affected future reservations off the agenda, and a login-free 48h WhatsApp rescheduling link for the affected customers — backend only.

**Architecture:** Three new small aggregates (`ProfessionalPresence`, `ProfessionalPresenceToken`, `RescheduleToken`) mirroring the existing `CheckInToken`/`xmin` patterns; two additive columns (`Reservation.CancellationReason`, `ProfessionalAvailabilityException.Origin`); one pure `PresenceEvaluator`; `INotificationService` gains a customer channel; `IOperationalAlertReader` gains two derived alert types. The central `IAppointmentAvailabilityService` is **reused unchanged** — presence is an extra gate the callers apply. No background job: effective status is computed at read time, with opportunistic materialisation only when a write already holds the row.

**Tech Stack:** .NET 10, EF Core + Npgsql (PostgreSQL, `xmin` row version), minimal APIs in `recepcaototem/Features`, xUnit (`tests/GestaoPredio.UnitTests` for domain/application, `tests/GestaoPredio.IntegrationTests` with `ModulesApiFactory` against `localhost:5432/LumisDev`).

**Spec:** `docs/superpowers/specs/2026-09-08-professional-presence-incidents-rescheduling-design.md` (approved, decisions A–G final). The plan argues from the spec; read both.

## Global Constraints

- Branch `codex/reception-backend`. No push, no deploy, no Supabase, no real Meta, no Intelbras. No background job / cron / worker / queue / Redis.
- Operational timezone: the `TimeZoneInfo` singleton from `Program.cs` (`Scheduling:TimeZoneId` = `America/Porto_Velho`). Never hard-code a timezone or a default opening time; **fail-closed when OperatingHours is not configured** (spec §31).
- All new persistence mirrors `CheckInToken` / `CheckInTokenConfiguration`: `Guid Id`; `uint Version` via `.Property(x => x.Version).IsRowVersion()` (`xmin`); timestamps `TimestampNormalizer.ToUtcMicroseconds` + `timestamp with time zone`; opaque tokens = `RandomNumberGenerator.GetBytes(32)` transported as `WebEncoders.Base64UrlEncode`, persisted only as `SHA256.HashData(raw)` (`bytea`, 32 bytes), unique index on the hash, one generic error per token failure class (anti-enumeration).
- QR presence token TTL = **120s** (`Presence:QrTokenTtlSeconds`, default 120). Reschedule link TTL = **48h** (`Rescheduling:LinkTtlHours`, default 48).
- Every new write runs inside `db.Database.BeginTransactionAsync` and acquires `ILeaseResourceLock.AcquireAsync` before any availability check (the `PostgreSqlAppointmentAvailabilityService.EnsureTransaction()` contract).
- Audit inline (decision F), `Result = "SUCCEEDED"`, `CorrelationId = context.TraceIdentifier`. **Never persist or log:** raw token, token hash, phone, password, cookie, the professional's internal incident reason (that lives only on `ProfessionalAvailabilityException.Reason`), raw WhatsApp body.
- **Visit is never read, cancelled or transitioned by any new flow** (spec §17). The incident transaction touches only `ProfessionalAvailabilityException`, `Reservation`, `ProfessionalPresence`, `RescheduleToken`, `AuditEntry`.
- Additive only: no column dropped/renamed/renumbered; new `smallint` columns are `NOT NULL DEFAULT 0`; existing call sites of `Reservation.Cancel` / `ProfessionalAvailabilityException.Create` compile unchanged.
- TDD: failing test first for every behavioural change, then implement, then green. `dotnet build` clean and the relevant `dotnet test` green before each commit. Commit after each task.
- Migration command (never `dotnet ef database update` against anything but the guarded local `LumisDev`):
  `dotnet ef migrations add ProfessionalPresenceAndRescheduling --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --context ApplicationDbContext --output-dir Persistence/Migrations/PostgreSql`

---

## Task 1: `ProfessionalPresence` aggregate

**Files:**
- Create: `src/GestaoPredio.Domain/Professionals/ProfessionalPresence.cs`
- Create: `src/GestaoPredio.Domain/Professionals/PresenceEndReason.cs`, `src/GestaoPredio.Domain/Professionals/PresenceSource.cs`
- Test: `tests/GestaoPredio.UnitTests/ProfessionalPresenceTests.cs`

**Interfaces — Produces:**
- `enum PresenceEndReason : short { ManagerManual = 0, IncidentUntilTime = 1, IncidentRestOfDay = 2, OperatingHoursElapsed = 3 }`
- `enum PresenceSource : short { QrSelfScan = 0, ManagerManual = 1 }`
- `sealed class ProfessionalPresence` — `Guid Id`, `Guid ProfessionalId`, `DateTimeOffset StartedAt`, `DateTimeOffset? EndedAt`, `PresenceEndReason? EndReason`, `PresenceSource Source`, `string? CreatedByUserId`, `uint Version`.
  - `static ProfessionalPresence StartByQr(Guid professionalId, DateTimeOffset now)`
  - `static ProfessionalPresence StartByManager(Guid professionalId, string managerUserId, DateTimeOffset now)` (guards `managerUserId` non-empty ≤450)
  - `void EndManually(string managerUserId, DateTimeOffset now)` → `EndedAt`, `EndReason = ManagerManual` (throws if already ended)
  - `void EndForIncident(bool restOfDay, DateTimeOffset now)` → `EndReason = restOfDay ? IncidentRestOfDay : IncidentUntilTime` (throws if already ended)
  - `void MaterialiseOperatingHoursEnd(DateTimeOffset closeInstant)` → `EndedAt = closeInstant`, `EndReason = OperatingHoursElapsed` (throws if already ended)
  - `bool IsOpen => EndedAt is null`

- [ ] **Step 1: Write the failing test** — `ProfessionalPresenceTests.cs`

```csharp
public class ProfessionalPresenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartByQr_opens_a_presence()
    {
        var p = ProfessionalPresence.StartByQr(Guid.NewGuid(), Now);
        Assert.True(p.IsOpen);
        Assert.Null(p.EndedAt);
        Assert.Equal(PresenceSource.QrSelfScan, p.Source);
        Assert.Null(p.CreatedByUserId);
    }

    [Fact]
    public void StartByManager_records_the_actor()
    {
        var p = ProfessionalPresence.StartByManager(Guid.NewGuid(), "mgr-1", Now);
        Assert.Equal(PresenceSource.ManagerManual, p.Source);
        Assert.Equal("mgr-1", p.CreatedByUserId);
    }

    [Fact]
    public void EndManually_then_end_again_throws()
    {
        var p = ProfessionalPresence.StartByQr(Guid.NewGuid(), Now);
        p.EndManually("mgr-1", Now.AddHours(1));
        Assert.Equal(PresenceEndReason.ManagerManual, p.EndReason);
        Assert.False(p.IsOpen);
        Assert.Throws<InvalidOperationException>(() => p.EndManually("mgr-1", Now.AddHours(2)));
    }

    [Fact]
    public void EndForIncident_sets_the_right_reason()
    {
        var a = ProfessionalPresence.StartByQr(Guid.NewGuid(), Now);
        a.EndForIncident(restOfDay: true, Now.AddHours(1));
        Assert.Equal(PresenceEndReason.IncidentRestOfDay, a.EndReason);

        var b = ProfessionalPresence.StartByQr(Guid.NewGuid(), Now);
        b.EndForIncident(restOfDay: false, Now.AddHours(1));
        Assert.Equal(PresenceEndReason.IncidentUntilTime, b.EndReason);
    }

    [Fact]
    public void MaterialiseOperatingHoursEnd_closes_with_elapsed_reason()
    {
        var p = ProfessionalPresence.StartByQr(Guid.NewGuid(), Now);
        p.MaterialiseOperatingHoursEnd(Now.AddHours(6));
        Assert.Equal(PresenceEndReason.OperatingHoursElapsed, p.EndReason);
        Assert.Equal(Now.AddHours(6), p.EndedAt);
    }

    [Fact]
    public void StartByManager_rejects_empty_actor() =>
        Assert.Throws<ArgumentException>(() => ProfessionalPresence.StartByManager(Guid.NewGuid(), " ", Now));
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/GestaoPredio.UnitTests --filter FullyQualifiedName~ProfessionalPresenceTests` → FAIL (type not found).
- [ ] **Step 3: Implement** the two enums and `ProfessionalPresence` per the Interfaces block. Follow `Customer.cs` / `CheckInToken.cs` style: private ctor, `Guid.NewGuid()` id, `TimestampNormalizer.ToUtcMicroseconds` on every `DateTimeOffset`, guard clauses `ArgumentException`, an `EnsureOpen()` private helper throwing `InvalidOperationException` used by the three enders.
- [ ] **Step 4: Run to verify it passes** — same filter → PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: add ProfessionalPresence domain aggregate"`

---

## Task 2: `ProfessionalPresenceToken` aggregate

**Files:**
- Create: `src/GestaoPredio.Domain/Professionals/ProfessionalPresenceToken.cs`
- Test: `tests/GestaoPredio.UnitTests/ProfessionalPresenceTokenTests.cs`

**Interfaces — Consumes:** none. **Produces:** `sealed class ProfessionalPresenceToken` identical in shape to `CheckInToken` but keyed on `ProfessionalId`:
- `Guid Id`, `Guid ProfessionalId`, `byte[] TokenHash`, `DateTimeOffset IssuedAt`, `DateTimeOffset ExpiresAt`, `DateTimeOffset? UsedAt`, `DateTimeOffset? RevokedAt`, `uint Version`.
- `static ProfessionalPresenceToken Create(Guid professionalId, byte[] tokenHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt)` — validates `professionalId != Empty`, `tokenHash.Length == 32`, `expiresAt > issuedAt`.
- `void MarkUsed(DateTimeOffset at)`, `void Revoke(DateTimeOffset at)`
- `void Rotate(byte[] tokenHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt)` — same 32-byte / ordering guards, clears `UsedAt` and `RevokedAt` (copy `CheckInToken.Rotate`).

- [ ] **Step 1: Write the failing test** — mirror `tests/GestaoPredio.UnitTests/CheckInTokenTests.cs`: `Create` rejects a 31-byte hash and `expiresAt <= issuedAt`; `Create` normalises timestamps; `Rotate` replaces hash and clears `UsedAt`/`RevokedAt`; `MarkUsed`/`Revoke` set their fields.
- [ ] **Step 2: Run** `dotnet test tests/GestaoPredio.UnitTests --filter FullyQualifiedName~ProfessionalPresenceTokenTests` → FAIL.
- [ ] **Step 3: Implement** by copying `CheckInToken.cs`, renaming `ReservationId`→`ProfessionalId`, keeping every guard and `TimestampNormalizer` call.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `git commit -am "feat: add ProfessionalPresenceToken domain aggregate"`

---

## Task 3: `RescheduleToken` aggregate

**Files:**
- Create: `src/GestaoPredio.Domain/Customers/RescheduleToken.cs`
- Test: `tests/GestaoPredio.UnitTests/RescheduleTokenTests.cs`

**Interfaces — Produces:** `sealed class RescheduleToken` — `Guid Id`, `Guid ReservationId`, `byte[] TokenHash`, `DateTimeOffset IssuedAt`, `DateTimeOffset ExpiresAt`, `DateTimeOffset? UsedAt`, `DateTimeOffset? RevokedAt`, `uint Version`. Same `Create` / `MarkUsed` / `Revoke` / `Rotate` as `CheckInToken` (keyed on `ReservationId`).

- [ ] **Step 1: Write the failing test** — mirror `CheckInTokenTests.cs`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~RescheduleTokenTests` → FAIL.
- [ ] **Step 3: Implement** — copy `CheckInToken.cs` into `src/GestaoPredio.Domain/Customers/RescheduleToken.cs` (same namespace `GestaoPredio.Domain.Customers`), keep `ReservationId`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `git commit -am "feat: add RescheduleToken domain aggregate"`

---

## Task 4: `Reservation` — cancellation reason + incident replacement factory

**Files:**
- Create: `src/GestaoPredio.Domain/Reservations/ReservationCancellationReason.cs`
- Modify: `src/GestaoPredio.Domain/Reservations/Reservation.cs`
- Test: `tests/GestaoPredio.UnitTests/ReservationTests.cs` (add cases)

**Interfaces — Produces:**
- `enum ReservationCancellationReason : short { None = 0, ProfessionalUnavailable = 1 }`
- `Reservation.CancellationReason` — `{ get; private set; }`, default `None`.
- `Reservation.Cancel(string actorUserId, DateTimeOffset occurredAt, ReservationCancellationReason reason = ReservationCancellationReason.None)` — sets `CancellationReason = reason` in addition to existing behaviour; still `EnsureApprovedActualReservation()`.
- `static Reservation CreateApprovedReplacementForIncident(Reservation original, Guid roomId, DateTimeOffset startAt, DateTimeOffset endAt, string actorUserId, DateTimeOffset occurredAt)` — requires `original.Status == Cancelled && original.CancellationReason == ProfessionalUnavailable` (else `InvalidOperationException`) and `roomId != Guid.Empty`; produces `Kind = Reschedule`, `Status = Approved`, `RoomId = roomId`, `OriginalReservationId = original.Id`, `CustomerId = original.CustomerId`, `ProfessionalId = original.ProfessionalId`, `DecidedByUserId`/`DecidedAt` set; **does not** call `EnsureMinimumNotice`. (`roomId` is passed by the caller from the central availability service — see Task 12; it may differ from `original.RoomId`.)

- [ ] **Step 1: Write the failing tests** — add to `ReservationTests.cs`:

```csharp
[Fact]
public void Cancel_default_reason_is_None()
{
    var r = ApprovedReservation();
    r.Cancel("mgr", Now);
    Assert.Equal(ReservationCancellationReason.None, r.CancellationReason);
    Assert.Equal(ReservationStatus.Cancelled, r.Status);
}

[Fact]
public void Cancel_with_ProfessionalUnavailable_records_the_reason()
{
    var r = ApprovedReservation();
    r.Cancel("PROFESSIONAL_INCIDENT", Now, ReservationCancellationReason.ProfessionalUnavailable);
    Assert.Equal(ReservationCancellationReason.ProfessionalUnavailable, r.CancellationReason);
}

[Fact]
public void CreateApprovedReplacementForIncident_requires_a_cancelled_unavailable_original()
{
    var approved = ApprovedReservation();
    Assert.Throws<InvalidOperationException>(() =>
        Reservation.CreateApprovedReplacementForIncident(approved, Start, End, "RESCHEDULE_LINK", Now));

    approved.Cancel("PROFESSIONAL_INCIDENT", Now, ReservationCancellationReason.ProfessionalUnavailable);
    var replacement = Reservation.CreateApprovedReplacementForIncident(approved, SomeRoomId, Start.AddDays(1), End.AddDays(1), "RESCHEDULE_LINK", Now);
    Assert.Equal(ReservationKind.Reschedule, replacement.Kind);
    Assert.Equal(ReservationStatus.Approved, replacement.Status);
    Assert.Equal(SomeRoomId, replacement.RoomId);
    Assert.Equal(approved.Id, replacement.OriginalReservationId);
    Assert.Equal(approved.CustomerId, replacement.CustomerId);
}

[Fact]
public void CreateApprovedReplacementForIncident_rejects_a_normally_cancelled_original()
{
    var r = ApprovedReservation();
    r.Cancel("customer", Now); // reason None
    Assert.Throws<InvalidOperationException>(() =>
        Reservation.CreateApprovedReplacementForIncident(r, SomeRoomId, Start.AddDays(1), End.AddDays(1), "RESCHEDULE_LINK", Now));
}
```

(`ApprovedReservation()`, `Now`, `Start`, `End`, `SomeRoomId` helpers: follow the existing fixtures in `ReservationTests.cs`; `SomeRoomId = Guid.NewGuid()`.)

- [ ] **Step 2: Run** `dotnet test tests/GestaoPredio.UnitTests --filter FullyQualifiedName~ReservationTests` → new cases FAIL.
- [ ] **Step 3: Implement** — add the enum; add `CancellationReason` property; add the `reason` parameter to `Cancel`; add the new factory calling the private `Create(...)` with `ReservationStatus.Approved` + setting `DecidedByUserId`/`DecidedAt` like `CreateApprovedReschedule`, guarding the original's state first.
- [ ] **Step 4: Run** the full `ReservationTests` filter → PASS (existing cases unaffected — `Cancel` still has the same first two params).
- [ ] **Step 5: Commit** — `git commit -am "feat: reservation cancellation reason and incident replacement factory"`

---

## Task 5: `ProfessionalAvailabilityException.Origin`

**Files:**
- Create: `src/GestaoPredio.Domain/Professionals/ProfessionalAvailabilityExceptionOrigin.cs`
- Modify: `src/GestaoPredio.Domain/Professionals/ProfessionalAvailabilityException.cs`
- Test: `tests/GestaoPredio.UnitTests/ProfessionalAvailabilityDomainTests.cs` (add cases)

**Interfaces — Produces:**
- `enum ProfessionalAvailabilityExceptionOrigin : short { Planned = 0, Incident = 1 }`
- `ProfessionalAvailabilityException.Origin` — `{ get; private set; }`, default `Planned`.
- `Create(Guid professionalId, DateOnly date, bool allDay, TimeOnly? startTime, TimeOnly? endTime, string? reason, DateTimeOffset occurredAt, ProfessionalAvailabilityExceptionOrigin origin = ProfessionalAvailabilityExceptionOrigin.Planned)` — trailing defaulted parameter; `Origin` is set once at creation and never mutated by `Update`.

- [ ] **Step 1: Write the failing tests** — add to `ProfessionalAvailabilityDomainTests.cs`: default `Create` → `Origin == Planned`; `Create(..., origin: Incident)` → `Origin == Incident`; `Update(...)` leaves `Origin` unchanged.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ProfessionalAvailabilityDomainTests` → new cases FAIL.
- [ ] **Step 3: Implement** the enum + property + parameter (set `Origin = origin` inside the factory before `value.Set(...)`).
- [ ] **Step 4: Run** → PASS (existing exception tests + `ProfessionalAvailabilityEvaluatorTests` unaffected).
- [ ] **Step 5: Commit** — `git commit -am "feat: tag professional availability exceptions with an origin"`

---

## Task 6: `PresenceEvaluator` (pure)

**Files:**
- Create: `src/GestaoPredio.Application/Availability/PresenceEvaluator.cs`
- Test: `tests/GestaoPredio.UnitTests/PresenceEvaluatorTests.cs`

**Interfaces — Consumes:** `ProfessionalPresence` (Task 1), `OperatingHourInterval`. **Produces:**

```csharp
public static class PresenceEvaluator
{
    // effective ⇔ open ∧ not ended ∧ same civil day as StartedAt ∧ operating hours configured for that day
    //             ∧ local(now) <= last ClosesAt of the day.  NOT gated on the day's OpensAt (decision E).
    public static bool IsEffective(
        ProfessionalPresence? openPresence,
        IReadOnlyCollection<OperatingHourInterval> operatingHoursForCivilDay,
        DateTimeOffset now,
        TimeZoneInfo zone);

    // the UTC instant of the day's last ClosesAt, for opportunistic materialisation; null if the day is closed.
    public static DateTimeOffset? OperatingHoursEndInstant(
        DateOnly civilDay,
        IReadOnlyCollection<OperatingHourInterval> operatingHoursForCivilDay,
        TimeZoneInfo zone);
}
```

- [ ] **Step 1: Write the failing test** — `PresenceEvaluatorTests.cs`, using fixed instants in `America/Porto_Velho` (or `TimeZoneInfo.CreateCustomTimeZone` UTC-4 no-DST) and `OperatingHourInterval.CreateDay`:
  - open presence started 09:00, hours Mon 08:00–18:30, now 15:00 → `true` (past personal-availability but before close).
  - now 18:31 → `false` (past last close).
  - now next civil day 09:00 → `false` (new day).
  - `operatingHoursForCivilDay` empty → `false` (fail-closed).
  - `openPresence == null` → `false`; ended presence → `false`.
  - hours with a lunch gap 08:00–12:00 + 13:00–18:00, now 12:30 → `true` (gaps don't end presence).
  - now 07:00 (before open) with an open presence started 06:50 → `true` (decision E).
  - `OperatingHoursEndInstant` returns the UTC of 18:30 local for the Monday; `null` when the collection is empty.

- [ ] **Step 2: Run** `--filter FullyQualifiedName~PresenceEvaluatorTests` → FAIL.
- [ ] **Step 3: Implement** — mirror `OperatingHoursEvaluator` (timezone conversions via `TimeZoneInfo.ConvertTime`, `TimeOnly.FromDateTime`). `max(ClosesAt)` over the collection; civil-day comparison on the local `DateTime.Date`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `git commit -am "feat: add PresenceEvaluator pure status function"`

---

## Task 7: `INotificationService` customer channel

**Files:**
- Modify: `src/GestaoPredio.Application/Notifications/INotificationService.cs`
- Modify: `src/GestaoPredio.Infrastructure/Notifications/NotificationServices.cs`
- Test: `tests/GestaoPredio.UnitTests/NotificationProviderTests.cs` (add cases) and/or a new `NotificationServiceCustomerTests.cs`

**Interfaces — Produces:**
- `sealed record CustomerNotificationEvent(Guid CustomerId, string EventType, Guid ReservationId, string ProfessionalName, DateTimeOffset OriginalStartAt, string RescheduleUrl)`
- `NotificationEventTypes.CustomerReservationCancelledReschedule = "CUSTOMER_RESERVATION_CANCELLED_RESCHEDULE"`
- `INotificationService.NotifyCustomerAsync(CustomerNotificationEvent e, CancellationToken ct) : Task<NotificationResult>` (new; `NotifyProfessionalAsync` unchanged).
- `NotificationMessage` — `Guid? ProfessionalId` (was non-nullable) and new `Guid? CustomerId`. `INotificationProvider.SendAsync` signature unchanged.

Body (spec §11 / §19): `"Seu atendimento precisou ser cancelado por um imprevisto do profissional. Você pode escolher um novo horário pelo link abaixo. Este link ficará disponível por 48 horas. {RescheduleUrl}"` — **no internal reason, no professional phone/email**. Only the professional first name is used for identification (reuse the existing `FirstName` helper). `LogFailure` logs provider + event type + `FailureCode` + `CustomerId` + `ReservationId` — never body, never phone.

- [ ] **Step 1: Write the failing tests**
  - `NotificationServices` — `NotifyCustomerAsync` with the Demo provider records a `DemoNotificationAttempt` and returns `Succeeded`; with `recorder.ForceFailure = true` returns `Failed("Demo", "DEMO_PROVIDER_FAILURE")` and does **not** throw.
  - the built body contains the URL and the professional first name and contains **neither** a `Reason`-like string **nor** the customer's phone.
  - `NotifyProfessionalAsync` existing behaviour unchanged (keep the existing tests green).
- [ ] **Step 2: Run** the notification filters → new cases FAIL / compile error.
- [ ] **Step 3: Implement**
  - widen `NotificationMessage` (nullable `ProfessionalId`, add `CustomerId`); fix the two provider `Record(...)` calls (they read `message.ProfessionalId` — guard with `?? Guid.Empty` for the recorder, which only needs a correlation value).
  - add `NotifyCustomerAsync` to the interface and `NotificationService`: load `db.Customers` by `event.CustomerId`, `!IsActive` → `Failed(provider.Name, "RECIPIENT_UNAVAILABLE")`; `WhatsAppNormalizer.TryNormalize(customer.Phone, out var phone)` fail → `"RECIPIENT_PHONE_INVALID"`; build body; `provider.SendAsync(new NotificationMessage(ProfessionalId: null, phone, event.EventType, body) { CustomerId = event.CustomerId })`; `LogFailure`.
- [ ] **Step 4: Run** all notification unit tests → PASS.
- [ ] **Step 5: Commit** — `git commit -am "feat: add customer notification channel to INotificationService"`

---

## Task 8: Derived Reception alerts

**Files:**
- Modify: `src/GestaoPredio.Application/OperationalAlerts/OperationalAlert.cs` (enum members)
- Modify: `src/GestaoPredio.Infrastructure/OperationalAlerts/PostgreSqlOperationalAlertReader.cs`
- Test: `tests/GestaoPredio.IntegrationTests/OperationalAlertApiTests.cs` (add cases)

**Interfaces — Produces:** `OperationalAlertType` gains `CustomerWaitingProfessionalAbsent` and `OpenVisitAffectedByIncident` (append at the end of the enum — do not renumber). No new state; two new `ReadXxxAsync` branches in the reader, following `ReadEndedReservationVisitsAsync` structure. **Decision B: no `NotificationDeliveryFailed` alert.**

- `CustomerWaitingProfessionalAbsent` (`Critical`): open `Visit` (`Waiting`) whose professional is **not** effective by `PresenceEvaluator` at `now`. The reader needs, per candidate professional: the open `ProfessionalPresence` row + the day's `OperatingHourIntervals`. Query it once for the distinct professional ids of waiting visits.
- `OpenVisitAffectedByIncident` (`Critical`): open `Visit` (`Waiting` or `InService`) where **either** the professional has a `ProfessionalAvailabilityException` with `Origin = Incident`, `Date = today (local)`, `StartTime <= now_local < EndTime`, **or** the visit's `ReservationId` points to a reservation with `Status = Cancelled && CancellationReason = ProfessionalUnavailable`.

- [ ] **Step 1: Write the failing integration tests** in `OperationalAlertApiTests.cs` (`[Collection(ModulesDatabaseCollection.Name)]`, `factory.ResetAsync()`, `factory.SeedDefaultOperatingHoursAsync()`, seed via a scope): seed a waiting visit + an absent professional → `GET /api/operational-alerts` contains `CUSTOMER_WAITING_PROFESSIONAL_ABSENT`; seed an incident exception covering now + an open visit → contains `OPEN_VISIT_AFFECTED_BY_INCIDENT`; with the professional present and no incident → neither appears.
- [ ] **Step 2: Run** `dotnet test tests/GestaoPredio.IntegrationTests --filter FullyQualifiedName~OperationalAlertApiTests` → new cases FAIL.
- [ ] **Step 3: Implement** the enum members (+ their `Contract`/serialisation strings wherever `OperationalAlertType` is mapped in `recepcaototem/Features/OperationalAlerts`) and the two reader branches. Inject `TimeZoneInfo` into `PostgreSqlOperationalAlertReader` (constructor already takes `ApplicationDbContext`; add the singleton `TimeZoneInfo`).
- [ ] **Step 4: Run** the filter → PASS; run the whole `OperationalAlertApiTests` to confirm existing alerts unaffected.
- [ ] **Step 5: Commit** — `git commit -am "feat: derive presence/incident Reception alerts"`

---

## Task 9: EF configuration, DbContext, migration

**Files:**
- Create: `src/GestaoPredio.Infrastructure/Persistence/Configurations/ProfessionalPresenceConfiguration.cs`, `ProfessionalPresenceTokenConfiguration.cs`, `RescheduleTokenConfiguration.cs`
- Modify: `src/GestaoPredio.Infrastructure/Persistence/ApplicationDbContext.cs` (three `DbSet<>`), + wherever `Reservation` / `ProfessionalAvailabilityException` are configured (add the two `smallint` columns with `HasDefaultValue((short)0)`)
- Create: `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/<timestamp>_ProfessionalPresenceAndRescheduling.cs` (+ Designer) via EF tooling
- Test: `tests/GestaoPredio.IntegrationTests/ModulePersistenceTests.cs` (add round-trip cases), `MigrationSafetyTests` must stay green

**Interfaces — Produces:** `db.ProfessionalPresences`, `db.ProfessionalPresenceTokens`, `db.RescheduleTokens`; `Reservations.CancellationReason` + `ProfessionalAvailabilityExceptions.Origin` columns.

Config details (mirror `CheckInTokenConfiguration`):
- **`ProfessionalPresence`** → table `ProfessionalPresence`; `TokenHash` n/a; `EndedAt`/`EndReason` nullable; `Version.IsRowVersion()`; `HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId).OnDelete(DeleteBehavior.NoAction)`; index `(ProfessionalId, StartedAt)`; **partial unique index** — in the migration's `Up`, `migrationBuilder.Sql("""CREATE UNIQUE INDEX "UX_ProfessionalPresence_Open" ON "ProfessionalPresence" ("ProfessionalId") WHERE "EndedAt" IS NULL;""")` and drop it in `Down`. (EF `HasIndex(...).HasFilter(...)` also works; the raw SQL is explicit and matches the existing check-constraint style.)
- **`ProfessionalPresenceTokens`** → mirror `CheckInTokenConfiguration`: `bytea` hash, unique `UX_ProfessionalPresenceTokens_TokenHash`, unique `UX_ProfessionalPresenceTokens_ProfessionalId`, FK to `Professionals` `NoAction`, `IsRowVersion`.
- **`RescheduleTokens`** → mirror exactly: unique `UX_RescheduleTokens_TokenHash`, unique `UX_RescheduleTokens_ReservationId`, FK to `Reservations` `NoAction`.
- **`Reservations.CancellationReason`** `smallint NOT NULL DEFAULT 0`; **`ProfessionalAvailabilityExceptions.Origin`** `smallint NOT NULL DEFAULT 0`. Enum stored as `short` (the codebase stores `AvailabilityMode` / `DayOfWeek` as `smallint`).

- [ ] **Step 1: Write the failing persistence test** — `ModulePersistenceTests.cs`: create + `SaveChanges` a `ProfessionalPresence`, a `ProfessionalPresenceToken`, a `RescheduleToken`; reload and assert fields round-trip and `Version` is populated. Assert a second open `ProfessionalPresence` for the same professional throws `DbUpdateException` (partial unique index). Assert an existing `Reservation` reloads with `CancellationReason == None` and an existing exception with `Origin == Planned`.
- [ ] **Step 2: Run** `dotnet test tests/GestaoPredio.IntegrationTests --filter FullyQualifiedName~ModulePersistenceTests` → FAIL (no tables / no columns).
- [ ] **Step 3: Implement** the three configuration classes + `DbSet`s + the two column mappings. Then generate the migration:
  `dotnet ef migrations add ProfessionalPresenceAndRescheduling --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --context ApplicationDbContext --output-dir Persistence/Migrations/PostgreSql`
  Hand-edit the generated `Up`/`Down` to add/drop the partial unique index via `migrationBuilder.Sql(...)`. Verify `Up` is **additive only** (new tables + two `AddColumn` with `defaultValue: (short)0`, no `DropColumn`, no `AlterColumn` on existing data).
- [ ] **Step 4: Apply to the guarded local DB and run tests**
  - `dotnet ef database update --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --context ApplicationDbContext` (this only ever targets the local `localhost:5432/LumisDev` per `LocalPostgreSqlTestDatabase` / user-secrets).
  - `dotnet test tests/GestaoPredio.IntegrationTests --filter "FullyQualifiedName~ModulePersistenceTests|FullyQualifiedName~MigrationSafetyTests"` → PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: persist presence, presence tokens and reschedule tokens"`

---

## Task 10: Professional presence endpoints (QR issue + own status)

**Files:**
- Create: `recepcaototem/Features/Professionals/ProfessionalPresenceEndpoints.cs` (group `/api/professional`, policy `Professional`)
- Create: `recepcaototem/Features/Professionals/ProfessionalPresenceRateLimiter.cs` (copy `CustomerPublicRateLimiter`)
- Modify: `recepcaototem/Program.cs` — `builder.Services.AddSingleton<ProfessionalPresenceRateLimiter>();` and `app.MapProfessionalPresenceEndpoints();` (before the `/api/{**path}` catch-all)
- Modify: `src/GestaoPredio.Domain/Auditing/AuditActions.cs` + `AuditTargetTypes.cs` (new constants — spec §16)
- Test: `tests/GestaoPredio.IntegrationTests/ProfessionalPresenceApiTests.cs`

**Interfaces — Produces:**
- `POST /api/professional/presence/qr` → `{ token: string, expiresAt: DateTimeOffset }` — creates/`Rotate`s the single `ProfessionalPresenceToken` for the caller's professional; audit `PROFESSIONAL_PRESENCE_QR_ISSUED` (target `PROFESSIONAL`).
- `GET /api/professional/presence` → `{ status: "PRESENT"|"ABSENT", since?: DateTimeOffset, absentUntil?: DateTimeOffset }` — derived for the caller via `PresenceEvaluator`; `absentUntil` from an `Origin = Incident` exception with `EndTime > now`.
- New `AuditActions`: `ProfessionalPresenceQrIssued = "PROFESSIONAL_PRESENCE_QR_ISSUED"`, `ProfessionalPresenceStarted = "PROFESSIONAL_PRESENCE_STARTED"`, `ProfessionalPresenceStartedByOperations`, `ProfessionalPresenceEndedByOperations`, `ProfessionalIncidentReportedNextAppointment`, `ProfessionalIncidentReportedUntilTime`, `ProfessionalIncidentReportedRestOfDay`, `ReservationCancelledProfessionalUnavailable = "RESERVATION_CANCELLED_PROFESSIONAL_UNAVAILABLE"`, `RescheduleLinkIssued = "RESCHEDULE_LINK_ISSUED"`, `RescheduleLinkConsumed = "RESCHEDULE_LINK_CONSUMED"`. New `AuditTargetTypes`: `ProfessionalPresence = "PROFESSIONAL_PRESENCE"`, `RescheduleToken = "RESCHEDULE_TOKEN"`.
- Professional-id resolution: `db.Professionals.Where(p => p.ApplicationUserId == userId && p.IsActive).Select(p => (Guid?)p.Id).SingleOrDefaultAsync()` (existing pattern); `null` → `404 PROFESSIONAL_PROFILE_NOT_LINKED`.

- [ ] **Step 1: Write the failing integration tests** — `ProfessionalPresenceApiTests.cs`:
  - login as a `PROFISSIONAL` linked to a professional; `POST /api/professional/presence/qr` → 200, `expiresAt - now ≈ 120s`, response has only `token` + `expiresAt`; decoding `token` yields 32 bytes; a second call rotates (different token, still one row).
  - `GET /api/professional/presence` before any scan → `ABSENT`.
  - an audit row `PROFESSIONAL_PRESENCE_QR_ISSUED` exists; no row contains the raw token or hash (assert the `AuditEntries` for this correlation have no `bytea`/base64 payload — there is no field for it, so simply assert the actions/targets).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ProfessionalPresenceApiTests` → FAIL.
- [ ] **Step 3: Implement** the endpoint group, the rate limiter (registered but only *used* by the Totem confirm in Task 11), the audit constants, and `Program.cs` wiring. Config `Presence:QrTokenTtlSeconds` (default 120) read via `IConfiguration.GetValue`.
- [ ] **Step 4: Run** → PASS; `dotnet build` clean.
- [ ] **Step 5: Commit** — `git commit -am "feat: professional presence QR issuance and own-status endpoint"`

---

## Task 11: Totem presence confirm + immediate availability

**Files:**
- Modify: `recepcaototem/Features/Totem/TotemEndpoints.cs`
- Test: `tests/GestaoPredio.IntegrationTests/TotemPresenceApiTests.cs`

**Interfaces — Consumes:** `ProfessionalPresenceToken`, `ProfessionalPresence`, `PresenceEvaluator`, `ProfessionalPresenceRateLimiter`, `IAppointmentAvailabilityService`, `ILeaseResourceLock`. **Produces:**
- `POST /api/totem/presence/confirm` (`AllowAnonymous`, `ProfessionalPresenceRateLimiter` per IP + per `SHA256(token)`) — body `{ token }`:
  1. decode base64url → 32 bytes (else generic `INVALID_PRESENCE`); `hash = SHA256`; lookup `ProfessionalPresenceToken` by `TokenHash`.
  2. reject generically if not found / `RevokedAt` / `ExpiresAt <= now` / `UsedAt`.
  3. `BeginTransactionAsync` + `resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [], [professionalId]))`.
  4. load the professional's open `ProfessionalPresence` + today's `OperatingHourIntervals`.
  5. if an open presence exists but `!PresenceEvaluator.IsEffective` → `MaterialiseOperatingHoursEnd(PresenceEvaluator.OperatingHoursEndInstant(...) ?? now)`.
  6. if an **effective** open presence exists → `token.MarkUsed(now)`, `SaveChanges`, commit, return `{ status: "PRESENT" }` (decision D — idempotent, no second row).
  7. else `db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(professionalId, now))`; `token.MarkUsed(now)`; audit `PROFESSIONAL_PRESENCE_STARTED` (target `PROFESSIONAL_PRESENCE`, id = presence id); `SaveChanges`; commit. Catch `DbUpdateException` from the partial unique index → treat as idempotent success (`{ status: "PRESENT" }`).
  8. **No session/cookie is issued.**
- `GET /api/totem/immediate?durationMinutes=<15..480, %15>` (`AllowAnonymous`, `CustomerPublicRateLimiter`) → `TotemProfessionalResponse[]` limited to professionals who are **both** `PresenceEvaluator.IsEffective == PRESENT` **and** `IAppointmentAvailabilityService.FindAvailableRoomAsync(id, now, now + duration, null, null).IsAvailable` (call inside a read transaction as `EnsureTransaction` requires). No second availability engine.

- [ ] **Step 1: Write the failing integration tests** — `TotemPresenceApiTests.cs`:
  - issue a QR (via the Task 10 endpoint), `POST /api/totem/presence/confirm` → `{ status: "PRESENT" }`; a `ProfessionalPresence` row is open; `GET /api/professional/presence` (as that professional) → `PRESENT`.
  - **second confirm with the same token** → generic `INVALID_PRESENCE` (token consumed).
  - issue a fresh QR while already present, confirm → idempotent `PRESENT`, still exactly one open presence row, token consumed.
  - expired token (advance the injected `TimeProvider` past 120s, or issue with a manipulated fixture) → generic `INVALID_PRESENCE`.
  - QR payload has no PII: the request/response carry only the opaque token / `{status}`.
  - `GET /api/totem/immediate` — with OperatingHours seeded, one professional PRESENT and centrally available → listed; same professional made ABSENT (manual end) → not listed; professional PRESENT but outside personal availability → not listed.
  - two parallel confirms of the same token (fire two `PostWithCsrfAsync`… actually `AllowAnonymous` so plain `PostAsync`) → exactly one open presence, both requests resolve (one `PRESENT`, one generic error or idempotent `PRESENT`), one `PROFESSIONAL_PRESENCE_STARTED` audit row.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~TotemPresenceApiTests` → FAIL.
- [ ] **Step 3: Implement** the two endpoints in `TotemEndpoints.cs` following `FindCheckIn` / `ConfirmCheckIn` structure. Reuse `CustomerPublicRateLimiter` for `immediate`; use `ProfessionalPresenceRateLimiter` for `presence/confirm`.
- [ ] **Step 4: Run** → PASS; run the full `TotemEndpoints`-related suites (`CustomerApiTests`, existing totem tests) to confirm no regression.
- [ ] **Step 5: Commit** — `git commit -am "feat: totem presence confirmation and immediate-availability endpoint"`

---

## Task 12: Public rescheduling endpoints

**Files:**
- Create: `recepcaototem/Features/Rescheduling/ReschedulingEndpoints.cs` (+ contracts record file if preferred)
- Create: `recepcaototem/Features/Rescheduling/RescheduleTokenRateLimiter.cs` (copy `CustomerPublicRateLimiter`)
- Modify: `recepcaototem/Program.cs` — register the limiter singleton + `app.MapReschedulingEndpoints();`
- Test: `tests/GestaoPredio.IntegrationTests/ReschedulingApiTests.cs`

**Interfaces — Consumes:** `RescheduleToken`, `Reservation.CreateApprovedReplacementForIncident`, `IAppointmentAvailabilityService`, `ILeaseResourceLock`, `ReservationCheckInTokenRevocation` (not needed here), `IReservationConflictDetector` (via the service). **Produces (all `AllowAnonymous`, all `RescheduleTokenRateLimiter` per IP + per `SHA256(token)`):**
- `POST /api/reschedule/resolve` — `{ token }` → `{ professionalId, professionalName, originalStartAt, originalEndAt, durationMinutes, expiresAt }` or generic `INVALID_RESCHEDULE_LINK` (404 not-found / expired / used / revoked / original not `Cancelled+ProfessionalUnavailable` — one error for all).
- `GET /api/reschedule/slots?token=<>&date=YYYY-MM-DD` → `AvailabilitySlotResponse[]` from `IAppointmentAvailabilityService.FindSlotsAsync(original.ProfessionalId, date, durationMinutes, ct)` (re-validate the token first; generic error on failure).
- `POST /api/reschedule/confirm` — `{ token, startAt, endAt }` → `{ reservationId, startAt, endAt, professionalName, roomName }`. Flow exactly per spec §10.4:
  1. decode/hash/lookup; generic error on miss.
  2. `ExpiresAt > now && RevokedAt is null && UsedAt is null` else generic error.
  3. load original; `Status == Cancelled && CancellationReason == ProfessionalUnavailable`; load `customer` by `original.CustomerId`, `IsActive`.
  4. validate `endAt > startAt`, same local civil day, `(endAt-startAt)` matches `(original.EndAt-original.StartAt)` within 1 minute.
  5. `BeginTransactionAsync` + `resourceLock.AcquireAsync([], activeRoomIds, [original.ProfessionalId])`.
  6. `available = FindAvailableRoomAsync(original.ProfessionalId, startAt, endAt, requiredRoomId: original.RoomId, excludedReservationId: null)`; if `!IsAvailable` retry once with `requiredRoomId: null` (decision C). Still not → `AppointmentAvailabilityResults.Conflict(available.Failure)` (409), **do not** consume the token.
  7. `replacement = Reservation.CreateApprovedReplacementForIncident(original, available.RoomId!.Value, startAt, endAt, "RESCHEDULE_LINK", now)` (the factory takes `roomId` — defined in Task 4).
  8. `token.MarkUsed(now)`; `db.Reservations.Add(replacement)`; audit `RESERVATION_RESCHEDULED` (target `RESERVATION`) + `RESCHEDULE_LINK_CONSUMED` (target `RESCHEDULE_TOKEN`).
  9. `SaveChangesAsync` → on `DbUpdateConcurrencyException` (token `xmin`) rollback + generic error; else commit.

- [ ] **Step 1: Write the failing integration tests** — `ReschedulingApiTests.cs` (`ModulesApiFactory`, seed OperatingHours + a professional + a customer + an `Approved` reservation, then cancel it with `ProfessionalUnavailable` and insert a `RescheduleToken` via a scope):
  - `resolve` with a valid token → the 6-field payload, no phone/email/CustomerId anywhere.
  - `resolve` after `ExpiresAt` (seed the token already expired) → `INVALID_RESCHEDULE_LINK`.
  - `slots` returns the same shape as `/api/customer/availability` for the same professional/date/duration (assert equality against a direct `FindSlotsAsync` or the customer endpoint).
  - `confirm` with a valid slot → 200; a new `Reservation` with `Kind = Reschedule`, `OriginalReservationId == original.Id`, original still `Cancelled`; the token's `UsedAt` set.
  - **second `confirm`** with the same token → `INVALID_RESCHEDULE_LINK`; still exactly one replacement reservation.
  - two parallel `confirm` calls on the same token → exactly one replacement; the other gets a generic error.
  - `confirm` when the chosen slot was taken by another reservation in between → 409, token **not** consumed, a later `confirm` with a free slot succeeds.
  - a Totem-created customer (no `ApplicationUserId`) reschedules successfully with **no login** (no auth header, no CSRF).
  - a logged-in customer opening the link uses the same anonymous endpoints and gets **no** session / history exposure (the response has no reservation list).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ReschedulingApiTests` → FAIL.
- [ ] **Step 3: Implement** the endpoint group + rate limiter + `Program.cs` wiring. `Rescheduling:PublicBaseUrl` and `Rescheduling:LinkTtlHours` (default 48) from config; `RescheduleUrl = $"{PublicBaseUrl}/reagendar/{base64url(raw)}"`.
- [ ] **Step 4: Run** → PASS; `dotnet build` clean.
- [ ] **Step 5: Commit** — `git commit -am "feat: public 48h rescheduling link endpoints"`

---

## Task 13: Incident endpoint (the three options)

**Files:**
- Modify: `recepcaototem/Features/Professionals/ProfessionalPresenceEndpoints.cs` (add `POST /api/professional/incidents` to the `Professional` group)
- Test: `tests/GestaoPredio.IntegrationTests/ProfessionalIncidentApiTests.cs`

**Interfaces — Consumes:** everything from Tasks 4–7, 12 (`RescheduleToken.Create`), `INotificationService.NotifyCustomerAsync`. **Produces:**
- `POST /api/professional/incidents` — body `{ type: "NEXT_APPOINTMENT"|"UNTIL_TIME"|"REST_OF_DAY", untilTime?: "HH:mm", reason?: string }` (`reason` ≤300, internal). One transaction + `resourceLock.AcquireAsync([], [], [professionalId])`. Steps exactly per spec §7:
  1. resolve professional from caller.
  2. compute local `[from, to]` window for **today** (spec §7 table): `NEXT_APPOINTMENT` = the next affected reservation's `[StartAt, EndAt]` (local); `UNTIL_TIME` = `[local(now), untilTime]` (`untilTime` must be `> local(now)` and `<= end of civil day`, else `400 INVALID_INCIDENT`); `REST_OF_DAY` = `[local(now), min(end of civil day, PresenceEvaluator.OperatingHoursEndInstant(today) local)]`.
  3. `ProfessionalAvailabilityException.Create(professionalId, today, allDay: false, from, to, reason, now, origin: Incident)` — **skip the endpoint-level `OverlapsAsync` guard** (system-authored). If `NEXT_APPOINTMENT` and there is no future affected reservation → no-op success `{ affectedReservationIds: [] }`, no exception created.
  4. affected reservations: `Status == Approved && Kind != Cancellation && EndAt > now && [StartAt,EndAt) overlaps [fromUtc,toUtc)`; for `NEXT_APPOINTMENT` take only the earliest.
  5. per reservation: `reservation.Cancel("PROFESSIONAL_INCIDENT", now, ReservationCancellationReason.ProfessionalUnavailable)` (catch `InvalidOperationException` per row → already handled, skip); `ReservationCheckInTokenRevocation.RevokeAsync(db, reservation.Id, now, ct)`; `raw = GetBytes(32)`, `RescheduleToken.Create(reservation.Id, SHA256(raw), now, now + 48h)` (or `Rotate` if a row exists); audit `RESERVATION_CANCELLED_PROFESSIONAL_UNAVAILABLE` + `RESCHEDULE_LINK_ISSUED`; keep the `raw`/URL in memory for step 9.
  6. presence: `NEXT_APPOINTMENT` → untouched; `UNTIL_TIME`/`REST_OF_DAY` → `openPresence?.EndForIncident(restOfDay: type == REST_OF_DAY, now)`.
  7. audit the incident: `PROFESSIONAL_INCIDENT_REPORTED_*` (target `PROFESSIONAL`) — **type only, never the reason**.
  8. `SaveChangesAsync` + `CommitAsync`.
  9. **after commit**, per cancelled reservation: `notifications.NotifyCustomerAsync(new CustomerNotificationEvent(customerId, CustomerReservationCancelledReschedule, reservationId, professionalName, originalStartAt, rescheduleUrl), CancellationToken.None)`. Failure logged, **no rollback**.
- Response: `{ exceptionId: Guid?, affectedReservationIds: Guid[], presence: "PRESENT"|"ABSENT" }`.

- [ ] **Step 1: Write the failing integration tests** — `ProfessionalIncidentApiTests.cs` (seed OperatingHours + professional + customer + two future `Approved` reservations + one already-`Cancelled` + one in the past; make the professional `PRESENT` first via the QR flow):
  - **NEXT_APPOINTMENT**: only the earliest future reservation → `Cancelled` with `ProfessionalUnavailable`; a `ProfessionalAvailabilityException` (`Origin = Incident`) equal to that reservation's interval exists; the *second* future reservation stays `Approved`; **presence stays `PRESENT`**; a `RescheduleToken` for the cancelled one exists; `NotifyCustomerAsync` recorded (Demo).
  - **UNTIL_TIME** (now 14:00, until 16:00): every reservation overlapping `[14:00,16:00]` cancelled; exception `[14:00,16:00]`; **presence `ABSENT` immediately**; after advancing the clock past 16:00, `GET /api/customer/availability` for that professional returns slots again (availability recovered, no job) **but** `GET /api/professional/presence` is still `ABSENT` and a fresh QR + Totem confirm is required to become `PRESENT` again.
  - **REST_OF_DAY**: all future affected reservations cancelled; exception `[now, last close]`; **presence `ABSENT`**; `GET /api/totem/immediate` no longer offers the professional today.
  - internal `reason` is on the exception row, absent from the incident `AuditEntry` (assert the audit rows for this correlation contain no `reason` text).
  - a reservation strictly outside the window is **not** cancelled.
  - replaying the same incident request → no double cancellation (already-`Cancelled` rows skipped); idempotent.
  - **no `Visit` is touched**: seed a `Waiting` visit for the professional; after the incident it is still `Waiting`; `GET /api/operational-alerts` now contains `OPEN_VISIT_AFFECTED_BY_INCIDENT`.
  - end-to-end: take the `RescheduleUrl` from the Demo recorder / DB, hit `/api/reschedule/resolve` + `/confirm` → a replacement reservation is created.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ProfessionalIncidentApiTests` → FAIL.
- [ ] **Step 3: Implement** the endpoint. Local-time helpers via the injected `TimeZoneInfo`; overlap math on `DateTimeOffset`.
- [ ] **Step 4: Run** → PASS; run `ProfessionalAvailabilitySchedulingTests`, `ReservationWorkflowTests`, `VisitApiTests` → PASS (no regression).
- [ ] **Step 5: Commit** — `git commit -am "feat: professional incident flow with three scopes and auto-cancellation"`

---

## Task 14: Reception manual presence + status projection

**Files:**
- Modify: `recepcaototem/Features/Reception/ReceptionEndpoints.cs` + `ReceptionContracts.cs`
- Test: `tests/GestaoPredio.IntegrationTests/ReceptionApiTests.cs` (add cases)

**Interfaces — Produces:**
- `POST /api/reception/presence` (policy `Operations`, `AntiforgeryFilter`) — `{ professionalId, state: "PRESENT"|"ABSENT" }`:
  - `PRESENT` → transaction + professional lock → close any stale open presence (`MaterialiseOperatingHoursEnd`) → if none effective, `ProfessionalPresence.StartByManager(professionalId, actorUserId, now)` → audit `PROFESSIONAL_PRESENCE_STARTED_BY_OPERATIONS`.
  - `ABSENT` → `openPresence.EndManually(actorUserId, now)` → audit `PROFESSIONAL_PRESENCE_ENDED_BY_OPERATIONS`. No open presence → `409` generic.
  - concurrency: professional lock + `xmin` → second concurrent write `DbUpdateConcurrencyException` → `RESOURCE_MODIFIED` (409).
- `ReceptionProfessionalResponse` gains `string Presence` (`"PRESENT"|"ABSENT"`) and `DateTimeOffset? AbsentUntil`; populated in `ReceptionEndpoints.Professionals` and `Overview` from `PresenceEvaluator` + the open-presence rows + today's `OperatingHourIntervals` + `Origin = Incident` exceptions (batch-load once for all listed professionals). The existing `OperationalStatus` string is unchanged (orthogonal field).

- [ ] **Step 1: Write the failing integration tests** in `ReceptionApiTests.cs`:
  - `POST /api/reception/presence {PRESENT}` as a manager → `GET /api/reception/professionals` shows that professional `Presence == "PRESENT"`; `{ABSENT}` → `"ABSENT"`; both produce audit rows (`_BY_OPERATIONS`).
  - a `PROFISSIONAL` calling `POST /api/reception/presence` → `403` (policy `Operations`).
  - a professional with an active `UNTIL_TIME` incident → `Presence == "ABSENT"`, `AbsentUntil` = that `EndTime` instant.
  - two concurrent manager writes → one `RESOURCE_MODIFIED`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ReceptionApiTests` → new cases FAIL.
- [ ] **Step 3: Implement** the endpoint + the two response fields + the batch projection query.
- [ ] **Step 4: Run** → PASS; run the full `ReceptionApiTests` → PASS.
- [ ] **Step 5: Commit** — `git commit -am "feat: reception manual presence fallback and presence projection"`

---

## Task 15: Full verification & report

- [ ] `dotnet build` (solution) → 0 warnings/errors related to these changes.
- [ ] `dotnet test tests/GestaoPredio.UnitTests` → all green.
- [ ] `dotnet test tests/GestaoPredio.IntegrationTests` → all green (if the local `LumisDev` Postgres is unavailable, record which suites could not run and fall back to unit tests + build; do **not** claim integration coverage that did not execute).
- [ ] `dotnet ef migrations list --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --context ApplicationDbContext` → the new migration is listed and applied to local `LumisDev` only.
- [ ] `dotnet ef migrations script <previous> ProfessionalPresenceAndRescheduling --idempotent --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --output artifacts/sql/ProfessionalPresenceAndRescheduling.sql` — inspect it is additive only (no `DROP`, no destructive `ALTER`).
- [ ] `git -C ../.. diff --check`; `git status --short`; `git log --oneline` of the new commits.
- [ ] Report: aggregates added, presence model, QR model, the three incident scopes, reservation cancellation, Visit preservation, the 48h link, WhatsApp integration point, migrations applied, any divergence hit during implementation, and anything left for a decision. **No push, no merge, no deploy.**

---

## Self-Review

**1. Spec coverage**

| Spec section | Task(s) |
|---|---|
| §2/§3 model, aggregates, additive changes | 1, 2, 3, 4, 5, 9 |
| §4 presence concepts + QR arrival + manual fallback | 6, 10, 11, 14 |
| §4.6 / §14 rate limiting for public presence | 10 (limiter), 11 (applied) |
| §5 automatic end by operating hours | 6 (`IsEffective`), 11 (materialise on confirm) |
| §6 no background job | 6 + every write path materialises opportunistically; §15 asserts no scheduler |
| §7 incident, three options, window per option | 13 |
| §8 customer check-in never blocked, Reception alert | 8 (alert), not-touched by 13; asserted in 8 & 13 tests |
| §9 affected reservations, immediate service gate | 11 (`/immediate`), 13 (cancellation) |
| §10 rescheduling public flow | 12 |
| §11 message | 7 (body), 13 (send) |
| §12 Reception alerts & status, Visits untouched | 8, 14, and the "no Visit" assertions in 13 |
| §13 API surface | 10, 11, 12, 13, 14 |
| §14 authorization | policies on every group (10/13 `Professional`, 14 `Operations`, 11/12 `AllowAnonymous`+limiter); asserted in 10/12/14 tests |
| §15 concurrency guarantees | 9 (partial unique index), 11, 12, 13, 14 tests |
| §16 audit | 10 (constants), 11, 12, 13, 14 (inline) |
| §17 migration set | 9 |
| §18 frontend contracts | documented in the spec; not implemented (out of scope) |
| §19 tests | every task's Step 1 maps to the spec §19 checklist |
| §20 compatibility | additive-only asserted in 9 (`MigrationSafetyTests`) and by leaving `Cancel`/`Create` call sites unchanged |
| §21 out of scope | respected — no frontend, no camera, no real Meta |
| §22 decisions A–G | A→3.5 (no entity), B→7/8 (no attempt table/alert), C→12 step 6, D→11 step 6, E→6, F→10 constants + inline, G→10 config default |

No spec requirement is left without a task.

**2. Placeholder scan** — no "TBD"/"handle edge cases"/"similar to Task N". Test code is given inline for the domain tasks (1–6) and specified case-by-case for the integration tasks (8, 10–14), each case traceable to spec §19. Migration body edits (partial unique index SQL) are spelled out in Task 9.

**3. Type consistency** — `ProfessionalPresence` / `PresenceEndReason` / `PresenceSource` defined in Task 1 and used verbatim in 6, 9, 10, 11, 13, 14. `ReservationCancellationReason` (Task 4) used in 8, 12, 13. `ProfessionalAvailabilityExceptionOrigin` (Task 5) used in 8, 13, 14. `RescheduleToken` (Task 3) used in 9, 12, 13. `CustomerNotificationEvent` / `NotifyCustomerAsync` (Task 7) used in 13. `PresenceEvaluator.IsEffective` / `OperatingHoursEndInstant` (Task 6) used in 8, 11, 13, 14. `CreateApprovedReplacementForIncident` carries the `roomId` parameter from its definition in Task 4, consumed unchanged in Task 12 step 7.

**4. Scope** — one migration, one plan, backend only; each task ends with an independently testable deliverable and its own commit. The heaviest task (13, incident) depends only on already-merged tasks 1–12.
