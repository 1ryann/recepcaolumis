# Professional presence, incidents and customer rescheduling — design

Date: 2026-09-08
Branch / base: `codex/reception-backend`
Status: spec for review. **No code, no migration, no DB change, no deploy, no Supabase, no Meta, no Intelbras in this document.**
Operational timezone: `America/Porto_Velho` (resolved through `OperationalTimeZone.Resolve` / the `TimeZoneInfo` singleton, UTC‑4, no DST).

---

## 1. Problem

Lumis knows a professional's **availability** (when they accept appointments) but not their **physical presence** (whether they are in the building right now). Three gaps:

1. There is no record of a professional physically arriving.
2. A professional who has an unexpected problem ("imprevisto") has no way to pull the affected future appointments off their agenda in one action.
3. When appointments are cancelled because of that problem, the customer has no simple, login‑free way to pick a new time.

The solution must stay simple: few screens, few options, one primary action for the customer.

---

## 2. Architecture

Clean‑architecture layering already in place is preserved:

- **Domain** (`src/GestaoPredio.Domain`): three new small aggregates (`ProfessionalPresence`, `ProfessionalPresenceToken`, `RescheduleToken`), two additive changes to existing aggregates (`Reservation.CancellationReason`, `ProfessionalAvailabilityException.Origin`), one new domain factory (`Reservation.CreateApprovedReplacementForIncident`).
- **Application** (`src/GestaoPredio.Application`): a `PresenceEvaluator` pure function (no state, mirrors `OperatingHoursEvaluator` / `ProfessionalAvailabilityEvaluator`); the `INotificationService` abstraction gains a customer channel; `IOperationalAlertReader` gains derived alert types. **The central availability service (`IAppointmentAvailabilityService`) is reused unchanged** — presence is an extra gate the callers apply, never a second engine.
- **Infrastructure** (`src/GestaoPredio.Infrastructure`): EF configurations + one PostgreSQL migration; `NotificationService` gains a customer path; `PostgreSqlOperationalAlertReader` gains the derived rows.
- **API host** (`recepcaototem/Features`): new endpoint groups `ProfessionalPresenceEndpoints`, `ReschedulingEndpoints`; additions to `TotemEndpoints` and `ReceptionEndpoints`; two new rate limiters registered in `Program.cs` next to `CustomerPublicRateLimiter`.

No background job, no cron, no worker, no queue, no Redis. Every "it should end automatically" requirement is satisfied by **computing** the effective status at read time; a write path may *opportunistically materialise* a terminal state when it already holds the row, but correctness never depends on that.

Everything additive. No column is dropped, no enum value is renumbered, no endpoint changes shape.

---

## 3. Domain model (proposed)

Conventions observed in the codebase and reused verbatim:

- Private parameterless ctor, `static Create(...)` factory, guard clauses throwing `ArgumentException`.
- `Guid Id` primary key; `uint Version` mapped to PostgreSQL `xmin` via `.Property(x => x.Version).IsRowVersion()`.
- Timestamps normalised through `TimestampNormalizer.ToUtcMicroseconds` and stored as `timestamp with time zone`.
- Opaque tokens: 32 random bytes from `RandomNumberGenerator.GetBytes(32)`, transported as `WebEncoders.Base64UrlEncode`, **persisted only as `SHA256.HashData(rawBytes)` (`bytea`, 32 bytes)**, looked up by hash, unique index on the hash, generic error on every miss (see `CheckInToken` + `TotemEndpoints.FindCheckIn`).

### 3.1 `ProfessionalPresence` (new aggregate)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `ProfessionalId` | `Guid` | FK → `Professionals` (`OnDelete: NoAction`) |
| `StartedAt` | `DateTimeOffset` | when presence was opened |
| `EndedAt` | `DateTimeOffset?` | set only by a manual close or an incident that pauses/ends presence; **never set by "end of operating hours"** |
| `EndReason` | `PresenceEndReason?` (`smallint`) | `ManagerManual`, `IncidentUntilTime`, `IncidentRestOfDay`, `OperatingHoursElapsed` (last one only ever written by opportunistic materialisation) |
| `Source` | `PresenceSource` (`smallint`) | `QrSelfScan`, `ManagerManual` |
| `CreatedByUserId` | `string?` (≤450) | the Operations user for a manual open; `null` for QR |
| `Version` | `uint` | `xmin` |

**Invariant — at most one open presence per professional.** Enforced at the database with a partial unique index:

```
CREATE UNIQUE INDEX "UX_ProfessionalPresence_Open"
  ON "ProfessionalPresence" ("ProfessionalId") WHERE "EndedAt" IS NULL;
```

This is the primary concurrency guarantee for "two scans / two manual marks do not create two presences" (§30). The QR token's `UsedAt` single‑use flag is the second, independent guard.

Domain surface:

- `ProfessionalPresence.StartByQr(Guid professionalId, DateTimeOffset now)`
- `ProfessionalPresence.StartByManager(Guid professionalId, string managerUserId, DateTimeOffset now)`
- `void EndManually(string managerUserId, DateTimeOffset now)` → sets `EndedAt`, `EndReason = ManagerManual`
- `void EndForIncident(bool restOfDay, DateTimeOffset now)` → `EndReason = IncidentRestOfDay | IncidentUntilTime`
- `void MaterialiseOperatingHoursEnd(DateTimeOffset closeInstant)` → sets `EndedAt = closeInstant`, `EndReason = OperatingHoursElapsed`; used only by opportunistic materialisation, only when the row is already stale (see §6).

### 3.2 `ProfessionalPresenceToken` (new aggregate — the QR)

Mirrors `CheckInToken` exactly, keyed on the professional instead of a reservation.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `ProfessionalId` | `Guid` | FK → `Professionals` |
| `TokenHash` | `byte[]` (`bytea`, 32) | `SHA256(raw)`; **unique index** |
| `IssuedAt` | `DateTimeOffset` | |
| `ExpiresAt` | `DateTimeOffset` | `IssuedAt + Presence:QrTokenTtlSeconds` (default **120s**) |
| `UsedAt` | `DateTimeOffset?` | single use |
| `RevokedAt` | `DateTimeOffset?` | |
| `Version` | `uint` | `xmin` |

- **Unique index on `ProfessionalId`** — one row per professional, re‑issue calls `Rotate(newHash, now, expiresAt)` (which also clears `UsedAt`/`RevokedAt`), identical to `CheckInToken.Rotate`.
- `Create(professionalId, tokenHash, issuedAt, expiresAt)` validates 32‑byte hash and `expiresAt > issuedAt`.
- `MarkUsed(now)`, `Revoke(now)`.
- The QR **payload carries only the base64url token string** (optionally prefixed with an app path). No `ProfessionalId`, name, phone, email — none of it is in the token or derivable from it.

### 3.3 `RescheduleToken` (new aggregate — the 48h link)

Mirrors `CheckInToken`, keyed on the cancelled reservation.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `ReservationId` | `Guid` | FK → `Reservations`; the **cancelled original**; unique index |
| `TokenHash` | `byte[]` (`bytea`, 32) | `SHA256(raw)`; unique index |
| `IssuedAt` | `DateTimeOffset` | at cancellation |
| `ExpiresAt` | `DateTimeOffset` | `IssuedAt + Rescheduling:LinkTtlHours` (default **48h**) |
| `UsedAt` | `DateTimeOffset?` | single use |
| `RevokedAt` | `DateTimeOffset?` | |
| `Version` | `uint` | `xmin` |

- `Create(reservationId, tokenHash, issuedAt, expiresAt)`, `MarkUsed(now)`, `Revoke(now)`, `Rotate(...)` (in case the notification is retried before use).

### 3.4 Additive changes to existing aggregates

**`Reservation`** (`src/GestaoPredio.Domain/Reservations/Reservation.cs`)

- New enum `ReservationCancellationReason { None = 0, ProfessionalUnavailable = 1 }`.
- New property `ReservationCancellationReason CancellationReason { get; private set; }` — column `smallint NOT NULL DEFAULT 0`. `None` for every existing and normal cancel.
- `Cancel(string actorUserId, DateTimeOffset occurredAt, ReservationCancellationReason reason = ReservationCancellationReason.None)` — one new optional parameter; existing callers unaffected. Still `EnsureApprovedActualReservation()`.
- New factory `Reservation.CreateApprovedReplacementForIncident(Reservation original, DateTimeOffset startAt, DateTimeOffset endAt, string actorUserId, DateTimeOffset occurredAt)`:
  - requires `original.Status == Cancelled && original.CancellationReason == ProfessionalUnavailable` (otherwise `InvalidOperationException`);
  - produces `Kind = Reschedule`, `Status = Approved`, `OriginalReservationId = original.Id`, `CustomerId = original.CustomerId`, `ProfessionalId = original.ProfessionalId`;
  - **does not** call `EnsureMinimumNotice` (consistent with the existing `CreateApprovedReschedule`, which also skips it);
  - room is chosen by the caller from the central availability service, not copied blindly.

  Rationale for a new factory rather than reusing `CreateApprovedReschedule`: that method calls `original.EnsureApprovedActualReservation()` and throws for a cancelled original. The incident flow *cancels first*, then the customer reschedules up to 48h later against a reservation that is already `Cancelled`. This factory is the minimal seam.

**`ProfessionalAvailabilityException`** (`src/GestaoPredio.Domain/Professionals/ProfessionalAvailabilityException.cs`)

- New enum `ProfessionalAvailabilityExceptionOrigin { Planned = 0, Incident = 1 }`.
- New property `Origin` — column `smallint NOT NULL DEFAULT 0`.
- `Create(...)` gains a trailing `ProfessionalAvailabilityExceptionOrigin origin = Planned` parameter. The manual planned‑exception endpoints keep passing nothing; the incident flow passes `Incident`.
- No other change: the incident reuses the existing shape (`Date` = today, `AllDay = false`, `StartTime`/`EndTime` = the window), the existing check constraint, and the existing `ProfessionalAvailabilityEvaluator` subtraction. **No parallel agenda‑blocking logic is introduced (§15).**

### 3.5 Why no separate `ProfessionalIncident` entity

§15 asks to justify if a dedicated incident record is *not* created. It is not, because every need it would serve is already covered:

- **Agenda blocking** → `ProfessionalAvailabilityException` (reused, with `Origin = Incident`).
- **Internal reason** → `ProfessionalAvailabilityException.Reason` (existing, 300 chars, internal, never sent to the customer, never in free logs).
- **Grouping the affected reservations** → they are cancelled synchronously in the same transaction; each cancel + each reschedule‑token issuance carries the same audit `CorrelationId` (the request `TraceIdentifier`). No cross‑row parent needed for the customer, who only ever sees one reservation per link.
- **"Active incidents" for Reception** → derived: an exception with `Origin = Incident`, `Date = today`, `StartTime <= now_local < EndTime`.
- **Notification** → per affected reservation, after commit.

A dedicated table would be state we can derive — explicitly disallowed by §26. **Decision point A:** if the product later wants an incident to survive as a first‑class object (e.g. "the professional cancelled 4 appointments at 14:05" as one audit line with a count), the minimal addition is a `ProfessionalIncident` row (`Id`, `ProfessionalId`, `ReportedAt`, `Type`, `ScopeEndsAt?`, `Version`) referenced by the exception and by each cancelled reservation. Not built now (YAGNI).

---

## 4. Presence

### 4.1 Concepts

- **Availability** = `ProfessionalAvailabilityEvaluator.GetEffectiveRanges(...)` — unchanged.
- **Presence** = there is an *effective* open `ProfessionalPresence` row.

They are independent: a professional can be `PRESENT` outside their personal availability (e.g. finished appointments at 15:00, still in the building until the establishment closes at 18:30) and can be `AVAILABLE` for future bookings while `ABSENT`.

### 4.2 `PresenceEvaluator` (pure, Application layer)

```
static bool IsEffective(
    ProfessionalPresence? openPresence,
    IReadOnlyCollection<OperatingHourInterval> operatingHoursForCivilDay,
    DateTimeOffset now,
    TimeZoneInfo zone)
```

`true` iff **all** of:

1. `openPresence is not null && openPresence.EndedAt is null`;
2. `operatingHoursForCivilDay` is non‑empty (establishment open today) — **fail‑closed when OperatingHours is not configured or the day is closed (§31)**;
3. the local civil day of `now` equals the local civil day of `openPresence.StartedAt` (a presence never carries past midnight — a professional who does not scan out is `ABSENT` the next day until they scan again);
4. `TimeOnly.FromDateTime(local(now)) <= max(interval.ClosesAt for operatingHoursForCivilDay)` — presence stays effective through gaps (lunch) up to the **last close of the day** (§5). It is *not* gated on the day's opening time; a professional who scans in early is present from the scan.

`ABSENT` = the negation: no open presence, or ended, or now past the day's last close, or a new civil day, or the establishment is not open today.

### 4.3 Registering arrival (QR)

1. Professional area → **"Registrar chegada"** → `POST /api/professional/presence/qr` (policy `Professional`).
   - Resolve the caller's professional id: `db.Professionals.Where(p => p.ApplicationUserId == userId && p.IsActive)` (existing pattern).
   - `raw = RandomNumberGenerator.GetBytes(32)`, `hash = SHA256.HashData(raw)`.
   - Upsert the single `ProfessionalPresenceToken` for this professional: create or `Rotate(hash, now, now + ttl)`.
   - Audit `PROFESSIONAL_PRESENCE_QR_ISSUED` (target `PROFESSIONAL`, target id = professional id). **The raw token and the hash are never audited or logged (§28).**
   - Response: `{ token: base64url(raw), expiresAt }`. The phone renders it as a QR (frontend, later).
2. Totem → **"Sou profissional"** → camera reads QR → `POST /api/totem/presence/confirm { token }` (`AllowAnonymous`, rate‑limited — §4.6).
   - `bytes = Base64UrlDecode(token)`; length ≠ 32 → generic `INVALID_PRESENCE`.
   - `hash = SHA256(bytes)`; look up `ProfessionalPresenceToken` by `TokenHash == hash`.
   - Reject (single generic error `INVALID_PRESENCE`) if: not found, `RevokedAt is not null`, `ExpiresAt <= now`, `UsedAt is not null`.
   - Transaction + `ILeaseResourceLock.AcquireAsync(new LeaseResourceLockRequest([], [], [professionalId]))` (existing professional‑scoped lock).
   - Close any **stale** open presence for this professional (`EndedAt IS NULL` but `!PresenceEvaluator.IsEffective(...)`) via `MaterialiseOperatingHoursEnd(dayClose)` — this frees the partial unique index (see §6).
   - If an **effective** open presence already exists → idempotent success: consume the token (`MarkUsed`), do **not** insert a second presence, return `{ status: "PRESENT" }`.
   - Otherwise insert `ProfessionalPresence.StartByQr(professionalId, now)`; `token.MarkUsed(now)`; audit `PROFESSIONAL_PRESENCE_STARTED`.
   - Commit. If the partial unique index still rejects the insert (a concurrent scan won the race) → catch `DbUpdateException`, treat as idempotent success.
   - Response: `{ status: "PRESENT" }`. **The QR never creates a Totem login/session — it only records presence (§4).**

### 4.4 No normal sign‑out

There is no "scan out". Presence ends by computation at the establishment's last close for that civil day (§4.2, §5). Materialisation (§6) may write the terminal row later; it is not required.

### 4.5 Manual fallback (Operations only)

`POST /api/reception/presence` (policy `Operations`, behind `AntiforgeryFilter`, not public):

- `{ professionalId, state: "PRESENT" | "ABSENT" }`.
- `PRESENT` → transaction + professional lock → close any stale open presence → if none effective, `ProfessionalPresence.StartByManager(professionalId, managerUserId, now)` → audit `PROFESSIONAL_PRESENCE_STARTED_BY_OPERATIONS`.
- `ABSENT` → `openPresence.EndManually(managerUserId, now)` → audit `PROFESSIONAL_PRESENCE_ENDED_BY_OPERATIONS`.
- Concurrency: professional lock + `xmin` on the presence row; two concurrent manual writes → one gets `DbUpdateConcurrencyException` → `RESOURCE_MODIFIED` (409), the existing pattern.
- Purpose: camera/QR/phone failure, operational judgement. Always audited. Never exposed to Professional/Customer/Totem.

### 4.6 Rate limiting for the public presence endpoints

New `ProfessionalPresenceRateLimiter`, a copy of `CustomerPublicRateLimiter` (partitioned fixed‑window per IP and per `SHA256(token)`), registered as a singleton in `Program.cs`. Config keys `RateLimiting:PresenceIpPermitLimit` / `RateLimiting:PresenceTokenPermitLimit` / `RateLimiting:PresenceWindowSeconds`. Applies to `POST /api/totem/presence/confirm`. Anti‑enumeration: one generic `INVALID_PRESENCE` for not‑found / expired / used / revoked.

---

## 5. Automatic end by operating hours

The presence "ends" at the **last `ClosesAt` of the establishment's OperatingHours for that civil day** — never at the end of the professional's personal availability.

Example from the brief: professional's personal agenda ends 15:00, establishment closes 18:30 → presence effective until 18:30.

Mechanism: `PresenceEvaluator.IsEffective` returns `false` once `local(now) > lastClose`. The `ProfessionalPresence` row keeps `EndedAt = NULL` until (and unless) a write path materialises it.

`OperatingHoursForCivilDay(date)` = `db.OperatingHourIntervals.Where(i => i.DayOfWeek == date.DayOfWeek)` (the same slice `PostgreSqlAppointmentAvailabilityService.LoadContextAsync` already reads). Empty ⇒ establishment closed that day ⇒ presence not effective (fail‑closed).

---

## 6. No background job

Correctness of `PRESENT` / `ABSENT` is a **read‑time computation** — `PresenceEvaluator.IsEffective`. Nothing schedules a sweep.

Opportunistic materialisation, allowed but not relied upon: whenever a write path already loads a professional's open presence (QR confirm §4.3, manual mark §4.5, incident §7, the Reception overview if it chooses to persist), and the row is stale (`!IsEffective`), it may call `MaterialiseOperatingHoursEnd(lastCloseInstantForThatDay)` inside its existing transaction. This keeps the partial unique index clean so the next day's scan can open a fresh presence. If materialisation never runs, the read side still returns the correct status, and the QR‑confirm path explicitly closes the stale row before inserting.

`PRESENT` ⇔ open presence ∧ not ended ∧ `IsEffective(now)`.
`ABSENT` ⇔ no open presence ∨ ended ∨ `!IsEffective(now)`.

---

## 7. Incidents ("Informar imprevisto")

Professional area → **"Informar imprevisto"** → three options only, an optional internal reason, **"Confirmar"**.

`POST /api/professional/incidents` (policy `Professional`), `{ type, untilTime?, reason? }` where `type ∈ { NEXT_APPOINTMENT, UNTIL_TIME, REST_OF_DAY }`, `untilTime` is a local `HH:mm` required only for `UNTIL_TIME`, `reason` optional (≤300, internal).

All three run in **one transaction** with `ILeaseResourceLock.AcquireAsync([], [], [professionalId])` and `db.Database.BeginTransactionAsync`:

1. Resolve the professional from the caller.
2. Compute the local **window** `[from, to]` (see the three cases below), in the operational timezone, for **today** (`DateOnly.FromDateTime(local(now))`).
3. Create one `ProfessionalAvailabilityException.Create(professionalId, today, allDay: false, startTime: from, endTime: to, reason, occurredAt: now, origin: Incident)`. Overlap with a pre‑existing planned exception is allowed for `Origin = Incident` rows (the endpoint's usual `OverlapsAsync` guard is skipped for system‑authored incident exceptions — the evaluator simply subtracts all of them).
4. Find every **affected future reservation**: `db.Reservations` where `ProfessionalId == id`, `Status == Approved`, `Kind != Cancellation`, `EndAt > now`, and `[StartAt, EndAt)` overlaps `[fromUtc, toUtc)`. For `NEXT_APPOINTMENT`, restrict to the single earliest such reservation.
5. For each: `reservation.Cancel(actorUserId: "PROFESSIONAL_INCIDENT", now, ReservationCancellationReason.ProfessionalUnavailable)`; `ReservationCheckInTokenRevocation.RevokeAsync(db, reservation.Id, now, ct)`; issue a `RescheduleToken` (§9/§21); audit `RESERVATION_CANCELLED_PROFESSIONAL_UNAVAILABLE` (target `RESERVATION`) and `RESCHEDULE_LINK_ISSUED` (target `RESCHEDULE_TOKEN`), same `CorrelationId`.
6. Presence side effect (see per‑case):
   - `NEXT_APPOINTMENT` → **presence unchanged, stays `PRESENT`** (§12).
   - `UNTIL_TIME` / `REST_OF_DAY` → `openPresence?.EndForIncident(restOfDay: type == REST_OF_DAY, now)`.
7. Audit the incident itself: `PROFESSIONAL_INCIDENT_REPORTED_NEXT_APPOINTMENT` / `_UNTIL_TIME` / `_REST_OF_DAY` (target `PROFESSIONAL`, target id = professional id). **The internal reason is not in this audit entry and not in logs** — it lives only on the exception row.
8. `SaveChangesAsync` + `CommitAsync`.
9. **After commit**, per cancelled reservation, fire `INotificationService.NotifyCustomerAsync(...)` (§8). Notification failure never rolls anything back.

Response: `{ exceptionId, affectedReservationIds: [...], presence: "PRESENT" | "ABSENT" }`.

Idempotency & concurrency: a reservation already `Cancelled` is skipped (its `Cancel` guard throws `InvalidOperationException`, caught per‑row and treated as "already handled"); the professional lock serialises two concurrent incident submissions; `xmin` on each reservation row means a reservation cannot be cancelled twice.

### Window computation (local, operational timezone, today)

| Option | `from` | `to` | Presence |
|---|---|---|---|
| **Próximo atendimento** (§12) | `local(nextReservation.StartAt)` | `local(nextReservation.EndAt)` | unchanged — **stays PRESENT** |
| **Até determinado horário** (§13) | `local(now)` | `request.untilTime` (must be `> local(now)` and `<= end of civil day`) | **ABSENT immediately** (`EndForIncident(restOfDay:false)`) |
| **Restante do dia** (§14) | `local(now)` | `min(end of civil day, last OperatingHours close today)` | **ABSENT immediately** (`EndForIncident(restOfDay:true)`) |

For **Próximo atendimento**: only the single next affected reservation is cancelled; the exception window is exactly that appointment's interval, so availability outside it is untouched. If there is no future affected reservation, the call is a no‑op success (nothing to cancel, no exception created).

### After "Até determinado horário" ends

- **Availability** returns on its own at `untilTime`: the exception's `EndTime` is fixed, so `GetEffectiveRanges` stops subtracting past that instant — no job.
- **Presence does NOT return.** `EndedAt` is set. To become `PRESENT` again the professional must generate a new QR and pass it at the Totem (§13). There is no automatic re‑open.

---

## 8. Notification to the customer

`INotificationService` is extended (minimal, additive):

```
public sealed record CustomerNotificationEvent(
    Guid CustomerId, string EventType, Guid ReservationId,
    string ProfessionalName, DateTimeOffset OriginalStartAt, string RescheduleUrl);

public interface INotificationService
{
    Task<NotificationResult> NotifyProfessionalAsync(ProfessionalNotificationEvent e, CancellationToken ct); // unchanged
    Task<NotificationResult> NotifyCustomerAsync(CustomerNotificationEvent e, CancellationToken ct);          // new
}
```

- `NotificationMessage` gains an optional `Guid? CustomerId` and `Guid? ProfessionalId` becomes nullable (one recipient set per message). `INotificationProvider.SendAsync` is unchanged in shape.
- `NotificationService.NotifyCustomerAsync` loads the customer, normalises `customer.Phone` through `WhatsAppNormalizer`, builds the body from §19, calls the configured `INotificationProvider`.
- New event type constant `NotificationEventTypes.CustomerReservationCancelledReschedule = "CUSTOMER_RESERVATION_CANCELLED_RESCHEDULE"`.
- **Demo provider** (`DemoNotificationService`) already records every attempt via `DemoNotificationRecorder`; a customer message flows through the same path and is fully testable. `ForceFailure` still exercises the failure branch.
- **Meta** stays fail‑closed (`MetaWhatsAppNotificationService` unchanged) until a future production task adds the HTTP call.
- The main operation does **not** await the notification result for its success. On failure: reservation stays `Cancelled`, the exception stays, presence stays correct, no rollback (§18). The failure is sanitised (`LogFailure` logs provider + event type + code + ids, never the body, never the phone) and surfaced operationally (see §12 — a derived `NotificationDeliveryFailed` alert when a `DemoNotificationAttempt`/failure record is available; **Decision point B:** there is currently no persisted notification‑attempt table, so this alert can only be derived if we add a small `NotificationAttempt` row — otherwise the failure is log‑only. Recommended minimal addition: a `NotificationAttempt { Id, CustomerId?, ProfessionalId?, EventType, Provider, Success, FailureCode, OccurredAt }` append‑only row written by `NotificationService`, no PII, no body).

---

## 9. Affected reservations & the 48h link

### Cancellation (§16)

- `Reservation.Cancel(..., ReservationCancellationReason.ProfessionalUnavailable)` — row **kept**, `Status = Cancelled`, history preserved (the row, `OriginalReservationId` chain, audit). No physical delete.
- Controlled reason lives in the typed `CancellationReason` column, never a free‑text public field. The professional's internal reason is **not** copied here and **not** sent to the customer.

### Reschedule token issuance (§21)

Per cancelled reservation, in the same incident transaction:

- `raw = RandomNumberGenerator.GetBytes(32)`, `hash = SHA256(raw)`.
- `RescheduleToken.Create(reservationId, hash, now, now + 48h)` (or `Rotate` if a row already exists for that reservation).
- `RescheduleUrl` = `"{PublicBaseUrl}/reagendar/{base64url(raw)}"` — token in the path, **no `CustomerId`, phone, email or any PII in the URL** (§21). `PublicBaseUrl` from configuration (`Rescheduling:PublicBaseUrl`).
- Audit `RESCHEDULE_LINK_ISSUED` (target `RESCHEDULE_TOKEN`, target id = token id). Raw/hash never audited.

### Immediate service at the Totem (§9)

Two distinct flows:

- **Future booking** (`GET /api/totem/availability`, `/api/customer/availability`) — a professional is offered **even while `ABSENT`**. Presence is not consulted. Unchanged.
- **Immediate / walk‑in** — new `GET /api/totem/immediate?durationMinutes=…` (`AllowAnonymous`, rate‑limited by `CustomerPublicRateLimiter`). For each active professional, offer them **only if all hold**:
  1. `PresenceEvaluator.IsEffective(...)` == `PRESENT`;
  2. `IAppointmentAvailabilityService.FindAvailableRoomAsync(professionalId, now, now + durationMinutes, requiredRoomId: null, excludedReservationId: null)` returns `IsAvailable` — this single call already enforces personal availability, OperatingHours, an active room, room blocks, and reservation/lease conflicts.

  **No second availability engine.** The endpoint is a presence filter wrapped around the existing service. The actual immediate reservation is then created through the existing `TotemEndpoints.CreateReservation` path (which re‑checks availability inside its own transaction).

---

## 10. Rescheduling — public flow

### 10.1 Endpoints (`ReschedulingEndpoints`, all `AllowAnonymous`, all rate‑limited by a new `RescheduleTokenRateLimiter` — a `CustomerPublicRateLimiter` clone; config `RateLimiting:Reschedule*`)

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/reschedule/resolve` | `{ token }` → validate → minimal context to render the page |
| `GET` | `/api/reschedule/slots` | `?token=…&date=YYYY-MM-DD` → available slots for that date, from the central service |
| `POST` | `/api/reschedule/confirm` | `{ token, startAt, endAt }` → create the replacement, consume the token |

`POST` is used for `resolve` (token in the body, never a query string / never logged in access logs; anti‑enumeration; no `AntiforgeryFilter` because there is no session/cookie — the token *is* the authorization).

### 10.2 `resolve`

- Decode/hash/lookup exactly like `TotemEndpoints.FindCheckIn`.
- Generic `INVALID_RESCHEDULE_LINK` (400) for: not found, `RevokedAt`, `ExpiresAt <= now`, `UsedAt`. **One error for every case (§29).**
- Load the original reservation; it must be `Status == Cancelled && CancellationReason == ProfessionalUnavailable`. Otherwise generic error.
- Response — the minimum to render the page:
  ```
  { professionalId, professionalName, originalStartAt, originalEndAt, durationMinutes, expiresAt }
  ```
  No `CustomerId`, phone, email, no other reservations, no customer profile. `professionalId` in the body (not the URL) is required to call `slots` and is not PII.
- Before expiry the page shows: *"Este link estará disponível por 48 horas após o cancelamento."* (§22).

### 10.3 `slots`

- Re‑validate the token (same generic checks).
- `durationMinutes = (originalEndAt - originalStartAt).TotalMinutes` (rounded to the 15‑min grid the service expects).
- `IAppointmentAvailabilityService.FindSlotsAsync(original.ProfessionalId, date, durationMinutes, ct)` — **the existing central service, unchanged (§23)**. Return `[{ startAt, endAt }]`.
- The page requests today, tomorrow, … (a handful of days) and renders them grouped ("Hoje", "Amanhã", …).

### 10.4 `confirm` (§24)

Ordered, in **one transaction** with `ILeaseResourceLock.AcquireAsync([], activeRoomIds, [original.ProfessionalId])` and `db.Database.BeginTransactionAsync`:

1. Decode/hash/lookup the token; generic error on miss.
2. `ExpiresAt > now`, `RevokedAt is null`, `UsedAt is null` — else generic error.
3. Load original reservation; must be `Cancelled` + `ProfessionalUnavailable`; load `customer` via `original.CustomerId`; `customer.IsActive`.
4. Validate `endAt > startAt`, same local civil day, `durationMinutes` matches the original within tolerance.
5. `available = IAppointmentAvailabilityService.FindAvailableRoomAsync(original.ProfessionalId, startAt, endAt, requiredRoomId: original.RoomId, excludedReservationId: null)`; if not available, retry once with `requiredRoomId: null`. `!IsAvailable` → `AppointmentAvailabilityResults.Conflict(...)` (the existing 409 shape). **Decision point C:** keep the original room when free, else any active room — proposed here; the alternative (always any room) is simpler but can move the customer between rooms silently.
6. `replacement = Reservation.CreateApprovedReplacementForIncident(original, startAt, endAt, actorUserId: "RESCHEDULE_LINK", now)` with `RoomId = available.RoomId`.
7. `token.MarkUsed(now)`.
8. `db.Reservations.Add(replacement)`; audit `RESERVATION_RESCHEDULED` (target `RESERVATION`, id = replacement id) and `RESCHEDULE_LINK_CONSUMED` (target `RESCHEDULE_TOKEN`).
9. `SaveChangesAsync` + `CommitAsync`.

Concurrency (§30):

- **Two concurrent uses of the same link** → both read `UsedAt is null`, both try to `MarkUsed` + insert; `xmin` on the `RescheduleToken` row makes the second `SaveChangesAsync` throw `DbUpdateConcurrencyException` → rolled back → generic `INVALID_RESCHEDULE_LINK`. Exactly one replacement is created.
- **The chosen slot taken by someone else in between** → `FindAvailableRoomAsync` + `IReservationConflictDetector` inside the locked transaction return a conflict → 409, token **not** consumed, the customer can pick again.
- **A later use after a successful one** → `UsedAt is not null` → generic error.

Response: `{ reservationId, startAt, endAt, professionalName, roomName }`.

### 10.5 Expiry page (§22)

When `resolve` returns `INVALID_RESCHEDULE_LINK` and the client cannot tell why, the page shows the fixed copy:

> *"Este link de reagendamento expirou. Entre em contato com a recepção para realizar um novo agendamento."*

(The backend deliberately does not distinguish expired / used / revoked / unknown — anti‑enumeration.)

### 10.6 Customer with an account (§25)

No login. The token grants **only** the authorization to reschedule that one reservation. It never creates a `Customer` session, never exposes history, profile, or other reservations. A logged‑in customer who happens to open the link uses the exact same anonymous endpoints.

---

## 11. Message (§19, §20)

Body built by `NotificationService.NotifyCustomerAsync`, conceptually:

> Seu atendimento precisou ser cancelado por um imprevisto do profissional.
> Você pode escolher um novo horário pelo link abaixo.
> Este link ficará disponível por 48 horas.
> {RescheduleUrl}

- **No internal reason.** No professional phone/email. Only what identifies the appointment (professional first name + original date/time) plus the link.
- One action only: open the link → **"Reagendar atendimento"**. No Yes/No, no free‑text reply expected (§20).

---

## 12. Reception alerts & status

### Derived alerts (`IOperationalAlertReader`, extend `OperationalAlertType`, no new state)

| New `OperationalAlertType` | Severity | Condition (derived at read time) |
|---|---|---|
| `CustomerWaitingProfessionalAbsent` | `Critical` | an open `Visit` (`Waiting`) whose `ProfessionalId` is currently `ABSENT` by `PresenceEvaluator` |
| `OpenVisitAffectedByIncident` | `Critical` | an open `Visit` (`Waiting` or `InService`) whose professional has an `Origin = Incident` exception for today covering `now`, **or** whose reservation was `Cancelled` with `ProfessionalUnavailable` |
| `NotificationDeliveryFailed` | `Warning` | only if the `NotificationAttempt` row from Decision point B exists: a failed customer notification in the recent window |

`PostgreSqlOperationalAlertReader` gains three `ReadXxxAsync` helpers following the existing structure; the endpoint filter and ordering are unchanged.

### Check‑in is never blocked (§8)

Nothing in the check‑in path (`TotemEndpoints.ConfirmCheckIn`, `ReceptionEndpoints.ManualCheckIn`) consults presence. A customer with a valid reservation checks in, `Visit = Waiting`, **even if the professional is `ABSENT`**. The `CustomerWaitingProfessionalAbsent` alert is raised. The visit is not cancelled, the check‑in is not rejected.

### Visits are never touched automatically (§17)

The incident flow **does not** load, cancel, or transition any `Visit`. `Waiting` stays `Waiting`; `InService` stays `InService`. Reception gets `OpenVisitAffectedByIncident` and a manager decides manually (cancel via the existing `POST /api/reception/visits/{id}/cancel`, talk to the customer, reorganise). There is no new automatic `Visit` transition anywhere in this design.

### Professional projection (§27)

`ReceptionEndpoints.Professionals` / `Overview` gain a **presence dimension**, derived, not a new enum on the entity:

- `Presence: "PRESENT" | "ABSENT"` from `PresenceEvaluator`.
- `AbsentUntil: DateTimeOffset?` — when `ABSENT` and an `Origin = Incident` exception for today has `EndTime > now`, the UTC instant of that `EndTime`. Rendered by the UI as "AUSENTE ATÉ 16:00".

The existing availability‑based `OperationalStatus` string ("AVAILABLE" / "WAITING_VISITOR" / "IN_SERVICE" / "UNAVAILABLE") is unchanged; `Presence` is an orthogonal field on the same response record.

---

## 13. APIs (summary)

### Professional (policy `Professional`, `AntiforgeryFilter` on writes)

| Method | Route | Body / result |
|---|---|---|
| `POST` | `/api/professional/presence/qr` | → `{ token, expiresAt }` (2‑minute QR) |
| `GET` | `/api/professional/presence` | → `{ status: "PRESENT"|"ABSENT", since?, absentUntil? }` (own, derived) |
| `POST` | `/api/professional/incidents` | `{ type, untilTime?, reason? }` → `{ exceptionId, affectedReservationIds, presence }` |

### Totem (public, `AllowAnonymous`, rate‑limited)

| Method | Route | Notes |
|---|---|---|
| `POST` | `/api/totem/presence/confirm` | `{ token }` → `{ status: "PRESENT" }` or generic `INVALID_PRESENCE`; **no session created** |
| `GET` | `/api/totem/immediate` | `?durationMinutes` → presence‑gated, centrally‑available professionals for a right‑now slot |

(existing `/api/totem/professionals`, `/api/totem/availability`, `/api/totem/customers/resolve`, `/api/totem/reservations`, `/api/totem/check-in/*` unchanged)

### Reschedule (public, `AllowAnonymous`, rate‑limited, token‑authorized)

| Method | Route | Notes |
|---|---|---|
| `POST` | `/api/reschedule/resolve` | `{ token }` → `{ professionalId, professionalName, originalStartAt, originalEndAt, durationMinutes, expiresAt }` |
| `GET` | `/api/reschedule/slots` | `?token&date` → `[{ startAt, endAt }]` from `FindSlotsAsync` |
| `POST` | `/api/reschedule/confirm` | `{ token, startAt, endAt }` → `{ reservationId, startAt, endAt, professionalName, roomName }` |

### Reception (policy `Operations`, `AntiforgeryFilter`)

| Method | Route | Notes |
|---|---|---|
| `POST` | `/api/reception/presence` | `{ professionalId, state: "PRESENT"|"ABSENT" }` — manual fallback, always audited |

(`/api/reception/overview` and `/api/reception/professionals` responses gain `Presence` / `AbsentUntil`; `/api/operational-alerts` gains the three derived types)

---

## 14. Authorization (§29)

| Actor | Can |
|---|---|
| Professional (`Professional` policy) | issue own presence QR; read own presence; report own incident. Cannot touch another professional's presence or incident (professional id is resolved from the authenticated user, never taken from the request body). |
| Operations (`Operations` policy) | read presence of any professional; set presence `PRESENT`/`ABSENT` manually via `/api/reception/presence`. Not public. |
| Customer / Totem | cannot alter presence arbitrarily. The public presence endpoint accepts only a valid, unused, unexpired token and only *records* presence. |
| Public reschedule endpoints | token‑authorized only; the token authorizes exactly one reservation's reschedule and nothing else; no session. |

Public endpoints (`/api/totem/presence/confirm`, `/api/reschedule/*`) each have their own partitioned rate limiter (per IP + per hashed token), fixed window, `429` with `TOO_MANY_REQUESTS`, following `CustomerPublicRateLimiter`. Anti‑enumeration: a single generic error per token failure class.

---

## 15. Concurrency (§30) — how each guarantee is met

| Requirement | Mechanism |
|---|---|
| Two scans of the same QR ⇏ two presences | `ProfessionalPresenceToken.UsedAt` single‑use **and** partial unique index `UX_ProfessionalPresence_Open` on `(ProfessionalId) WHERE EndedAt IS NULL` **and** professional `ILeaseResourceLock`; the loser catches `DbUpdateException` and returns idempotent success. |
| Two concurrent manual marks ⇏ incoherent state | professional `ILeaseResourceLock` serialises; `xmin` on `ProfessionalPresence` → second write `DbUpdateConcurrencyException` → `RESOURCE_MODIFIED`. |
| Incident ⇏ same reservation cancelled twice | per‑row `xmin` on `Reservation`; `Cancel()` guard throws on a non‑Approved reservation (caught per row); professional lock serialises two incident submissions. |
| Same reschedule link ⇏ two replacement reservations | `RescheduleToken.UsedAt` + `xmin` on the token row; the second `SaveChangesAsync` fails and rolls back; the room lock + `IReservationConflictDetector` prevent double occupancy even if both passed. |
| Concurrent reservation ⇏ occupy an already‑used slot | `ILeaseResourceLock` on room ids + `IAppointmentAvailabilityService.FindAvailableRoomAsync` (which calls `IReservationConflictDetector`) inside the transaction — the existing booking guarantee, reused. |

All new writes run inside `db.Database.BeginTransactionAsync` and acquire `ILeaseResourceLock` before the availability check (the pattern `PostgreSqlAppointmentAvailabilityService.EnsureTransaction()` enforces).

---

## 16. Audit (§28)

New `AuditActions` constants (string values follow the existing `SCREAMING_SNAKE` convention):

- `PROFESSIONAL_PRESENCE_QR_ISSUED`
- `PROFESSIONAL_PRESENCE_STARTED`
- `PROFESSIONAL_PRESENCE_STARTED_BY_OPERATIONS`
- `PROFESSIONAL_PRESENCE_ENDED_BY_OPERATIONS`
- `PROFESSIONAL_INCIDENT_REPORTED_NEXT_APPOINTMENT`
- `PROFESSIONAL_INCIDENT_REPORTED_UNTIL_TIME`
- `PROFESSIONAL_INCIDENT_REPORTED_REST_OF_DAY`
- `RESERVATION_CANCELLED_PROFESSIONAL_UNAVAILABLE`
- `RESCHEDULE_LINK_ISSUED`
- `RESCHEDULE_LINK_CONSUMED`
- (`RESERVATION_RESCHEDULED` reused for the replacement)

New `AuditTargetTypes`: `PROFESSIONAL_PRESENCE`, `RESCHEDULE_TOKEN`.

Audit entries are written inline in the endpoint transactions (the `TotemEndpoints` / `ReceptionEndpoints` style) with the request `TraceIdentifier` as `CorrelationId`, `ActorUserId` from the claim (or `null` for public token flows), `Result = "SUCCEEDED"`.

**Audited:** QR issued; presence started (QR); presence started/ended by Operations; incident reported (with type, not reason); each reservation cancelled by an incident; reschedule token issued; reschedule token consumed.
**Not audited / not logged:** raw token; token hash; phone; password; cookie; the professional's internal reason (only ever on `ProfessionalAvailabilityException.Reason`); raw WhatsApp body. A repeatedly‑failing expired‑token probe does **not** create a business audit row per attempt (rate limiting + generic error absorb it).

---

## 17. Migrations previewed (§35)

One PostgreSQL migration (name e.g. `ProfessionalPresenceAndRescheduling`), all additive:

1. **`ProfessionalPresence`** table — `Id uuid pk`, `ProfessionalId uuid` FK → `Professionals(Id)` `NoAction`, `StartedAt timestamptz`, `EndedAt timestamptz null`, `EndReason smallint null`, `Source smallint`, `CreatedByUserId varchar(450) null`, `xmin`. Index `IX_ProfessionalPresence_ProfessionalId_StartedAt`. **Partial unique index** `UX_ProfessionalPresence_Open ON (ProfessionalId) WHERE EndedAt IS NULL`.
2. **`ProfessionalPresenceTokens`** table — mirrors `CheckInTokens`: `Id`, `ProfessionalId uuid` FK, `TokenHash bytea` (unique `UX_ProfessionalPresenceTokens_TokenHash`), `IssuedAt`, `ExpiresAt`, `UsedAt null`, `RevokedAt null`, `xmin`. Unique `UX_ProfessionalPresenceTokens_ProfessionalId`.
3. **`RescheduleTokens`** table — mirrors `CheckInTokens`: `Id`, `ReservationId uuid` FK → `Reservations(Id)` `NoAction` (unique `UX_RescheduleTokens_ReservationId`), `TokenHash bytea` (unique `UX_RescheduleTokens_TokenHash`), `IssuedAt`, `ExpiresAt`, `UsedAt null`, `RevokedAt null`, `xmin`.
4. **`Reservations.CancellationReason`** — `smallint NOT NULL DEFAULT 0`.
5. **`ProfessionalAvailabilityExceptions.Origin`** — `smallint NOT NULL DEFAULT 0`.
6. *(only if Decision point B is accepted)* **`NotificationAttempts`** table — append‑only: `Id`, `CustomerId uuid null`, `ProfessionalId uuid null`, `EventType varchar`, `Provider varchar`, `Success bool`, `FailureCode varchar null`, `OccurredAt timestamptz`. No PII, no body.

No table is created "just for a screen". No slots table. No jobs table.

DbContext gains `DbSet<ProfessionalPresence>`, `DbSet<ProfessionalPresenceToken>`, `DbSet<RescheduleToken>` (and `DbSet<NotificationAttempt>` if B). New `IEntityTypeConfiguration<>` classes follow `CheckInTokenConfiguration`.

---

## 18. Frontend contracts (future — not implemented here, §32–34)

### Totem home (touch)

```
[ Fazer check-in ]
[ Agendar atendimento ]
[ Sou profissional ]
```

`Sou profissional` → open camera → read QR → `POST /api/totem/presence/confirm { token }` → short success message ("Presença registrada") → back to home. **No password. Does not open the professional panel.** The full professional panel stays at `/profissional` on the professional's own phone/computer.

### Professional area (`/profissional`, existing shell)

Two new actions (few steps):

- **Registrar chegada** → `POST /api/professional/presence/qr` → render the returned `token` as a QR for ~2 minutes, show a countdown, allow re‑generate.
- **Informar imprevisto** → one screen: three radio options (`Próximo atendimento` / `Até determinado horário` [time picker] / `Restante do dia`), an optional "Motivo (interno, opcional)" text field, `Confirmar` → `POST /api/professional/incidents`. On success show what happened ("2 agendamentos cancelados, clientes avisados").
- Optional presence indicator from `GET /api/professional/presence`.

### Reschedule page (public, new route `/reagendar/:token`, anonymous)

- On load: `POST /api/reschedule/resolve`.
- If invalid/expired → the §22 expiry copy, nothing else.
- If valid → "Escolha um novo horário", groups **Hoje / Amanhã / …** each populated from `GET /api/reschedule/slots?token&date`, `[ Confirmar novo horário ]` → `POST /api/reschedule/confirm`.
- **Never asks for** name, phone, email, password, or login. Same professional as the original reservation.

### Reception (existing `/recepcao`)

Adds: professional `PRESENT` / `AUSENTE` (+ "AUSENTE ATÉ X"); "clientes aguardando com profissional ausente"; "imprevistos ativos"; the manual `PRESENTE` / `AUSENTE` fallback control (calls `POST /api/reception/presence`). Simple interface.

---

## 19. Tests required (§37) — for the future implementation

**Presence**

- QR issue returns a token; `ExpiresAt - IssuedAt == 120s` (config default).
- Token is single‑use: second `confirm` with the same token → generic `INVALID_PRESENCE`.
- QR payload contains no `ProfessionalId` / name / phone / email (assert the decoded bytes are opaque and the response has only `token` + `expiresAt`).
- `confirm` opens a `ProfessionalPresence`; `GET /api/professional/presence` → `PRESENT`.
- Second `confirm` while already effectively present → idempotent success, no second row (`UX_ProfessionalPresence_Open` holds), token consumed.
- `PresenceEvaluator.IsEffective` → `ABSENT` once `local(now) > last OperatingHours close today`, with **no job running** (advance a fake `TimeProvider`, no scheduler registered).
- `PresenceEvaluator` fail‑closed when OperatingHours not configured / day closed.
- Operations `POST /api/reception/presence` sets `PRESENT` and `ABSENT`; both audited.
- Professional cannot affect another professional's presence (id resolved from claim; a forged body id is ignored).
- Concurrent `confirm` of the same QR from two callers → exactly one presence, both requests return success, one audit `PROFESSIONAL_PRESENCE_STARTED`.

**Incident**

- `NEXT_APPOINTMENT`: only the next affected reservation is cancelled; an exception equal to that reservation's interval is created (`Origin = Incident`); **presence stays `PRESENT`**.
- `UNTIL_TIME`: exception `[now, untilTime]`; every reservation overlapping it is cancelled; **presence `ABSENT` immediately**; after `untilTime` availability returns (evaluator), presence does **not** — a fresh QR is required to become `PRESENT` again.
- `REST_OF_DAY`: exception `[now, last close]`; affected reservations cancelled; **presence `ABSENT` immediately**; no new offers that day (immediate endpoint filters the professional out).
- Internal reason is stored on the exception, absent from the incident audit entry and from logs.
- Reservation strictly outside the window is **not** cancelled.
- Idempotent: replaying the incident request does not double‑cancel (already‑Cancelled rows skipped).
- Concurrent incident submissions serialised by the professional lock; each reservation cancelled at most once.

**Reservation**

- Affected future `Approved` reservations → `Cancelled`, `CancellationReason = ProfessionalUnavailable`, row retained, `OriginalReservationId`/audit chain intact.
- A `Cancelled` or past reservation is untouched.
- The check‑in token of a cancelled reservation is revoked.

**Visit**

- `Waiting` visit of the affected professional stays `Waiting`; `InService` stays `InService` — **no automatic transition**.
- `OpenVisitAffectedByIncident` alert appears for Reception.

**Customer / Totem**

- Customer check‑in still succeeds while the professional is `ABSENT`; `Visit = Waiting`; `CustomerWaitingProfessionalAbsent` alert raised; check‑in not rejected, visit not cancelled.
- Future booking offers a professional who is `ABSENT`.
- `/api/totem/immediate` offers a professional **only** when `PRESENT` **and** `FindAvailableRoomAsync` is available; drops them when absent or centrally unavailable.

**Rescheduling**

- Token TTL is 48h; `resolve` after 48h → generic error; page shows the expiry copy.
- Single use: a successful `confirm` then a second `confirm` → generic error; exactly one replacement created.
- No login required; works for a customer with an account and for a Totem‑created customer without one; no session created; `resolve` exposes no history/profile/other reservations/PII, and the URL carries no `CustomerId`/phone/email.
- Slots come from the central `FindSlotsAsync` (assert it is the same service, e.g. via a spy / identical results to `/api/customer/availability`).
- Two concurrent `confirm` on the same link → one succeeds, one gets a generic error; one replacement reservation.
- Concurrent booking of the chosen slot elsewhere → `confirm` returns 409, token **not** consumed, retry possible.
- The replacement preserves history: `Kind = Reschedule`, `OriginalReservationId == cancelledOriginal.Id`, original stays `Cancelled`.

**Notification**

- Provider failure (Demo `ForceFailure`) does **not** roll back: reservation stays `Cancelled`, exception stays, presence stays correct, reschedule token stays valid.
- The message body contains neither the internal reason nor the professional's phone/email; it contains the reschedule URL.
- Demo provider records the customer attempt (`DemoNotificationRecorder`), exercising the full path end‑to‑end.

---

## 20. Compatibility

- All schema changes additive: three new tables, two new `NOT NULL DEFAULT 0` `smallint` columns, no drop/rename/renumber. Existing rows get `CancellationReason = 0` (`None`) and `Origin = 0` (`Planned`).
- `Reservation.Cancel` gains an optional parameter — every existing call site compiles unchanged and behaves identically.
- `ProfessionalAvailabilityException.Create` gains a trailing defaulted parameter — same.
- `INotificationService` gains a method; existing `NotifyProfessionalAsync` untouched; `NotificationMessage` gains a nullable field.
- `IAppointmentAvailabilityService`, `ProfessionalAvailabilityEvaluator`, `OperatingHoursEvaluator`, `IReservationConflictDetector`, `ILeaseResourceLock`: **used as‑is, not modified**.
- `IOperationalAlertReader` / `OperationalAlertType` gain enum members and reader branches; the endpoint contract and filter are unchanged.
- No change to auth policies; two new singleton rate limiters registered next to the existing ones; new endpoint groups mapped before the `/api/{**path}` catch‑all with `.AllowAnonymous()` where public.
- Frontend: additive routes and actions only; `/totem/check-in` and `/profissional/*` unaffected.

---

## 21. Out of scope (§38)

Frontend implementation; a working camera; real Meta WhatsApp; Intelbras; Google Calendar; Outlook; SMS; background jobs; Redis; queues; Supabase; production; deploy. This document changes no database and ships no code.

---

## 22. Decision points needing a call before implementation

| # | Decision | Recommendation |
|---|---|---|
| **A** | Separate `ProfessionalIncident` entity? | **No** — derive everything (see §3.5). Add later only if incidents must be first‑class objects with a count. |
| **B** | Persist notification attempts (to power a `NotificationDeliveryFailed` alert and "falha de notificação visível operacionalmente" per §18/§26)? | **Yes, minimal** — an append‑only `NotificationAttempt` row with no PII/body. Without it, notification failure is log‑only and Reception cannot see it. |
| **C** | On reschedule, keep the original room or pick any available? | Try the original room first, fall back to any active room. |
| **D** | `confirm` on the same QR while already effectively present | Idempotent success + consume the token (proposed). Alternative: reject with a specific "already present". |
| **E** | Presence effective before the establishment's opening time (professional scans early) | Effective from the scan (proposed — they are physically there). Alternative: clamp start to `OpensAt`. |
| **F** | `PROFESSIONAL_INCIDENT_*` audit through the restrictive `ProfessionalAvailabilityAudit.CreateSucceeded` helper or inline like `TotemEndpoints` | Inline (consistent with the newer token flows). |
| **G** | `Presence:QrTokenTtlSeconds` default | 120s (§4 "aproximadamente 2 minutos"). |

---

## 23. Self‑review (§40)

- **TODO/TBD scan:** none in the design. Open items are enumerated as explicit Decision points A–G, not left implicit.
- **Contradictions:** none found. Presence vs availability kept orthogonal throughout; the only cross‑reference is the immediate‑service gate (§9) and the Reception projection (§12).
- **Visit never altered automatically:** confirmed in §12 and §7 step‑by‑step — the incident transaction reads/writes only `ProfessionalAvailabilityException`, `Reservation`, `ProfessionalPresence`, `RescheduleToken`, `AuditEntry`. No `Visit` DbSet is touched. Test coverage in §19 (Visit block).
- **Incident cancels the affected future reservation(s):** §7 step 4–5, §9. Past / non‑Approved / out‑of‑window reservations excluded.
- **Three options present and distinct:** §7 table — `NEXT_APPOINTMENT`, `UNTIL_TIME`, `REST_OF_DAY`; window and reservation‑selection rules differ per row.
- **Presence rule per option:** `NEXT_APPOINTMENT` → stays `PRESENT`; `UNTIL_TIME` / `REST_OF_DAY` → `ABSENT` immediately (§7 table, §12–14 of the brief).
- **New QR required to return after `UNTIL_TIME`:** §7 "After 'Até determinado horário' ends" and §19 test.
- **48 hours:** `RescheduleToken.ExpiresAt = IssuedAt + Rescheduling:LinkTtlHours` (default 48) — §3.3, §9, §10.2, §10.5, §19.
- **Single use:** `UsedAt` + `xmin` on both token aggregates — §3.2, §3.3, §10.4, §15, §19.
- **No PII in tokens:** 32 random bytes, hash‑only persistence, opaque payload, token in path not query for the link, `resolve` response limited to non‑PII fields — §3, §10.2, §14, §19.
- **Immediate service requires presence:** §9 (`/api/totem/immediate` = presence filter + `FindAvailableRoomAsync`).
- **Future booking does not require presence:** §9 (existing `/api/totem/availability` and `/api/customer/availability` untouched).
- **No job introduced:** §6 — read‑time computation; opportunistic materialisation only when a write already holds the row; §19 asserts no scheduler.
- **UX stays simple:** Totem home = 3 buttons; professional actions = 2; incident = 3 options + optional reason + confirm; reschedule page = pick a time + confirm, no login, no re‑entered data.

---

**SPEC DE PRESENÇA / IMPREVISTOS / REAGENDAMENTO PRONTA PARA REVISÃO.**
