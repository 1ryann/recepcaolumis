# Totem: 3-screen split + real professional carousel — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the Totem into `/totem` (decision), `/totem/profissionais` (real swipe carousel), `/totem/check-in` (existing QR/code flow), backed by a minimal public professionals endpoint, and carry the chosen `professionalId` into the CUSTOMER booking flow through a strictly‑validated `returnUrl`.

**Architecture:** ASP.NET Core minimal‑API host (`recepcaototem`) over a clean‑architecture core (`src/GestaoPredio.*`), PostgreSQL/EF Core. Frontend is React 18 + TS + Vite + Vitest, dark "Lumis kiosk" identity already in `styles.css`. No new runtime dependencies (CSP blocks external scripts): the carousel and all "Magic UI" effects are local components. TDD throughout; one local commit per task.

**Spec:** `docs/superpowers/specs/2026-09-09-lumis-totem-entry-and-professional-carousel-design.md` (commit `d90cd5a`). The plan argues from the spec; read both.

## Global Constraints

- Branch `codex/reception-backend`, worktree `.worktrees/reception-backend`. **No push, no `main`, no Railway, no manual DB.**
- Commit trailer on every commit: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- **Migration:** Task 13 *generates* `CheckInManualCode` via `dotnet ef migrations add` (source-controlled) but **does not apply it** to staging/production — applying it is a separate, separately-approved step. The integration test DB recreates the schema from the model/migrations, so tests still cover it.
- **Do not edit `recepcaototem/Program.cs`.** The CSP `font-src`/`worker-src` fix is a separate prerequisite (spec §20). The camera/QR scan path is not "done" until it ships elsewhere.
- **Credential invariant:** the QR strong token and the 6-digit manual code are two representations of the **same** `CheckInToken` row. Consuming or revoking one invalidates the other; `Rotate` replaces both. `resolve`/`confirm` keep **one** eligibility rule for both.
- **Manual-code hashing:** persisted hash is **HMAC-SHA-256 with a dedicated server secret** (`CheckIn:ManualCodeHmacKey`, Options pattern) — never plain SHA-256, never the DB password / a JWT secret / a DataProtection key / a hardcoded value. `HmacManualCheckInCodeHasher` **fails closed in `Production`** when the key is missing; an explicit deterministic dev fallback applies only outside `Production`; test factories inject a fixed test key. No key rotation this MVP. The 6-digit plaintext, the `ManualCodeHash`, and the HMAC key are **never** persisted in clear / logged / audited. `ManualCheckInCode` is a **pure** value object (no `Hash()`, no `IConfiguration`).
- **`ManualCodeHash` is transient:** set on issue, **nulled by `MarkUsed` and `Revoke`** (the 6-digit combination returns to the pool), replaced by `Rotate`, and lazily reclaimed (`ClearManualCode()`) when a new issue's candidate collides with an already-stale row — all within the issue transaction. No cleanup job. `TokenHash` is never nulled.
- **Do not touch** `/api/totem/immediate`, `TotemProfessionalResponse`, `src/pages/Reception.tsx`, `src/dev/DevelopmentAppStore.tsx`, `AppStore`, `atrium_*`. No mock data, no hardcoded professionals/photos.
- Timezone is `America/Porto_Velho`. Reuse `src/features/totem/KioskClock.tsx` — **no new `setInterval`/timer**.
- Preserve every existing test. Preserve the `ProtectedRoute` non‑remount latch (`validatedOnce`) from commit `d03f312`.
- Frontend gate commands run from `recepcaototem/ClientApp`: `npx vitest run`, `npx tsc -b`, `npx vite build`, `npm run --silent verify:production-bundle`, `git diff --check`. Backend: `dotnet test` from repo root of the worktree.
- New carousel CSS classes are `totem-carousel-*` / `totem-entry-*` / `totem-status-*` / `totem-magic-*` — never `lumis-gallery*` (that is the dev mock).
- Public DTO minimization: `TotemProfessionalCard` has **exactly** `Id, Name, Profession, PhotoUrl, Status`. Never `Description`, `WhatsApp`, `ApplicationUserId`, Identity ids, email, phone, `PhotoFileId`.

---

## File Structure

**Backend (create)**
- `recepcaototem/Features/Totem/TotemProfessionalStatus.cs` — pure status mapper.
- `recepcaototem/Features/Professionals/ProfessionalPhotoStreaming.cs` — shared photo‑streaming helper.
- `src/GestaoPredio.Domain/Customers/ManualCheckInCode.cs` — **pure** 6-digit value object (no hashing).
- `src/GestaoPredio.Application/Customers/IManualCheckInCodeHasher.cs` + `ManualCheckInCodeHashingOptions.cs`.
- `src/GestaoPredio.Infrastructure/Customers/HmacManualCheckInCodeHasher.cs` — HMAC-SHA-256; fail-closed in `Production`.
- `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/<ts>_CheckInManualCode.cs` — via `dotnet ef migrations add` (Task 13; not applied).
- `tests/GestaoPredio.UnitTests/{TotemProfessionalStatusTests,ManualCheckInCodeTests,HmacManualCheckInCodeHasherTests}.cs`
- `tests/GestaoPredio.IntegrationTests/{TotemProfessionalsCarouselTests,CheckInManualCodeTests}.cs`

**Backend (modify)**
- `recepcaototem/Features/Totem/TotemEndpoints.cs` — new `record TotemProfessionalCard`; replace `Professionals` handler body; add `ProfessionalPhoto` handler + route; `FindCheckIn`/`ResolveCheckIn`/`ConfirmCheckIn` take `IManualCheckInCodeHasher` and dispatch by string shape (6 digits → `ManualCodeHash == hasher.Hash(code)`).
- `recepcaototem/Features/Professionals/ProfessionalPhotoEndpoints.cs` — `Get` delegates to the shared helper (behaviour unchanged: `private, no-store`, **503 on real storage I/O failure**, 404 only for the guards).
- `src/GestaoPredio.Domain/Customers/CheckInToken.cs` — `ManualCodeHash: byte[]?`; `Create`/`Rotate` take both hashes; **`MarkUsed`/`Revoke` null `ManualCodeHash`**; new `ClearManualCode()`.
- `src/GestaoPredio.Infrastructure/Persistence/Configurations/CheckInTokenConfiguration.cs` — map `ManualCodeHash` + partial unique index `UX_CheckInTokens_ManualCodeHash`.
- composition root (DI) — register `IManualCheckInCodeHasher` → `HmacManualCheckInCodeHasher`, `Configure<ManualCheckInCodeHashingOptions>`, `IManualCodeSource` → `DefaultManualCodeSource`. **Services only — not the CSP line.**
- `recepcaototem/Features/Customers/CustomerSchedulingEndpoints.cs` — `IssueToken`: `IManualCodeSource` + `IManualCheckInCodeHasher` + bounded collision loop **with lazy reclaim**, in the existing transaction; response `{ token, manualCode, expiresAt }`.
- `tests/GestaoPredio.UnitTests/CheckInTokenTests.cs` — new `Create`/`Rotate` arity + transient-lifecycle assertions.

**Frontend (create)** — under `recepcaototem/ClientApp/src/`
- `auth/returnUrl.ts` + `auth/returnUrl.test.ts` — `safeCustomerReturnUrl`.
- `features/totem/professionalInitials.ts` + `.test.ts`.
- `features/totem/sixDigitCode.ts` + `.test.ts` — `onlyDigits6` / `isComplete6`.
- `features/totem/magic/LightRays.tsx`, `BlurFade.tsx`, `MagicCard.tsx`, `BorderBeam.tsx`, `ProgressiveBlur.tsx`, `RippleButton.tsx`
- `features/totem/magic/magic.test.tsx`
- `features/totem/TotemProfessionalCarousel.tsx` + `.test.tsx`
- `pages/TotemEntry.tsx` + `.test.tsx`
- `pages/TotemProfessionals.tsx` + `.test.tsx`

**Frontend (modify)**
- `api/modules.ts` — `TotemProfessionalCardDto` + `totemApi.professionals()`; `issueCheckInToken` return type `{ token; manualCode; expiresAt }`.
- `App.tsx`, `dev/DevelopmentApp.tsx` — three Totem routes.
- `frontend-portals.test.ts` — updated Totem route assertions.
- `pages/TotemCheckIn.tsx` + `.test.tsx` — `← Voltar` + auto‑return to `/totem` + trimmed aside copy + `LightRays` + **6-digit manual input**.
- `pages/customer/CustomerReservationDetail.tsx` + `.test.tsx` — show the 6-digit code next to the QR (no storage).
- `components/ProtectedRoute.tsx` + `.test.tsx` — `returnUrl` on the customer redirect.
- `pages/Login.tsx` + `.test.tsx` — honour + propagate `returnUrl` (customer only).
- `pages/customer/CustomerRegister.tsx` + `.test.tsx` — propagate `returnUrl`.
- `pages/customer/CustomerBooking.tsx` + `.test.tsx` — preselect from `?professionalId`.
- `styles.css` — append `.totem-entry-*`, `.totem-carousel-*`, `.totem-status-*`, `.totem-magic-*`, `.customer-checkin-code` blocks (no existing rule changed).

---

## Interfaces between tasks

- **Task 1 → 2:** `TotemProfessionalStatus.Resolve(bool inService, bool effectivePresence) : string` returning `"IN_SERVICE" | "AVAILABLE" | "UNAVAILABLE"`.
- **Task 2 → 4:** `GET /api/totem/professionals` (anonymous) → `200` JSON array of `{ id: string, name: string, profession: string, photoUrl: string | null, status: "AVAILABLE"|"IN_SERVICE"|"UNAVAILABLE" }`, ordered by normalized name; `429` on rate limit.
- **Task 3 → 8/9:** `GET /api/totem/professionals/{id:guid}/photo` (anonymous) → `200` image bytes (`Cache-Control: public, max-age=300`) for an **active** professional with a `PROFESSIONAL_PHOTO` file; `404` for unknown/inactive/no‑photo/wrong‑purpose; `503` on storage I/O failure. Helper: `ProfessionalPhotoStreaming.StreamAsync(Professional professional, ApplicationDbContext db, IPrivateFileStorage storage, ILoggerFactory loggerFactory, HttpContext context, string cacheControl, CancellationToken ct) : Task<IResult>`.
- **Task 4 → 8/9:** `TotemProfessionalCardDto = { id: string; name: string; profession: string; photoUrl: string | null; status: 'AVAILABLE' | 'IN_SERVICE' | 'UNAVAILABLE' }`; `totemApi.professionals(signal?: AbortSignal): Promise<TotemProfessionalCardDto[]>`; `professionalInitials(name: string): string` (1–2 uppercase letters).
- **Task 5 → 11:** `safeCustomerReturnUrl(raw: string | null | undefined): string | null` — returns a normalized `/cliente…` path or `null`.
- **Task 6 → 7/8/9/10:** local components with these props:
  - `<LightRays className?: string />` — decorative, `aria-hidden`, `pointer-events:none`.
  - `<BlurFade delay?: number; children />` — one‑shot intro on mount.
  - `<MagicCard as?: 'button'; onClick?; className?; children />` — subtle pointer‑follow highlight on fine pointers only.
  - `<BorderBeam active: boolean />` — animated border, only when `active`; static under reduced motion.
  - `<ProgressiveBlur side: 'left' | 'right' />` — edge mask, `pointer-events:none`.
  - `<RippleButton onClick; disabled?; className?; children; aria-label? />` — `<button>` with a short ripple.
- **Task 7 → routes:** `<TotemEntry />` at `/totem`.
- **Task 8 → 9:** `<TotemProfessionalCarousel professionals: TotemProfessionalCardDto[]; onActiveChange: (p: TotemProfessionalCardDto) => void />`.
- **Task 9 → routes:** `<TotemProfessionals />` at `/totem/profissionais`.
- **Task 12 → 13/14/15/16:** `readonly struct ManualCheckInCode` — `static Generate() : ManualCheckInCode`, `static TryParse(string?, out ManualCheckInCode) : bool`, `Value : string` (6 digits), `ToString()`. **No `Hash()` — no hashing, no `IConfiguration` in this type.**
- **Task 13 → 14/15/16:** keyed hasher + persistence:
  - `IManualCheckInCodeHasher.Hash(ManualCheckInCode code) : byte[]` (Application) — HMAC-SHA-256, 32 bytes, deterministic.
  - `ManualCheckInCodeHashingOptions { const SectionName = "CheckIn"; string ManualCodeHmacKey }` (Application).
  - `HmacManualCheckInCodeHasher(IOptions<ManualCheckInCodeHashingOptions>, IHostEnvironment)` (Infrastructure) — fail-closed in `Production` when key missing; explicit deterministic dev fallback otherwise.
  - `CheckInToken.ManualCodeHash : byte[]?`; `CheckInToken.Create(Guid, byte[] tokenHash, byte[] manualCodeHash, DateTimeOffset, DateTimeOffset)`; `CheckInToken.Rotate(byte[] tokenHash, byte[] manualCodeHash, DateTimeOffset, DateTimeOffset)`; `MarkUsed`/`Revoke` **null `ManualCodeHash`**; new `ClearManualCode()` (reclaim, does not touch `RevokedAt`/`UsedAt`); partial-unique index `UX_CheckInTokens_ManualCodeHash`.
- **Task 14 → 15/16/17:** `POST /api/customer/reservations/{id}/check-in-token` → `200 { token: string, manualCode: string /* ^\d{6}$ */, expiresAt: string }`; `503 CHECK_IN_CODE_UNAVAILABLE` on retry exhaustion. Introduces `IManualCodeSource { ManualCheckInCode Next(); }` (default `DefaultManualCodeSource` wraps `ManualCheckInCode.Generate`), injected into `IssueToken`; the generation loop does **lazy reclaim** of stale colliding rows within the same transaction.
- **Task 15 → 18:** `POST /api/totem/check-in/resolve` and `/confirm` accept a 6-digit `token`; look up `ManualCodeHash == IManualCheckInCodeHasher.Hash(code)`; same `CheckInPreviewDto` / visit result as the QR path; invalid → `400 { code: "INVALID_CHECK_IN", message: "Não foi possível validar este código." }`.
- **Task 17 → :** `customerApi.issueCheckInToken(id) : Promise<{ token: string; manualCode: string; expiresAt: string }>`.
- **Task 18 → :** `onlyDigits6(v: string) : string`, `isComplete6(v: string) : boolean`.

---

## Task 1: Backend — `TotemProfessionalStatus` pure mapper

**Files:**
- Create: `recepcaototem/Features/Totem/TotemProfessionalStatus.cs`
- Test: `tests/GestaoPredio.UnitTests/TotemProfessionalStatusTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static string TotemProfessionalStatus.Resolve(bool inService, bool effectivePresence)`.

- [ ] **Step 1: Write the failing test**

`tests/GestaoPredio.UnitTests/TotemProfessionalStatusTests.cs`:
```csharp
using recepcaototem.Features.Totem;
using Xunit;

namespace GestaoPredio.UnitTests;

public sealed class TotemProfessionalStatusTests
{
    [Theory]
    [InlineData(true, true, "IN_SERVICE")]
    [InlineData(true, false, "IN_SERVICE")]
    [InlineData(false, true, "AVAILABLE")]
    [InlineData(false, false, "UNAVAILABLE")]
    public void Resolve_maps_presence_and_service_to_the_three_totem_states(
        bool inService, bool effectivePresence, string expected)
    {
        Assert.Equal(expected, TotemProfessionalStatus.Resolve(inService, effectivePresence));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/GestaoPredio.UnitTests --filter TotemProfessionalStatusTests`
Expected: FAIL — `TotemProfessionalStatus` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`recepcaototem/Features/Totem/TotemProfessionalStatus.cs`:
```csharp
namespace recepcaototem.Features.Totem;

/// <summary>
/// Pure mapping of the two real signals — an in-service visit and effective
/// physical presence (see <c>PresenceEvaluator</c>) — to the Totem's 3 states.
/// IN_SERVICE has precedence. No room-availability check here: the carousel is
/// about "working today, worth booking", not "can be seen right now".
/// </summary>
public static class TotemProfessionalStatus
{
    public static string Resolve(bool inService, bool effectivePresence) =>
        inService ? "IN_SERVICE"
        : effectivePresence ? "AVAILABLE"
        : "UNAVAILABLE";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/GestaoPredio.UnitTests --filter TotemProfessionalStatusTests`
Expected: PASS (4 cases).

- [ ] **Step 5: Commit**

```bash
git add recepcaototem/Features/Totem/TotemProfessionalStatus.cs tests/GestaoPredio.UnitTests/TotemProfessionalStatusTests.cs
git commit -m "feat(totem): pure professional-status mapper (IN_SERVICE/AVAILABLE/UNAVAILABLE)"
```

---

## Task 2: Backend — enriched `GET /api/totem/professionals`

**Files:**
- Modify: `recepcaototem/Features/Totem/TotemEndpoints.cs` (add `record TotemProfessionalCard`; replace body of `private static async Task<IResult> Professionals(...)`)
- Test: `tests/GestaoPredio.IntegrationTests/TotemProfessionalsCarouselTests.cs` (new)

**Interfaces:**
- Consumes: `TotemProfessionalStatus.Resolve` (Task 1); `PresenceEvaluator.IsEffective` (existing); `TimeZoneInfo`, `TimeProvider`, `ApplicationDbContext`, `CustomerPublicRateLimiter` (existing DI).
- Produces: `public sealed record TotemProfessionalCard(Guid Id, string Name, string Profession, string? PhotoUrl, string Status)`; `GET /api/totem/professionals` → `TotemProfessionalCard[]`.

- [ ] **Step 1: Write the failing test**

`tests/GestaoPredio.IntegrationTests/TotemProfessionalsCarouselTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class TotemProfessionalsCarouselTests(ModulesApiFactory factory)
{
    private sealed record Card(string Id, string Name, string Profession, string? PhotoUrl, string Status);

    [Fact]
    public async Task Lists_only_active_professionals_ordered_by_name_with_minimal_fields()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var beatriz = Professional.Create("Beatriz Silva", "Nutricionista", "+5569999990001", now);
            var ana = Professional.Create("Ana Souza", "Fisioterapeuta", "+5569999990002", now);
            var inactive = Professional.Create("Zeca Inativo", "Psicólogo", "+5569999990003", now);
            inactive.Deactivate(now);
            db.Professionals.AddRange(beatriz, ana, inactive);
            await db.SaveChangesAsync();
        }

        var response = await factory.Client.GetAsync("/api/totem/professionals");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cards = await response.Content.ReadFromJsonAsync<List<Card>>();
        Assert.NotNull(cards);
        Assert.Equal(new[] { "Ana Souza", "Beatriz Silva" }, cards!.Select(c => c.Name).ToArray());
        Assert.All(cards, c => Assert.Null(c.PhotoUrl));
        Assert.All(cards, c => Assert.Equal("UNAVAILABLE", c.Status));

        // Minimization: raw JSON must not carry any extra property.
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var names = el.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "id", "name", "photourl", "profession", "status" }, names);
        }
    }

    [Fact]
    public async Task Status_reflects_in_service_visit_then_effective_presence_then_unavailable()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();   // establishment open "now"
        Guid inServiceId, presentId, absentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var a = Professional.Create("A Atende", "X", "+5569999991001", now);
            var b = Professional.Create("B Presente", "X", "+5569999991002", now);
            var c = Professional.Create("C Ausente", "X", "+5569999991003", now);
            db.Professionals.AddRange(a, b, c);
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(a.Id, now));
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(b.Id, now));
            db.Visits.Add(Visit.Arrive(a.Id, null, Guid.NewGuid(), "Visitante", "TOTEM", now, Guid.NewGuid()));
            await db.SaveChangesAsync();
            var visit = await db.Visits.SingleAsync(v => v.ProfessionalId == a.Id);
            visit.StartService(now);          // -> VisitStatus.InService
            await db.SaveChangesAsync();
            (inServiceId, presentId, absentId) = (a.Id, b.Id, c.Id);
        }

        var cards = await factory.Client.GetFromJsonAsync<List<Card>>("/api/totem/professionals");
        string Status(Guid id) => cards!.Single(c => c.Id == id.ToString()).Status;
        Assert.Equal("IN_SERVICE", Status(inServiceId));
        Assert.Equal("AVAILABLE", Status(presentId));
        Assert.Equal("UNAVAILABLE", Status(absentId));
    }

    [Fact]
    public async Task Effective_presence_but_establishment_closed_is_unavailable()
    {
        await factory.ResetAsync();
        // NO operating hours seeded => PresenceEvaluator fails closed for the civil day.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var p = Professional.Create("So Presenca", "X", "+5569999992001", now);
            db.Professionals.Add(p);
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(p.Id, now));
            await db.SaveChangesAsync();
        }

        var cards = await factory.Client.GetFromJsonAsync<List<Card>>("/api/totem/professionals");
        Assert.Equal("UNAVAILABLE", Assert.Single(cards!).Status);
    }

    [Fact]
    public async Task Photo_url_is_the_public_totem_path_when_a_photo_exists()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        Guid withPhoto;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var p = Professional.Create("Com Foto", "X", "+5569999993001", now);
            p.SetPhoto(Guid.NewGuid(), now);
            db.Professionals.Add(p);
            await db.SaveChangesAsync();
            withPhoto = p.Id;
        }

        var cards = await factory.Client.GetFromJsonAsync<List<Card>>("/api/totem/professionals");
        Assert.Equal($"/api/totem/professionals/{withPhoto}/photo", Assert.Single(cards!).PhotoUrl);
    }
}
```
> If `Visit.StartService` / `ProfessionalPresence.StartByQr` signatures differ, adapt to the real domain API discovered while implementing — the assertions (status strings, ordering, field set) are the contract.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter TotemProfessionalsCarouselTests`
Expected: FAIL — response still has `description` / lacks `status` & `photoUrl`.

- [ ] **Step 3: Write minimal implementation**

In `recepcaototem/Features/Totem/TotemEndpoints.cs`, add near the other records:
```csharp
public sealed record TotemProfessionalCard(Guid Id, string Name, string Profession, string? PhotoUrl, string Status);
```
Replace the body of `Professionals` (keep the map registration line `endpoints.MapGet("/api/totem/professionals", Professionals).AllowAnonymous();`):
```csharp
private static async Task<IResult> Professionals(
    HttpContext context, CustomerPublicRateLimiter limiter, ApplicationDbContext db,
    TimeZoneInfo timeZone, TimeProvider time, CancellationToken ct)
{
    using var lease = await limiter.AcquireAsync(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", "totem-professionals", ct);
    if (!lease.IsAcquired)
        return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);

    var professionals = await db.Professionals.AsNoTracking()
        .Where(x => x.IsActive)
        .OrderBy(x => x.NormalizedName)
        .Select(x => new { x.Id, x.Name, x.Profession, x.PhotoFileId })
        .ToListAsync(ct);
    if (professionals.Count == 0) return Results.Ok(Array.Empty<TotemProfessionalCard>());

    var ids = professionals.Select(x => x.Id).ToList();
    var inService = (await db.Visits.AsNoTracking()
        .Where(v => ids.Contains(v.ProfessionalId) && v.Status == VisitStatus.InService)
        .Select(v => v.ProfessionalId).Distinct().ToListAsync(ct)).ToHashSet();
    var operatingHours = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(ct);
    var presenceByProfessional = (await db.ProfessionalPresences.AsNoTracking()
            .Where(x => ids.Contains(x.ProfessionalId) && x.EndedAt == null).ToListAsync(ct))
        .GroupBy(x => x.ProfessionalId)
        .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartedAt).First());

    var now = time.GetUtcNow();
    var cards = professionals.Select(p =>
    {
        presenceByProfessional.TryGetValue(p.Id, out var presence);
        var status = TotemProfessionalStatus.Resolve(
            inService.Contains(p.Id),
            PresenceEvaluator.IsEffective(presence, operatingHours, now, timeZone));
        return new TotemProfessionalCard(
            p.Id, p.Name, p.Profession,
            p.PhotoFileId is null ? null : $"/api/totem/professionals/{p.Id}/photo",
            status);
    }).ToArray();

    return Results.Ok(cards);
}
```
Add `using`s if missing: `GestaoPredio.Domain.Visits;`, `GestaoPredio.Application.Availability;` (already present).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter TotemProfessionalsCarouselTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Run the neighbouring suites for regressions**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter "TotemPresenceApiTests|ProfessionalQueryTests"`
Expected: PASS (unchanged).

- [ ] **Step 6: Commit**

```bash
git add recepcaototem/Features/Totem/TotemEndpoints.cs tests/GestaoPredio.IntegrationTests/TotemProfessionalsCarouselTests.cs
git commit -m "feat(totem): enrich GET /api/totem/professionals with photoUrl + real status"
```

---

## Task 3: Backend — public professional photo endpoint

**Files:**
- Create: `recepcaototem/Features/Professionals/ProfessionalPhotoStreaming.cs`
- Modify: `recepcaototem/Features/Professionals/ProfessionalPhotoEndpoints.cs` (`Get` delegates to the helper)
- Modify: `recepcaototem/Features/Totem/TotemEndpoints.cs` (new `ProfessionalPhoto` handler + route)
- Test: `tests/GestaoPredio.IntegrationTests/TotemProfessionalsCarouselTests.cs` (append photo cases)

**Interfaces:**
- Consumes: `IPrivateFileStorage`, `ApplicationDbContext`, `ILoggerFactory` (existing DI); `PrivateFilePurposes.ProfessionalPhoto`.
- Produces: `ProfessionalPhotoStreaming.StreamAsync(Professional professional, ApplicationDbContext db, IPrivateFileStorage storage, ILoggerFactory loggerFactory, HttpContext context, string cacheControl, CancellationToken ct) : Task<IResult>`; `GET /api/totem/professionals/{id:guid}/photo`.

- [ ] **Step 1: Write the failing tests** (append to `TotemProfessionalsCarouselTests.cs`)

```csharp
[Fact]
public async Task Public_photo_endpoint_streams_only_for_active_professionals()
{
    await factory.ResetAsync();
    Guid activeWithPhoto, inactiveWithPhoto, activeNoPhoto;
    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var a = Professional.Create("Ativo Foto", "X", "+5569999994001", now);
        var b = Professional.Create("Inativo Foto", "X", "+5569999994002", now);
        var c = Professional.Create("Ativo Sem Foto", "X", "+5569999994003", now);
        a.SetPhoto(await SeedPhotoFileAsync(db), now);
        b.SetPhoto(await SeedPhotoFileAsync(db), now);
        b.Deactivate(now);
        db.Professionals.AddRange(a, b, c);
        await db.SaveChangesAsync();
        (activeWithPhoto, inactiveWithPhoto, activeNoPhoto) = (a.Id, b.Id, c.Id);
    }

    var ok = await factory.Client.GetAsync($"/api/totem/professionals/{activeWithPhoto}/photo");
    Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    Assert.Equal("public, max-age=300", ok.Headers.CacheControl?.ToString());
    Assert.False(string.IsNullOrEmpty(ok.Content.Headers.ContentType?.MediaType));

    Assert.Equal(HttpStatusCode.NotFound,
        (await factory.Client.GetAsync($"/api/totem/professionals/{inactiveWithPhoto}/photo")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound,
        (await factory.Client.GetAsync($"/api/totem/professionals/{activeNoPhoto}/photo")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound,
        (await factory.Client.GetAsync($"/api/totem/professionals/{Guid.NewGuid()}/photo")).StatusCode);
}

// Helper: stage a real PROFESSIONAL_PHOTO file through IPrivateFileStorage and return its PrivateFile id.
private async Task<Guid> SeedPhotoFileAsync(ApplicationDbContext db) { /* implement using the same
    staging path ProfessionalPhotoEndpoints.Put uses (IPrivateFileStorage + PrivateFile.Create with
    PrivateFilePurposes.ProfessionalPhoto). Mirror ProfessionalPhotoTests.cs setup. */ }
```
> Model `SeedPhotoFileAsync` on the existing `ProfessionalPhotoTests.cs` fixture. The admin‑regression test below stays in `ProfessionalPhotoTests.cs` (already exists) and must keep passing unchanged.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter TotemProfessionalsCarouselTests`
Expected: FAIL — route `/api/totem/professionals/{id}/photo` returns 404 for the active case (not mapped).

- [ ] **Step 3: Extract the shared helper**

`recepcaototem/Features/Professionals/ProfessionalPhotoStreaming.cs`:
```csharp
using GestaoPredio.Application.Files;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

internal static class ProfessionalPhotoStreaming
{
    // Streams the professional's photo bytes. Caller has already decided the professional
    // is allowed to be seen. 404 when there is no usable photo; 503 on storage I/O failure
    // (identical to the admin endpoint's historical behaviour).
    public static async Task<IResult> StreamAsync(
        Professional professional, ApplicationDbContext db, IPrivateFileStorage storage,
        ILoggerFactory loggerFactory, HttpContext context, string cacheControl, CancellationToken ct)
    {
        if (professional.PhotoFileId is null) return Results.NotFound();

        var metadata = await db.PrivateFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == professional.PhotoFileId, ct);
        if (metadata is null || !string.Equals(metadata.Purpose, PrivateFilePurposes.ProfessionalPhoto, StringComparison.Ordinal))
            return PhotoUnavailable(loggerFactory, context, professional.Id, professional.PhotoFileId.Value);

        Stream? stream;
        try
        {
            stream = await storage.OpenReadAsync(metadata.StorageKey, ct);
            if (stream is null || !stream.CanSeek || stream.Length != metadata.Length)
            {
                if (stream is not null) await stream.DisposeAsync();
                return PhotoUnavailable(loggerFactory, context, professional.Id, metadata.Id);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return PhotoUnavailable(loggerFactory, context, professional.Id, metadata.Id);
        }

        context.Response.Headers.ContentDisposition = "inline";
        context.Response.Headers.CacheControl = cacheControl;
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(stream, metadata.MimeType, enableRangeProcessing: false);
    }

    private static IResult PhotoUnavailable(ILoggerFactory lf, HttpContext ctx, Guid professionalId, Guid fileId)
    {
        lf.CreateLogger("ProfessionalPhoto").LogError(
            "Professional photo unavailable. ProfessionalId={ProfessionalId} PrivateFileId={PrivateFileId} CorrelationId={CorrelationId}",
            professionalId, fileId, ctx.TraceIdentifier);
        return Results.Json(new ApiError("PHOTO_UNAVAILABLE", "A foto do profissional está temporariamente indisponível."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
```
Then in `ProfessionalPhotoEndpoints.Get`, after loading `professional` (keep the `professional is null` → `NotFound`), replace the metadata+stream block with:
```csharp
if (professional is null) return Results.NotFound();
return await ProfessionalPhotoStreaming.StreamAsync(
    professional, db, storage, loggerFactory, context, "private, no-store", cancellationToken);
```
Delete the now‑duplicated private `PhotoUnavailable` in `ProfessionalPhotoEndpoints` **only if** nothing else references it; otherwise leave it.

- [ ] **Step 4: Add the public Totem route + handler**

In `TotemEndpoints.MapTotemEndpoints`, after the professionals line:
```csharp
endpoints.MapGet("/api/totem/professionals/{id:guid}/photo", ProfessionalPhoto).AllowAnonymous();
```
Handler:
```csharp
private static async Task<IResult> ProfessionalPhoto(
    Guid id, HttpContext context, ApplicationDbContext db,
    GestaoPredio.Application.Files.IPrivateFileStorage storage,
    ILoggerFactory loggerFactory, CancellationToken ct)
{
    var professional = await db.Professionals.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
    if (professional is null) return Results.NotFound();
    return await recepcaototem.Features.Professionals.ProfessionalPhotoStreaming.StreamAsync(
        professional, db, storage, loggerFactory, context, "public, max-age=300", ct);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter "TotemProfessionalsCarouselTests|ProfessionalPhotoTests|ProfessionalPhotoFailureTests"`
Expected: PASS — new Totem photo cases green; admin photo tests unchanged (still `private, no-store`, still auth‑gated).

- [ ] **Step 6: Commit**

```bash
git add recepcaototem/Features/Professionals/ProfessionalPhotoStreaming.cs recepcaototem/Features/Professionals/ProfessionalPhotoEndpoints.cs recepcaototem/Features/Totem/TotemEndpoints.cs tests/GestaoPredio.IntegrationTests/TotemProfessionalsCarouselTests.cs
git commit -m "feat(totem): narrow public professional photo endpoint (active only, shared streamer)"
```

---

## Task 4: Frontend — API client + `professionalInitials`

**Files:**
- Modify: `recepcaototem/ClientApp/src/api/modules.ts`
- Create: `recepcaototem/ClientApp/src/features/totem/professionalInitials.ts` + `.test.ts`
- Create: `recepcaototem/ClientApp/src/api/totemProfessionals.test.ts`

**Interfaces:**
- Consumes: Task 2 JSON shape; existing `apiClient.get<T>(url, { signal })`.
- Produces: `TotemProfessionalCardDto`; `totemApi.professionals(signal?)`; `professionalInitials(name)`.

- [ ] **Step 1: Write failing tests**

`src/features/totem/professionalInitials.test.ts`:
```ts
import { expect, test } from 'vitest'
import { professionalInitials } from './professionalInitials'

test('two words -> first letter of first and last, uppercased', () => {
  expect(professionalInitials('Dra. Helena Smoke')).toBe('HS')
  expect(professionalInitials('ana souza')).toBe('AS')
})
test('single word -> one letter', () => {
  expect(professionalInitials('Beatriz')).toBe('B')
})
test('empty / whitespace -> stable placeholder', () => {
  expect(professionalInitials('')).toBe('?')
  expect(professionalInitials('   ')).toBe('?')
})
```

`src/api/totemProfessionals.test.ts`:
```ts
import { afterEach, expect, test, vi } from 'vitest'
import { apiClient } from './client'
import { totemApi } from './modules'

afterEach(() => vi.restoreAllMocks())

test('totemApi.professionals GETs the public carousel endpoint', async () => {
  const get = vi.spyOn(apiClient, 'get').mockResolvedValue([
    { id: 'p1', name: 'Ana', profession: 'Fisio', photoUrl: null, status: 'AVAILABLE' },
  ] as never)
  const result = await totemApi.professionals()
  expect(get).toHaveBeenCalledWith('/api/totem/professionals', { signal: undefined })
  expect(result[0].status).toBe('AVAILABLE')
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/features/totem/professionalInitials.test.ts src/api/totemProfessionals.test.ts`
Expected: FAIL — modules not found.

- [ ] **Step 3: Implement**

`src/features/totem/professionalInitials.ts`:
```ts
// Elegant fallback for a missing professional photo: 1–2 uppercase letters.
export function professionalInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].charAt(0).toUpperCase()
  return (parts[0].charAt(0) + parts[parts.length - 1].charAt(0)).toUpperCase()
}
```

In `src/api/modules.ts`, add the interface near the other DTOs and extend `totemApi`:
```ts
export interface TotemProfessionalCardDto {
  id: string
  name: string
  profession: string
  photoUrl: string | null
  status: 'AVAILABLE' | 'IN_SERVICE' | 'UNAVAILABLE'
}
```
```ts
export const totemApi = {
  professionals(signal?: AbortSignal) {
    return apiClient.get<TotemProfessionalCardDto[]>('/api/totem/professionals', { signal })
  },
  resolveCheckIn(token: string) {
    return apiClient.post<CheckInPreviewDto>('/api/totem/check-in/resolve', { token })
  },
  confirmCheckIn(token: string) {
    return apiClient.post<CheckInResultDto>('/api/totem/check-in/confirm', { token })
  },
}
```

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/features/totem/professionalInitials.test.ts src/api/totemProfessionals.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/api/modules.ts src/features/totem/professionalInitials.ts src/features/totem/professionalInitials.test.ts src/api/totemProfessionals.test.ts
git commit -m "feat(totem): totemApi.professionals client + initials fallback helper"
```

---

## Task 5: Frontend — `safeCustomerReturnUrl` (strict open-redirect guard)

**Files:**
- Create: `recepcaototem/ClientApp/src/auth/returnUrl.ts` + `returnUrl.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces: `safeCustomerReturnUrl(raw: string | null | undefined): string | null`.

- [ ] **Step 1: Write the failing tests**

`src/auth/returnUrl.test.ts`:
```ts
import { describe, expect, it } from 'vitest'
import { safeCustomerReturnUrl } from './returnUrl'

describe('safeCustomerReturnUrl — rejects', () => {
  const bad: [string, string | null | undefined][] = [
    ['null', null], ['undefined', undefined], ['empty', ''],
    ['https', 'https://evil.com'], ['http', 'http://evil.com'], ['HTTP upper', 'HTTP://evil.com'],
    ['javascript', 'javascript:alert(1)'], ['data', 'data:text/html,x'],
    ['protocol-relative', '//evil.com'], ['slash-backslash', '/\\evil.com'],
    ['bare backslash', '\\evil'], ['backslash after cliente', '/cliente\\evil'],
    ['encoded //', '%2F%2Fevil.com'], ['double-encoded //', '%252F%252Fevil.com'],
    ['encoded scheme', '%68ttp://evil'],
    ['traversal', '/cliente/../admin'], ['encoded traversal', '/cliente/%2e%2e/admin'],
    ['admin', '/admin'], ['recepcao', '/recepcao'], ['root', '/'], ['login', '/login'],
    ['cliente prefix trick', '/cliente-admin'], ['clientefoo', '/clientefoo'],
    ['script in query', '/cliente/agendar?x=<script>'],
    ['two question marks', '/cliente/agendar?a=b?c=d'],
    ['control char (NUL)', '/cliente/age' + String.fromCharCode(0) + 'nda'],
    ['too long', '/cliente/' + 'a'.repeat(600)],
  ]
  it.each(bad)('%s', (_label, value) => expect(safeCustomerReturnUrl(value)).toBeNull())
})

describe('safeCustomerReturnUrl — accepts (normalized)', () => {
  it.each([
    ['/cliente', '/cliente'],
    ['/cliente/agendar', '/cliente/agendar'],
    ['/cliente/agendamentos', '/cliente/agendamentos'],
    ['/cliente/agendamentos/abc', '/cliente/agendamentos/abc'],
    ['/cliente/agendar?professionalId=8f3c1e2a-0000-4a00-8000-000000000001',
     '/cliente/agendar?professionalId=8f3c1e2a-0000-4a00-8000-000000000001'],
    ['%2Fcliente%2Fagendar%3FprofessionalId%3D8f3c1e2a-0000-4a00-8000-000000000001',
     '/cliente/agendar?professionalId=8f3c1e2a-0000-4a00-8000-000000000001'],
  ])('%s', (input, expected) => expect(safeCustomerReturnUrl(input)).toBe(expected))
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/auth/returnUrl.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement** (from spec §10.1)

`src/auth/returnUrl.ts`:
```ts
// Strict allowlist for post-login redirects into the CUSTOMER portal.
// Only paths under /cliente are ever accepted. Everything else -> null.

const hasControlChar = (s: string) =>
  [...s].some((c) => { const n = c.charCodeAt(0); return n < 0x20 || (n >= 0x7f && n <= 0x9f) })
const CUSTOMER_PATH = /^\/cliente(?:\/[A-Za-z0-9_-]+)*\/?$/
const CUSTOMER_QUERY = /^[A-Za-z0-9_-]+=[A-Za-z0-9_-]*(?:&[A-Za-z0-9_-]+=[A-Za-z0-9_-]*)*$/

export function safeCustomerReturnUrl(raw: string | null | undefined): string | null {
  if (!raw || typeof raw !== 'string') return null
  if (raw.length > 512) return null
  if (hasControlChar(raw)) return null

  let value = raw
  for (let i = 0; i < 4; i++) {
    let next: string
    try { next = decodeURIComponent(value) } catch { return null }
    if (next === value) break
    value = next
  }

  if (hasControlChar(value)) return null
  if (value.includes('\\')) return null
  if (value.includes('%')) return null
  if (value.includes('://')) return null
  if (value.includes('..')) return null
  if (value.startsWith('//')) return null
  if (!value.startsWith('/cliente')) return null

  const q = value.indexOf('?')
  const path = q === -1 ? value : value.slice(0, q)
  const query = q === -1 ? '' : value.slice(q + 1)
  if (query.includes('?')) return null
  if (path.includes('//')) return null
  if (!CUSTOMER_PATH.test(path)) return null
  if (query && !CUSTOMER_QUERY.test(query)) return null

  return query ? `${path}?${query}` : path
}
```

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/auth/returnUrl.test.ts`
Expected: PASS (all reject + accept rows).

- [ ] **Step 5: Commit**

```bash
git add src/auth/returnUrl.ts src/auth/returnUrl.test.ts
git commit -m "feat(auth): safeCustomerReturnUrl — strict /cliente allowlist against open redirect"
```

---

## Task 6: Frontend — local "Magic UI" components

**Files:**
- Create: `src/features/totem/magic/LightRays.tsx`, `BlurFade.tsx`, `MagicCard.tsx`, `BorderBeam.tsx`, `ProgressiveBlur.tsx`, `RippleButton.tsx`
- Create: `src/features/totem/magic/magic.test.tsx`
- Modify: `src/styles.css` (append `.totem-magic-*` block)

**Interfaces:** produced components + props listed in "Interfaces between tasks".

- [ ] **Step 1: Write the failing test**

`src/features/totem/magic/magic.test.tsx`:
```tsx
import { render, screen } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { LightRays } from './LightRays'
import { BlurFade } from './BlurFade'
import { MagicCard } from './MagicCard'
import { BorderBeam } from './BorderBeam'
import { ProgressiveBlur } from './ProgressiveBlur'
import { RippleButton } from './RippleButton'

function mockReducedMotion(reduced: boolean) {
  vi.stubGlobal('matchMedia', (q: string) => ({
    matches: reduced && q.includes('reduce'),
    media: q, addEventListener: vi.fn(), removeEventListener: vi.fn(),
    addListener: vi.fn(), removeListener: vi.fn(), onchange: null, dispatchEvent: vi.fn(),
  }))
}
beforeEach(() => mockReducedMotion(false))

test('LightRays is decorative and non-interactive', () => {
  const { container } = render(<LightRays />)
  const el = container.firstChild as HTMLElement
  expect(el).toHaveAttribute('aria-hidden', 'true')
  expect(getComputedStyle(el).pointerEvents).toBe('none')
})

test('BlurFade renders its children', () => {
  render(<BlurFade><span>oi</span></BlurFade>)
  expect(screen.getByText('oi')).toBeInTheDocument()
})

test('MagicCard renders a real button and fires onClick', async () => {
  const onClick = vi.fn()
  render(<MagicCard as="button" onClick={onClick}>tap</MagicCard>)
  screen.getByRole('button', { name: 'tap' }).click()
  expect(onClick).toHaveBeenCalled()
})

test('BorderBeam animates only when active; reduced motion keeps it static', () => {
  const { rerender, container } = render(<BorderBeam active={false} />)
  expect(container.querySelector('.totem-magic-beam.is-active')).toBeNull()
  rerender(<BorderBeam active />)
  expect(container.querySelector('.totem-magic-beam.is-active')).not.toBeNull()
  mockReducedMotion(true)
  rerender(<BorderBeam active />)
  expect(container.querySelector('.totem-magic-beam.is-static')).not.toBeNull()
})

test('ProgressiveBlur is a non-interactive edge mask', () => {
  const { container } = render(<ProgressiveBlur side="left" />)
  const el = container.firstChild as HTMLElement
  expect(el).toHaveAttribute('aria-hidden', 'true')
  expect(el.className).toContain('totem-magic-progblur')
})

test('RippleButton is a real button, blocks when disabled, no ripple under reduced motion', () => {
  const onClick = vi.fn()
  const { rerender } = render(<RippleButton onClick={onClick} disabled>x</RippleButton>)
  screen.getByRole('button', { name: 'x' }).click()
  expect(onClick).not.toHaveBeenCalled()
  mockReducedMotion(true)
  rerender(<RippleButton onClick={onClick}>x</RippleButton>)
  screen.getByRole('button', { name: 'x' }).click()
  expect(onClick).toHaveBeenCalledTimes(1)
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/features/totem/magic/magic.test.tsx`
Expected: FAIL — components missing.

- [ ] **Step 3: Implement the six components**

Create a tiny shared hook `src/features/totem/magic/usePrefersReducedMotion.ts`:
```ts
import { useEffect, useState } from 'react'
export function usePrefersReducedMotion(): boolean {
  const [reduced, setReduced] = useState(
    () => typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches)
  useEffect(() => {
    if (typeof matchMedia !== 'function') return
    const mq = matchMedia('(prefers-reduced-motion: reduce)')
    const on = () => setReduced(mq.matches)
    mq.addEventListener?.('change', on)
    return () => mq.removeEventListener?.('change', on)
  }, [])
  return reduced
}
```
- `LightRays.tsx` — `return <div className={\`totem-magic-rays ${className ?? ''}\`} aria-hidden="true" />`. CSS provides the animated diagonal streaks + `pointer-events:none`; reduced‑motion handled purely in CSS (`@media (prefers-reduced-motion: reduce) { .totem-magic-rays { animation: none } }`).
- `BlurFade.tsx` — one‑shot: `const [shown, setShown] = useState(false); useEffect(() => { const id = requestAnimationFrame(() => setShown(true)); return () => cancelAnimationFrame(id) }, [])`. Wrap children in `<div className={\`totem-magic-fade ${shown ? 'is-in' : ''}\`} style={{ transitionDelay: \`${delay ?? 0}ms\` }}>`. Under reduced motion the CSS zeroes the transform/blur so it just appears.
- `MagicCard.tsx` — renders `<button className={\`totem-magic-card ${className ?? ''}\`} onClick={onClick}>{children}</button>` (only `as="button"` is used here). On `pointermove` **only** when `matchMedia('(hover:hover) and (pointer:fine)').matches` and not reduced motion, set CSS vars `--mx/--my` for a faint radial highlight. No hover dependency for function.
- `BorderBeam.tsx` — `const reduced = usePrefersReducedMotion(); return <span aria-hidden="true" className={\`totem-magic-beam ${active ? 'is-active' : ''} ${active && reduced ? 'is-static' : ''}\`} />`. Palette `#FFFFFF/#888888/#3D3D3D` via CSS conic‑gradient; `is-static` = plain 1px `#3D3D3D` border, no animation.
- `ProgressiveBlur.tsx` — `return <div aria-hidden="true" className={\`totem-magic-progblur totem-magic-progblur--${side}\`} />`. CSS: stacked `backdrop-filter: blur()` layers with a `mask-image` gradient; `pointer-events:none`.
- `RippleButton.tsx` — `<button className={\`totem-magic-ripple ${className ?? ''}\`} disabled={disabled} aria-label={ariaLabel} onClick={(e) => { if (disabled) return; if (!reduced) spawnRipple(e); onClick(e) }}>{children}</button>`. `spawnRipple` appends a positioned `<span class="totem-magic-ripple-dot">` and removes it on `animationend`.

Append the `.totem-magic-*` CSS block to `src/styles.css` (dark‑identity tokens already exist; use `#181818/#222/#272727/#f2f2f2/#888/#3d3d3d`). Every animation guarded by `@media (prefers-reduced-motion: reduce)`.

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/features/totem/magic/magic.test.tsx`
Expected: PASS.

- [ ] **Step 5: Type + build check**

Run: `npx tsc -b && npx vite build`
Expected: both succeed.

- [ ] **Step 6: Commit**

```bash
git add src/features/totem/magic src/styles.css
git commit -m "feat(totem): local Magic UI components (LightRays, BlurFade, MagicCard, BorderBeam, ProgressiveBlur, RippleButton)"
```

---

## Task 7: Frontend — `/totem` decision screen (`TotemEntry`)

**Files:**
- Create: `src/pages/TotemEntry.tsx` + `.test.tsx`
- Modify: `src/App.tsx`, `src/dev/DevelopmentApp.tsx` (route `/totem` → `<TotemEntry />`)
- Modify: `src/frontend-portals.test.ts` (Totem route assertions)
- Modify: `src/styles.css` (append `.totem-entry-*`)

**Interfaces:**
- Consumes: `LightRays`, `BlurFade`, `MagicCard`, `RippleButton` (Task 6); `KioskClock` (existing).
- Produces: `<TotemEntry />` at `/totem`.

- [ ] **Step 1: Write the failing test**

`src/pages/TotemEntry.test.tsx`:
```tsx
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { TotemEntry } from './TotemEntry'

vi.stubGlobal('matchMedia', (q: string) => ({
  matches: false, media: q, addEventListener: vi.fn(), removeEventListener: vi.fn(),
  addListener: vi.fn(), removeListener: vi.fn(), onchange: null, dispatchEvent: vi.fn(),
}))

const renderAt = () => render(
  <MemoryRouter initialEntries={['/totem']}>
    <Routes>
      <Route path="/totem" element={<TotemEntry />} />
      <Route path="/totem/check-in" element={<div>CHECKIN</div>} />
      <Route path="/totem/profissionais" element={<div>CARROSSEL</div>} />
    </Routes>
  </MemoryRouter>,
)

test('asks only how to continue, with two options and one support line', () => {
  renderAt()
  expect(screen.getByRole('img', { name: 'LUMIS' })).toBeInTheDocument()
  expect(screen.getByText('Bem-vindo')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /como deseja continuar\?/i })).toBeInTheDocument()
  expect(screen.getByText('Toque em uma opção para continuar.')).toBeInTheDocument()
  expect(screen.getByText(/^\d{2}:\d{2}$/)).toBeInTheDocument()        // KioskClock
  expect(screen.queryByLabelText(/código/i)).not.toBeInTheDocument()    // no code field
})

test('"Tenho código" navigates to /totem/check-in', () => {
  renderAt()
  screen.getByRole('button', { name: /tenho código/i }).click()
  expect(screen.getByText('CHECKIN')).toBeInTheDocument()
})

test('"Não tenho código" navigates to /totem/profissionais', () => {
  renderAt()
  screen.getByRole('button', { name: /não tenho código/i }).click()
  expect(screen.getByText('CARROSSEL')).toBeInTheDocument()
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/pages/TotemEntry.test.tsx`
Expected: FAIL — `TotemEntry` missing.

- [ ] **Step 3: Implement**

`src/pages/TotemEntry.tsx` — centered single column: `<main className="totem-entry">` with `<LightRays />`, `<BlurFade>` around logo, `<span className="totem-eyebrow">Bem-vindo</span>`, `<h1>Como deseja continuar?</h1>`, two `<MagicCard as="button">` (icons `QrCode`, `UserRound`) wrapped so their labels are "Tenho código" / "Não tenho código" (aria-labels explicit), each `onClick={() => navigate('/totem/check-in')}` / `navigate('/totem/profissionais')`, a single `<p className="totem-entry-hint">Toque em uma opção para continuar.</p>`, and `<KioskClock />` in a footer. Optionally wrap the card `onClick`s with `RippleButton` styling if it reads well — keep them real buttons either way.

Append `.totem-entry-*` CSS to `styles.css`.

Routes — in **both** `src/App.tsx` and `src/dev/DevelopmentApp.tsx`, replace the existing `<Route path="/totem" element={<TotemCheckIn />} />` with `<Route path="/totem" element={<TotemEntry />} />` and add the import. Keep `<Route path="/totem/check-in" element={<TotemCheckIn />} />`. (`/totem/profissionais` route is added in Task 9.)

`src/frontend-portals.test.ts` — update the Totem block to:
```ts
for (const source of [appSource, developmentSource]) {
  expect(source).toContain('<Route path="/totem" element={<TotemEntry />} />')
  expect(source).toContain('<Route path="/totem/check-in" element={<TotemCheckIn />} />')
}
expect(totemCheckInSource).toContain('totemApi.resolveCheckIn')
expect(totemCheckInSource).toContain('totemApi.confirmCheckIn')
expect(modulesSource).toContain("'/api/totem/check-in/confirm'")
expect(modulesSource).toContain("'/api/totem/professionals'")
```
(The `/totem/profissionais` assertion is added in Task 9.)

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/pages/TotemEntry.test.tsx src/frontend-portals.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/pages/TotemEntry.tsx src/pages/TotemEntry.test.tsx src/App.tsx src/dev/DevelopmentApp.tsx src/frontend-portals.test.ts src/styles.css
git commit -m "feat(totem): /totem decision screen (Tenho codigo / Nao tenho codigo)"
```

---

## Task 8: Frontend — `TotemProfessionalCarousel` component

**Files:**
- Create: `src/features/totem/TotemProfessionalCarousel.tsx` + `.test.tsx`
- Modify: `src/styles.css` (append `.totem-carousel-*`, `.totem-status-*`)

**Interfaces:**
- Consumes: `TotemProfessionalCardDto`, `professionalInitials` (Task 4); `BorderBeam`, `ProgressiveBlur` (Task 6).
- Produces: `<TotemProfessionalCarousel professionals onActiveChange />`.

- [ ] **Step 1: Write the failing test**

`src/features/totem/TotemProfessionalCarousel.test.tsx`:
```tsx
import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { TotemProfessionalCarousel } from './TotemProfessionalCarousel'
import type { TotemProfessionalCardDto } from '../../api/modules'

vi.stubGlobal('matchMedia', (q: string) => ({
  matches: false, media: q, addEventListener: vi.fn(), removeEventListener: vi.fn(),
  addListener: vi.fn(), removeListener: vi.fn(), onchange: null, dispatchEvent: vi.fn(),
}))

const people: TotemProfessionalCardDto[] = [
  { id: 'a', name: 'Ana Souza', profession: 'Fisioterapeuta', photoUrl: null, status: 'AVAILABLE' },
  { id: 'b', name: 'Bruno Lima', profession: 'Psicólogo', photoUrl: '/api/totem/professionals/b/photo', status: 'IN_SERVICE' },
  { id: 'c', name: 'Carla Reis', profession: 'Nutricionista', photoUrl: null, status: 'UNAVAILABLE' },
]

beforeEach(() => {
  // jsdom has no layout; stub the geometry the carousel reads.
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', { value: vi.fn(), writable: true })
  Object.defineProperty(HTMLElement.prototype, 'scrollBy', { value: vi.fn(), writable: true })
})

test('renders one option per professional with status text + colour class (not colour alone)', () => {
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={vi.fn()} />)
  const options = screen.getAllByRole('option')
  expect(options).toHaveLength(3)
  expect(within(options[0]).getByText('Disponível')).toBeInTheDocument()
  expect(within(options[1]).getByText('Em atendimento')).toBeInTheDocument()
  expect(within(options[2]).getByText('Indisponível')).toBeInTheDocument()
  expect(options[0].querySelector('.totem-status-ok')).not.toBeNull()
  expect(options[1].querySelector('.totem-status-busy')).not.toBeNull()
})

test('first professional is active initially and emitted', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  expect(screen.getAllByRole('option')[0]).toHaveAttribute('aria-selected', 'true')
  expect(onActiveChange).toHaveBeenCalledWith(people[0])
})

test('Próximo arrow and ArrowRight move the active card', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  fireEvent.click(screen.getByRole('button', { name: /próximo/i }))
  expect(onActiveChange).toHaveBeenLastCalledWith(people[1])
  fireEvent.keyDown(screen.getByRole('listbox'), { key: 'ArrowRight' })
  expect(onActiveChange).toHaveBeenLastCalledWith(people[2])
})

test('dots reflect and control the active card', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  fireEvent.click(screen.getByRole('button', { name: /ir para carla reis/i }))
  expect(onActiveChange).toHaveBeenLastCalledWith(people[2])
})

test('missing photo shows initials; broken photo falls back to initials', () => {
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={vi.fn()} />)
  expect(screen.getByText('AS')).toBeInTheDocument()   // Ana Souza, no photo
  const img = screen.getByRole('img', { name: '' }) as HTMLImageElement // Bruno has a photo
  fireEvent.error(img)
  expect(screen.getByText('BL')).toBeInTheDocument()
})

test('mouse drag scrolls and suppresses the click-select', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  const viewport = screen.getByRole('listbox')
  fireEvent.pointerDown(viewport, { pointerType: 'mouse', clientX: 300 })
  fireEvent.pointerMove(viewport, { pointerType: 'mouse', clientX: 120 })
  fireEvent.pointerUp(viewport, { pointerType: 'mouse', clientX: 120 })
  onActiveChange.mockClear()
  fireEvent.click(screen.getAllByRole('option')[2])   // click right after a drag
  expect(onActiveChange).not.toHaveBeenCalled()
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/features/totem/TotemProfessionalCarousel.test.tsx`
Expected: FAIL — component missing.

- [ ] **Step 3: Implement**

`src/features/totem/TotemProfessionalCarousel.tsx`:
- State: `activeIndex` (default 0), refs `viewportRef`, `didDragRef`, `pointerActiveRef`, `dragStartXRef`.
- `useEffect` on `professionals` → reset `activeIndex` to 0 and `onActiveChange(professionals[0])` when non‑empty.
- `setActive(i)` — clamps `0..len-1`, sets state, `onActiveChange(professionals[i])`, and centers: `card.scrollIntoView?.({ inline: 'center', block: 'nearest', behavior })` OR `viewport.scrollTo({ left: card.offsetLeft - (viewport.clientWidth - card.offsetWidth) / 2, behavior })`. `behavior = usePrefersReducedMotion() ? 'auto' : 'smooth'`.
- Markup:
  ```tsx
  <div className="totem-carousel">
    <ProgressiveBlur side="left" />
    <button type="button" className="totem-carousel-arrow is-prev" aria-label="Anterior" onClick={() => setActive(activeIndex - 1)} />
    <div
      className="totem-carousel-viewport" role="listbox" aria-label="Profissionais" tabIndex={0}
      ref={viewportRef}
      onKeyDown={onKeyDown}
      onPointerDown={onPointerDown} onPointerMove={onPointerMove} onPointerUp={onPointerUp}
      onScroll={onScroll}
    >
      {professionals.map((p, i) => (
        <button
          key={p.id} type="button" role="option" aria-selected={i === activeIndex}
          aria-label={`${p.name}, ${p.profession}, ${statusLabel(p.status)}`}
          className={`totem-carousel-card ${i === activeIndex ? 'is-active' : ''}`}
          onClick={() => { if (didDragRef.current) { didDragRef.current = false; return } setActive(i) }}
          ref={i === activeIndex ? activeCardRef : undefined}
        >
          <Photo url={p.photoUrl} name={p.name} />
          <strong>{p.name}</strong>
          <span>{p.profession}</span>
          <span className={`totem-status ${statusClass(p.status)}`}><i /> {statusLabel(p.status)}</span>
          <BorderBeam active={i === activeIndex} />
        </button>
      ))}
    </div>
    <button type="button" className="totem-carousel-arrow is-next" aria-label="Próximo" onClick={() => setActive(activeIndex + 1)} />
    <ProgressiveBlur side="right" />
    <div className="totem-carousel-dots" role="group" aria-label="Selecionar profissional">
      {professionals.map((p, i) => (
        <button key={p.id} type="button" aria-label={`Ir para ${p.name}`} aria-current={i === activeIndex}
          className={`totem-carousel-dot ${i === activeIndex ? 'is-active' : ''}`} onClick={() => setActive(i)} />
      ))}
    </div>
  </div>
  ```
- `Photo` sub‑component: `const [broken, setBroken] = useState(false)`; if `!url || broken` render `<span className="totem-carousel-initials" aria-hidden="true">{professionalInitials(name)}</span>`; else `<img src={url} alt="" onError={() => setBroken(true)} loading="lazy" />`.
- `statusLabel`: `AVAILABLE→'Disponível'`, `IN_SERVICE→'Em atendimento'`, `UNAVAILABLE→'Indisponível'`. `statusClass`: `→'totem-status-ok'|'totem-status-busy'|'totem-status-muted'`.
- `onKeyDown`: `ArrowRight`/`ArrowLeft` → `setActive(±1)`; `Home`→`setActive(0)`; `End`→`setActive(len-1)`; prevent default for those.
- `onPointerDown/Move/Up`: only when `pointerType === 'mouse'`; on down set `pointerActiveRef=true`, `dragStartXRef=clientX`, `didDragRef=false`; on move (if active) `viewport.scrollLeft -= (clientX - dragStartXRef); dragStartXRef=clientX; if(|Δtotal|>6) didDragRef=true`; on up clear `pointerActiveRef`.
- `onScroll`: `requestAnimationFrame` → compute the card whose center is nearest the viewport center; if it differs from `activeIndex`, `setActiveIndex` + `onActiveChange` (do **not** re‑scroll here — avoids a feedback loop).

Append `.totem-carousel-*` and `.totem-status-*` CSS (scroll‑snap viewport, hidden scrollbar, `padding-inline` for edge centering, `.is-active { transform: scale(1.06) }`, arrows ≥56px, dots, `.totem-status-ok{color:#3E9B6B} .totem-status-busy{color:#C98A2B} .totem-status-muted{color:#888}` with a matching `<i>` dot; `@media (prefers-reduced-motion: reduce)` disables smooth behaviours).

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/features/totem/TotemProfessionalCarousel.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/features/totem/TotemProfessionalCarousel.tsx src/features/totem/TotemProfessionalCarousel.test.tsx src/styles.css
git commit -m "feat(totem): real swipe/drag professional carousel (snap, arrows, dots, keyboard)"
```

---

## Task 9: Frontend — `/totem/profissionais` screen (`TotemProfessionals`)

**Files:**
- Create: `src/pages/TotemProfessionals.tsx` + `.test.tsx`
- Modify: `src/App.tsx`, `src/dev/DevelopmentApp.tsx` (route `/totem/profissionais`)
- Modify: `src/frontend-portals.test.ts` (add the `/totem/profissionais` assertion)
- Modify: `src/styles.css` (append `.totem-professionals-*` if needed)

**Interfaces:**
- Consumes: `totemApi.professionals` (Task 4); `<TotemProfessionalCarousel />` (Task 8); `LightRays`, `BlurFade`, `RippleButton` (Task 6); `KioskClock` (existing).
- Produces: `<TotemProfessionals />` at `/totem/profissionais`.

- [ ] **Step 1: Write the failing test**

`src/pages/TotemProfessionals.test.tsx`:
```tsx
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { totemApi } from '../api/modules'
import { TotemProfessionals } from './TotemProfessionals'

vi.mock('../api/modules', async (orig) => ({
  ...(await orig<typeof import('../api/modules')>()),
  totemApi: { professionals: vi.fn() },
}))
vi.stubGlobal('matchMedia', (q: string) => ({
  matches: false, media: q, addEventListener: vi.fn(), removeEventListener: vi.fn(),
  addListener: vi.fn(), removeListener: vi.fn(), onchange: null, dispatchEvent: vi.fn(),
}))
beforeEach(() => {
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', { value: vi.fn(), writable: true })
  Object.defineProperty(HTMLElement.prototype, 'scrollBy', { value: vi.fn(), writable: true })
})
afterEach(() => vi.clearAllMocks())

const people = [
  { id: 'a', name: 'Ana Souza', profession: 'Fisio', photoUrl: null, status: 'AVAILABLE' as const },
  { id: 'b', name: 'Bruno Lima', profession: 'Psi', photoUrl: null, status: 'UNAVAILABLE' as const },
]
const renderAt = () => render(
  <MemoryRouter initialEntries={['/totem/profissionais']}>
    <Routes>
      <Route path="/totem/profissionais" element={<TotemProfessionals />} />
      <Route path="/totem" element={<div>ENTRY</div>} />
      <Route path="/totem/check-in" element={<div>CHECKIN</div>} />
      <Route path="/cliente/agendar" element={<Dest />} />
    </Routes>
  </MemoryRouter>,
)
function Dest() {
  const p = new URLSearchParams(window.location.search)
  return <div>AGENDAR professionalId={p.get('professionalId')}</div>
}

test('loading shows the kiosk layout with skeleton cards', () => {
  vi.mocked(totemApi.professionals).mockReturnValue(new Promise(() => {}))
  renderAt()
  expect(screen.getByRole('heading', { name: /escolha o profissional/i })).toBeInTheDocument()
  expect(screen.getAllByTestId('totem-skeleton-card').length).toBeGreaterThan(0)
})

test('success -> carousel + Continuar (uses the active professional id) + Voltar', async () => {
  vi.mocked(totemApi.professionals).mockResolvedValue(people)
  renderAt()
  await screen.findByRole('listbox')
  screen.getByRole('button', { name: /continuar/i }).click()
  await waitFor(() => expect(screen.getByText('AGENDAR professionalId=a')).toBeInTheDocument())
})

test('Voltar goes to /totem', async () => {
  vi.mocked(totemApi.professionals).mockResolvedValue(people)
  renderAt()
  await screen.findByRole('listbox')
  screen.getByRole('button', { name: /voltar/i }).click()
  expect(screen.getByText('ENTRY')).toBeInTheDocument()
})

test('empty -> message + Tentar novamente + Tenho código', async () => {
  vi.mocked(totemApi.professionals).mockResolvedValue([])
  renderAt()
  expect(await screen.findByText('Nenhum profissional disponível.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /tentar novamente/i })).toBeInTheDocument()
  screen.getByRole('button', { name: /tenho código/i }).click()
  expect(screen.getByText('CHECKIN')).toBeInTheDocument()
})

test('error -> message + Tentar novamente (refetches) + Tenho código', async () => {
  vi.mocked(totemApi.professionals).mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(people)
  renderAt()
  expect(await screen.findByText('Não foi possível carregar os profissionais.')).toBeInTheDocument()
  screen.getByRole('button', { name: /tentar novamente/i }).click()
  await screen.findByRole('listbox')
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/pages/TotemProfessionals.test.tsx`
Expected: FAIL — component missing.

- [ ] **Step 3: Implement**

`src/pages/TotemProfessionals.tsx`:
- `type Phase = 'loading' | 'ready' | 'empty' | 'error'`. `professionals`, `active` (`TotemProfessionalCardDto | null`).
- `load()` — `setPhase('loading')`; `totemApi.professionals(signal)` → `list.length ? (setProfessionals(list), setPhase('ready')) : setPhase('empty')`; `.catch` (ignore `AbortError`) → `setPhase('error')`. Call in `useEffect` on mount with an `AbortController`.
- Layout: top bar (`LUMIS` left, `<KioskClock />` right) + `<LightRays />` + centered content in a `<BlurFade>`: `<h1>Escolha o profissional</h1>`.
  - `loading` → 3× `<div data-testid="totem-skeleton-card" className="totem-skeleton-card" />` in the carousel slot.
  - `ready` → `<TotemProfessionalCarousel professionals={professionals} onActiveChange={setActive} />`, then `<RippleButton className="totem-continue" disabled={!active} onClick={() => active && navigate('/cliente/agendar?professionalId=' + active.id)}>Continuar →</RippleButton>`, then `<button className="totem-back" onClick={() => navigate('/totem')}>← Voltar</button>`.
  - `empty` → `<p>Nenhum profissional disponível.</p>` + `<button onClick={load}>Tentar novamente</button>` + `<button onClick={() => navigate('/totem/check-in')}>Tenho código</button>`.
  - `error` → `<p>Não foi possível carregar os profissionais.</p>` + same two buttons.
- Route: add `<Route path="/totem/profissionais" element={<TotemProfessionals />} />` to **both** `App.tsx` and `DevelopmentApp.tsx` (+ import).
- `frontend-portals.test.ts`: add to the loop
  `expect(source).toContain('<Route path="/totem/profissionais" element={<TotemProfessionals />} />')`.

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/pages/TotemProfessionals.test.tsx src/frontend-portals.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/pages/TotemProfessionals.tsx src/pages/TotemProfessionals.test.tsx src/App.tsx src/dev/DevelopmentApp.tsx src/frontend-portals.test.ts src/styles.css
git commit -m "feat(totem): /totem/profissionais screen (carousel + Continuar + loading/empty/error)"
```

---

## Task 10: Frontend — `/totem/check-in` returns to `/totem`

**Files:**
- Modify: `src/pages/TotemCheckIn.tsx` + `src/pages/TotemCheckIn.test.tsx`

**Interfaces:**
- Consumes: `LightRays` (Task 6); `useNavigate` (existing).
- Produces: unchanged public behaviour except navigation targets.

- [ ] **Step 1: Extend the test (RED)**

Add to `src/pages/TotemCheckIn.test.tsx` (keep every existing test):
```tsx
import { MemoryRouter, Route, Routes } from 'react-router-dom'
// helper that also mounts a /totem sink
const renderWithTotem = () => render(
  <MemoryRouter initialEntries={['/totem/check-in']}>
    <Routes>
      <Route path="/totem/check-in" element={<TotemCheckIn />} />
      <Route path="/totem" element={<div>ENTRY</div>} />
    </Routes>
  </MemoryRouter>,
)

test('the discrete Voltar control returns to /totem', () => {
  renderWithTotem()
  screen.getByRole('button', { name: /voltar/i }).click()
  expect(screen.getByText('ENTRY')).toBeInTheDocument()
})

test('after a successful check-in the kiosk returns to /totem (auto + Concluir)', async () => {
  vi.useFakeTimers()
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(true))
  renderWithTotem()
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  fireEvent.change(screen.getByLabelText(/código do qr code/i), { target: { value: 'MAN-Z' } })
  fireEvent.click(screen.getByRole('button', { name: /validar agendamento/i }))
  await act(async () => { await vi.advanceTimersByTimeAsync(0) })
  fireEvent.click(screen.getByRole('button', { name: /confirmar chegada/i }))
  await act(async () => { await vi.advanceTimersByTimeAsync(0) })
  expect(screen.getByRole('dialog')).toBeInTheDocument()
  await act(async () => { await vi.advanceTimersByTimeAsync(12_000) })
  expect(screen.getByText('ENTRY')).toBeInTheDocument()   // auto-return landed on /totem
})
```
Adjust the existing "returns itself to the start" test's expectation: after `advanceTimersByTimeAsync(12_000)` it now lands on `/totem` (assert `ENTRY`), not the scan tab.

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/pages/TotemCheckIn.test.tsx`
Expected: FAIL — no Voltar; auto-return still calls `reset()` (stays on check-in).

- [ ] **Step 3: Implement (minimal, preserve all logic)**

In `src/pages/TotemCheckIn.tsx`:
- `import { useNavigate } from 'react-router-dom'` and `import { LightRays } from '../features/totem/magic/LightRays'`; `const navigate = useNavigate()`.
- Add `const goHome = useCallback(() => { reset(); navigate('/totem') }, [reset, navigate])`.
- Auto‑return effect: change `window.setTimeout(reset, AUTO_RESET_MS)` → `window.setTimeout(goHome, AUTO_RESET_MS)`; deps `[confirmed, goHome]`.
- Modal "Concluir" button `onClick={goHome}` (and `<Modal onClose={goHome}>`).
- Add a discrete `<button type="button" className="totem-back" aria-label="Voltar" onClick={goHome}>← Voltar</button>` in the shell (top-left of the aside or stage).
- Replace the ad-hoc beam `<div className="totem-beam" />` with `<LightRays />`.
- Trim the aside copy: remove the `totem-eyebrow` "Bem-vindo", the `<h1>` headline and the support `<p>` from `.totem-aside`; keep only `<img … alt="LUMIS">` + `<KioskClock />`. The main stage keeps `Confirme sua chegada`, the two option tabs with their descriptions, and the camera-status / input-placeholder lines only.
- Do **not** change `useQrScanner`, `resolveToken`, `registerArrival`, `errorFor`, the segment/fallback effects, or `AUTO_RESET_MS`.

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/pages/TotemCheckIn.test.tsx`
Expected: PASS (all existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add src/pages/TotemCheckIn.tsx src/pages/TotemCheckIn.test.tsx
git commit -m "feat(totem): check-in returns to /totem (Voltar + auto-return), trimmed copy"
```

---

## Task 11: Frontend — carry `professionalId` through login/register into booking

**Files:**
- Modify: `src/components/ProtectedRoute.tsx` + `.test.tsx`
- Modify: `src/pages/Login.tsx` + `src/pages/Login.test.tsx`
- Modify: `src/pages/customer/CustomerRegister.tsx` + `.test.tsx` (create test if absent)
- Modify: `src/pages/customer/CustomerBooking.tsx` + `.test.tsx` (create test if absent)

**Interfaces:**
- Consumes: `safeCustomerReturnUrl` (Task 5); existing `useSession`, `useNavigate`, `useSearchParams`.
- Produces: no new exports; behaviour changes only.

- [ ] **Step 1: Write failing tests**

`src/components/ProtectedRoute.test.tsx` — add:
```tsx
test('anonymous on a /cliente route redirects to /cliente/login with an encoded returnUrl', async () => {
  // session mock -> status 'anonymous'; render <ProtectedRoute allowedRoles={['CUSTOMER']}/> at
  // /cliente/agendar?professionalId=abc inside a MemoryRouter with a /cliente/login sink that
  // echoes location.search.
  // expect the sink to show returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dabc
})
test('anonymous on a staff route redirects to /login WITHOUT returnUrl', async () => { /* ... */ })
```
`src/pages/Login.test.tsx` — add:
```tsx
test('customer login with a safe returnUrl navigates there', async () => {/* ?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx -> after login, route is /cliente/agendar?professionalId=x */})
test('customer login ignores an unsafe returnUrl and uses homeForRoles', async () => {/* ?returnUrl=//evil -> /cliente */})
test('mustChangePassword wins over returnUrl', async () => {/* -> /change-password */})
test('admin login ignores returnUrl entirely', async () => {/* audience=admin -> homeForRoles */})
test('the "Criar minha conta" link carries an encoded returnUrl when present', () => {/* href = /cliente/cadastro?returnUrl=... */})
```
`src/pages/customer/CustomerRegister.test.tsx`:
```tsx
test('propagates a safe returnUrl into the post-register /cliente/login navigation', async () => {/* mock customerApi.register resolved; ?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx -> navigates to /cliente/login?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx */})
```
`src/pages/customer/CustomerBooking.test.tsx`:
```tsx
test('preselects the professional from ?professionalId when it exists', async () => {/* mock customerApi.professionals -> [p1,p2]; mount at /cliente/agendar?professionalId=p2 -> the select value is p2 */})
test('falls back to the first professional when ?professionalId is unknown/absent', async () => {/* -> p1 */})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/components/ProtectedRoute.test.tsx src/pages/Login.test.tsx src/pages/customer/CustomerRegister.test.tsx src/pages/customer/CustomerBooking.test.tsx`
Expected: FAIL on the new cases.

- [ ] **Step 3: Implement (all minimal, per spec §10.2–§10.5)**

- `ProtectedRoute.tsx` anonymous branch:
  ```tsx
  const base = location.pathname.startsWith('/cliente') ? '/cliente/login' : '/login'
  const to = base === '/cliente/login'
    ? `${base}?returnUrl=${encodeURIComponent(location.pathname + location.search)}`
    : base
  return <Navigate to={to} state={{ from: location.pathname }} replace />
  ```
  Keep the literal ternary substring intact; do not touch the `validatedOnce` latch.
- `Login.tsx`:
  ```tsx
  const [params] = useSearchParams()
  const returnUrl = audience === 'customer' ? safeCustomerReturnUrl(params.get('returnUrl')) : null
  // after login():
  navigate(current.mustChangePassword ? '/change-password'
    : returnUrl ?? homeForRoles(current.roles), { replace: true })
  ```
  "Continuar na minha área" button → same `returnUrl ?? homeForRoles(...)`. Register link:
  `to={\`/cliente/cadastro${returnUrl ? \`?returnUrl=\${encodeURIComponent(returnUrl)}\` : ''}\`}` (switch the `<a href>` to `<Link>` if needed for query building).
- `CustomerRegister.tsx`:
  ```tsx
  const [params] = useSearchParams()
  const returnUrl = safeCustomerReturnUrl(params.get('returnUrl'))
  // after register:
  navigate(`/cliente/login${returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : ''}`,
    { replace: true, state: { registered: true } })
  ```
- `CustomerBooking.tsx`: `const [params] = useSearchParams()`; in the professionals‑load `.then`:
  ```tsx
  const preselect = params.get('professionalId')
  setProfessionalId(items.some(i => i.id === preselect) ? preselect! : (items[0]?.id ?? ''))
  ```

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/components/ProtectedRoute.test.tsx src/pages/Login.test.tsx src/pages/customer/CustomerRegister.test.tsx src/pages/customer/CustomerBooking.test.tsx`
Expected: PASS (existing + new).

- [ ] **Step 5: Commit**

```bash
git add src/components/ProtectedRoute.tsx src/components/ProtectedRoute.test.tsx src/pages/Login.tsx src/pages/Login.test.tsx src/pages/customer/CustomerRegister.tsx src/pages/customer/CustomerRegister.test.tsx src/pages/customer/CustomerBooking.tsx src/pages/customer/CustomerBooking.test.tsx
git commit -m "feat(customer): carry professionalId through login/register via strict returnUrl"
```

---

## Task 12: Domain — `ManualCheckInCode` value object

**Files:**
- Create: `src/GestaoPredio.Domain/Customers/ManualCheckInCode.cs`
- Test: `tests/GestaoPredio.UnitTests/ManualCheckInCodeTests.cs`

**Interfaces:**
- Consumes: `System.Security.Cryptography.RandomNumberGenerator`.
- Produces: `readonly struct ManualCheckInCode` with `string Value`, `static ManualCheckInCode Generate()`, `static bool TryParse(string?, out ManualCheckInCode)`, `override string ToString()`. **No `Hash()`** — keyed hashing is `IManualCheckInCodeHasher` (Task 13).

- [ ] **Step 1: Write the failing tests**

`tests/GestaoPredio.UnitTests/ManualCheckInCodeTests.cs`:
```csharp
using System.Security.Cryptography;
using GestaoPredio.Domain.Customers;
using Xunit;

namespace GestaoPredio.UnitTests;

public sealed class ManualCheckInCodeTests
{
    [Fact]
    public void Generate_yields_exactly_six_digits()
    {
        for (var i = 0; i < 10_000; i++)
        {
            var code = ManualCheckInCode.Generate().Value;
            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.InRange(c, '0', '9'));
        }
    }

    [Fact]
    public void Generate_preserves_leading_zeros_and_has_reasonable_spread()
    {
        var values = new HashSet<string>();
        var anyLeadingZero = false;
        for (var i = 0; i < 10_000; i++)
        {
            var v = ManualCheckInCode.Generate().Value;
            values.Add(v);
            anyLeadingZero |= v[0] == '0';
        }
        Assert.True(anyLeadingZero, "expected at least one code starting with 0 in 10k draws");
        Assert.True(values.Count > 9_000, "RNG spread sanity");
    }

    [Theory]
    [InlineData("482731", true, "482731")]
    [InlineData("004821", true, "004821")]
    [InlineData(" 482731 ", true, "482731")]
    [InlineData("48273", false, null)]
    [InlineData("4827311", false, null)]
    [InlineData("48a731", false, null)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    public void TryParse_accepts_only_six_ascii_digits(string? raw, bool ok, string? expected)
    {
        var parsed = ManualCheckInCode.TryParse(raw, out var code);
        Assert.Equal(ok, parsed);
        if (ok) Assert.Equal(expected, code.Value);
    }

    [Fact]
    public void Struct_has_no_hashing_member()
    {
        // Guard: hashing is keyed (IManualCheckInCodeHasher). The value object must not
        // expose a Hash()/GetHash()-style method taking no args and returning byte[].
        var offenders = typeof(ManualCheckInCode).GetMethods()
            .Where(m => m.ReturnType == typeof(byte[]) && m.GetParameters().Length == 0);
        Assert.Empty(offenders);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/GestaoPredio.UnitTests --filter ManualCheckInCodeTests`
Expected: FAIL — type missing.

- [ ] **Step 3: Implement** (spec §7A.3)

`src/GestaoPredio.Domain/Customers/ManualCheckInCode.cs`:
```csharp
using System.Globalization;
using System.Security.Cryptography;

namespace GestaoPredio.Domain.Customers;

/// <summary>
/// The 6-digit manual check-in code. A string (leading zeros matter), never an int.
/// Pure value object: generate + validate only. The plaintext lives only in the issue
/// response; the persisted form is a keyed hash produced by IManualCheckInCodeHasher.
/// </summary>
public readonly struct ManualCheckInCode
{
    public string Value { get; }
    private ManualCheckInCode(string value) => Value = value;

    public static ManualCheckInCode Generate() =>
        new(RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture));

    public static bool TryParse(string? raw, out ManualCheckInCode code)
    {
        code = default;
        var t = raw?.Trim() ?? string.Empty;
        if (t.Length != 6) return false;
        foreach (var c in t) if (c is < '0' or > '9') return false;
        code = new ManualCheckInCode(t);
        return true;
    }

    public override string ToString() => Value;
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/GestaoPredio.UnitTests --filter ManualCheckInCodeTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/GestaoPredio.Domain/Customers/ManualCheckInCode.cs tests/GestaoPredio.UnitTests/ManualCheckInCodeTests.cs
git commit -m "feat(checkin): ManualCheckInCode pure value object (6-digit, CSPRNG, no hashing)"
```

---

## Task 13: Keyed HMAC hasher + transient `CheckInToken.ManualCodeHash` + migration

One reviewable unit: the hashing & persistence substrate the manual code needs. Two RED/GREEN cycles (hasher, then entity+config+migration).

**Files:**
- Create: `src/GestaoPredio.Application/Customers/IManualCheckInCodeHasher.cs`
- Create: `src/GestaoPredio.Application/Customers/ManualCheckInCodeHashingOptions.cs`
- Create: `src/GestaoPredio.Infrastructure/Customers/HmacManualCheckInCodeHasher.cs`
- Create: `tests/GestaoPredio.UnitTests/HmacManualCheckInCodeHasherTests.cs`
- Modify: `src/GestaoPredio.Domain/Customers/CheckInToken.cs`
- Modify: `src/GestaoPredio.Infrastructure/Persistence/Configurations/CheckInTokenConfiguration.cs`
- Modify: the composition root where feature services are registered (DI for the hasher + options) — **services only, not the CSP line**.
- Create: `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/<timestamp>_CheckInManualCode.cs` (via `dotnet ef migrations add` — **not applied**)
- Modify: `tests/GestaoPredio.UnitTests/CheckInTokenTests.cs` (new signatures + transient-lifecycle assertions)

**Interfaces:**
- Consumes: `IOptions<ManualCheckInCodeHashingOptions>`, `IHostEnvironment`.
- Produces: `IManualCheckInCodeHasher.Hash(ManualCheckInCode) : byte[]` (32); `ManualCheckInCodeHashingOptions { const string SectionName = "CheckIn"; string ManualCodeHmacKey }`; `HmacManualCheckInCodeHasher` (Infra impl). `CheckInToken.ManualCodeHash : byte[]?`; `Create`/`Rotate` take `manualCodeHash`; `MarkUsed`/`Revoke` null it; `ClearManualCode()`.

### 13a — keyed hasher (spec §7A.3b, §7A.11)

- [ ] **Step 1: Write the failing tests**

`tests/GestaoPredio.UnitTests/HmacManualCheckInCodeHasherTests.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using GestaoPredio.Application.Customers;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Infrastructure.Customers;
using Microsoft.Extensions.Options;
using Xunit;

namespace GestaoPredio.UnitTests;

public sealed class HmacManualCheckInCodeHasherTests
{
    private sealed class Env(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
    private static HmacManualCheckInCodeHasher Make(string key, string env = "Development") =>
        new(Options.Create(new ManualCheckInCodeHashingOptions { ManualCodeHmacKey = key }), new Env(env));
    private static ManualCheckInCode Code(string v) { ManualCheckInCode.TryParse(v, out var c); return c; }

    [Fact]
    public void Deterministic_for_same_key_and_code()
    {
        var h = Make("k-abc-123");
        Assert.Equal(32, h.Hash(Code("482731")).Length);
        Assert.True(h.Hash(Code("482731")).AsSpan().SequenceEqual(h.Hash(Code("482731"))));
    }

    [Fact]
    public void Different_key_yields_different_hash()
    {
        Assert.False(Make("key-A").Hash(Code("482731")).AsSpan()
            .SequenceEqual(Make("key-B").Hash(Code("482731"))));
    }

    [Fact]
    public void Not_equal_to_plain_sha256()
    {
        var hmac = Make("some-key").Hash(Code("482731"));
        var sha = SHA256.HashData(Encoding.ASCII.GetBytes("482731"));
        Assert.False(hmac.AsSpan().SequenceEqual(sha));
    }

    [Fact]
    public void Leading_zeros_change_the_hash()
    {
        var h = Make("k");
        Assert.False(h.Hash(Code("004821")).AsSpan().SequenceEqual(h.Hash(Code("048210"))));
    }

    [Fact]
    public void Missing_key_in_production_fails_closed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Make("", "Production"));
        Assert.DoesNotContain("dev-only", ex.Message); // no secret material in the message
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Missing_key_outside_production_uses_explicit_dev_fallback(string env)
    {
        var h = Make("", env);
        Assert.Equal(32, h.Hash(Code("000000")).Length); // does not throw
    }
}
```

- [ ] **Step 2: Run → FAIL** — `dotnet test tests/GestaoPredio.UnitTests --filter HmacManualCheckInCodeHasherTests` (types missing).

- [ ] **Step 3: Implement** (spec §7A.3b)

`IManualCheckInCodeHasher.cs` (Application): `byte[] Hash(ManualCheckInCode code);`
`ManualCheckInCodeHashingOptions.cs` (Application): `const string SectionName = "CheckIn"; public string ManualCodeHmacKey { get; set; } = "";`
`HmacManualCheckInCodeHasher.cs` (Infrastructure) — exactly as spec §7A.3b: ctor takes `IOptions<ManualCheckInCodeHashingOptions>` + `IHostEnvironment`; empty key + `env.IsProduction()` → `throw new InvalidOperationException("CheckIn:ManualCodeHmacKey ausente...")` (no secret in the message); empty key + non-Production → the explicit `dev-only-...` constant; `Hash` = `HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(code.Value))`.
DI (composition root): `services.Configure<ManualCheckInCodeHashingOptions>(config.GetSection(ManualCheckInCodeHashingOptions.SectionName));` + `services.AddSingleton<IManualCheckInCodeHasher, HmacManualCheckInCodeHasher>();`

- [ ] **Step 4: Run → PASS.**

### 13b — transient `ManualCodeHash` on `CheckInToken` + EF + migration

- [ ] **Step 5: Rewrite the failing test**

`tests/GestaoPredio.UnitTests/CheckInTokenTests.cs`:
```csharp
using System.Security.Cryptography;
using GestaoPredio.Domain.Customers;
using Xunit;

namespace GestaoPredio.UnitTests;

public sealed class CheckInTokenTests
{
    private static byte[] H() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Create_requires_32_byte_hashes()
    {
        var now = DateTimeOffset.UtcNow;
        var t = CheckInToken.Create(Guid.NewGuid(), H(), H(), now, now.AddHours(1));
        Assert.Equal(32, t.TokenHash.Length);
        Assert.Equal(32, t.ManualCodeHash!.Length);
        Assert.Throws<ArgumentException>(() => CheckInToken.Create(Guid.NewGuid(), new byte[31], H(), now, now.AddHours(1)));
        Assert.Throws<ArgumentException>(() => CheckInToken.Create(Guid.NewGuid(), H(), new byte[10], now, now.AddHours(1)));
    }

    [Fact]
    public void MarkUsed_and_Revoke_null_the_manual_code_hash_but_keep_the_token_hash()
    {
        var now = DateTimeOffset.UtcNow;
        var a = CheckInToken.Create(Guid.NewGuid(), H(), H(), now, now.AddHours(1));
        a.MarkUsed(now.AddMinutes(1));
        Assert.NotNull(a.UsedAt);
        Assert.Null(a.ManualCodeHash);
        Assert.Equal(32, a.TokenHash.Length);

        var b = CheckInToken.Create(Guid.NewGuid(), H(), H(), now, now.AddHours(1));
        b.Revoke(now.AddMinutes(1));
        Assert.NotNull(b.RevokedAt);
        Assert.Null(b.ManualCodeHash);
        Assert.Equal(32, b.TokenHash.Length);
    }

    [Fact]
    public void ClearManualCode_frees_the_hash_without_touching_used_or_revoked()
    {
        var now = DateTimeOffset.UtcNow;
        var t = CheckInToken.Create(Guid.NewGuid(), H(), H(), now, now.AddHours(1));
        t.ClearManualCode();
        Assert.Null(t.ManualCodeHash);
        Assert.Null(t.UsedAt);
        Assert.Null(t.RevokedAt);
    }

    [Fact]
    public void Rotate_replaces_both_hashes_and_clears_state()
    {
        var now = DateTimeOffset.UtcNow;
        var t = CheckInToken.Create(Guid.NewGuid(), H(), H(), now, now.AddHours(1));
        t.MarkUsed(now); t.Revoke(now);
        var t2 = H(); var m2 = H();
        t.Rotate(t2, m2, now.AddMinutes(5), now.AddHours(2));
        Assert.Equal(t2, t.TokenHash);
        Assert.Equal(m2, t.ManualCodeHash);
        Assert.Null(t.UsedAt);
        Assert.Null(t.RevokedAt);
    }
}
```

- [ ] **Step 6: Run → FAIL** — arity + `ManualCodeHash`/`ClearManualCode` missing; `MarkUsed`/`Revoke` don't null the hash yet.

- [ ] **Step 7: Implement**

`CheckInToken.cs`: add `public byte[]? ManualCodeHash { get; private set; }`. `Create(Guid, byte[] tokenHash, byte[] manualCodeHash, DateTimeOffset, DateTimeOffset)` and `Rotate(byte[] tokenHash, byte[] manualCodeHash, DateTimeOffset, DateTimeOffset)` — validate both hashes are 32 bytes; `Rotate` sets both, nulls `RevokedAt`/`UsedAt`. `MarkUsed(at)` and `Revoke(at)` additionally set `ManualCodeHash = null`. New `public void ClearManualCode() => ManualCodeHash = null;` (does not touch `UsedAt`/`RevokedAt`). `TokenHash` never nulled.

`CheckInTokenConfiguration.cs`:
```csharp
entity.Property(x => x.ManualCodeHash).HasColumnType("bytea");
entity.HasIndex(x => x.ManualCodeHash)
    .IsUnique()
    .HasFilter("\"ManualCodeHash\" IS NOT NULL")
    .HasDatabaseName("UX_CheckInTokens_ManualCodeHash");
```

Generate the migration (**do not apply**):
```bash
dotnet ef migrations add CheckInManualCode -p src/GestaoPredio.Infrastructure -s recepcaototem
```
Verify `Up()` = nullable `bytea` column + partial unique index; `Down()` drops both (spec §26). `dotnet build` clean; model snapshot updated.

- [ ] **Step 8: Run → PASS** — `dotnet test tests/GestaoPredio.UnitTests --filter "CheckInTokenTests|HmacManualCheckInCodeHasherTests"` + `dotnet build`. (Integration suite runs in Task 15/16.)

- [ ] **Step 9: Commit**

```bash
git add src/GestaoPredio.Application/Customers/ src/GestaoPredio.Infrastructure/Customers/ src/GestaoPredio.Domain/Customers/CheckInToken.cs src/GestaoPredio.Infrastructure/Persistence/Configurations/CheckInTokenConfiguration.cs "src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/" tests/GestaoPredio.UnitTests/CheckInTokenTests.cs tests/GestaoPredio.UnitTests/HmacManualCheckInCodeHasherTests.cs
git commit -m "feat(checkin): keyed HMAC hasher + transient CheckInToken.ManualCodeHash (migration, not applied)"
```

---

## Task 14: Issue — `IssueToken` returns the manual code (collision loop + lazy reclaim)

**Files:**
- Modify: `recepcaototem/Features/Customers/CustomerSchedulingEndpoints.cs` (`IssueToken`)
- Modify: the composition root (register `IManualCodeSource` → `DefaultManualCodeSource`)
- Test: `tests/GestaoPredio.IntegrationTests/CheckInManualCodeTests.cs` (new — issue cases)

**Interfaces:**
- Consumes: `ManualCheckInCode` (Task 12); `IManualCheckInCodeHasher`, `CheckInToken.Create/Rotate` new arity, `ClearManualCode()`, transient `MarkUsed`/`Revoke` (Task 13).
- Produces: `POST /api/customer/reservations/{id}/check-in-token` → `{ token: string, manualCode: string, expiresAt: string }`; `503 CHECK_IN_CODE_UNAVAILABLE` on retry exhaustion. `internal interface IManualCodeSource { ManualCheckInCode Next(); }` + `DefaultManualCodeSource`.

- [ ] **Step 1: Write the failing tests**

`tests/GestaoPredio.IntegrationTests/CheckInManualCodeTests.cs` (issue block):
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed partial class CheckInManualCodeTests(ModulesApiFactory factory)
{
    private sealed record Issue(string Token, string ManualCode, DateTimeOffset ExpiresAt);

    [Fact]
    public async Task Issue_returns_token_manualCode_and_expiry_and_persists_only_the_keyed_hash()
    {
        var ctx = await factory.SeedEligibleReservationAsync();     // helper: customer + approved reservation inside the check-in window
        var issue = await ctx.IssueAsync();
        Assert.Matches(new Regex("^\\d{6}$"), issue.ManualCode);
        Assert.NotEqual(issue.ManualCode, issue.Token);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IManualCheckInCodeHasher>();
        ManualCheckInCode.TryParse(issue.ManualCode, out var code);
        var row = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx.ReservationId);
        Assert.Equal(hasher.Hash(code), row.ManualCodeHash);                                  // keyed HMAC (test key)
        Assert.NotEqual(SHA256.HashData(Encoding.ASCII.GetBytes(issue.ManualCode)), row.ManualCodeHash); // not plain SHA-256
        Assert.Equal(32, row.ManualCodeHash!.Length);
    }

    [Fact]
    public async Task Reissue_rotates_both_representations()
    {
        var ctx = await factory.SeedEligibleReservationAsync();
        var first = await ctx.IssueAsync();
        var second = await ctx.IssueAsync();
        Assert.NotEqual(first.ManualCode, second.ManualCode);
        Assert.NotEqual(first.Token, second.Token);
    }
}
```
> `SeedEligibleReservationAsync` / `IssueAsync` are small helpers on `ModulesApiFactory` (or a local fixture) that create a customer, log them in, create an approved reservation whose window is open "now", and POST the issue endpoint. Model on `CustomerApiTests.cs`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter CheckInManualCodeTests`
Expected: FAIL — response has no `manualCode`; `ManualCodeHash` null.

- [ ] **Step 3: Implement** (spec §7A.5, §7A.7)

First add the RNG seam (so Task 16 can script it): `internal interface IManualCodeSource { ManualCheckInCode Next(); }` + `internal sealed class DefaultManualCodeSource : IManualCodeSource { public ManualCheckInCode Next() => ManualCheckInCode.Generate(); }`, registered `services.AddSingleton<IManualCodeSource, DefaultManualCodeSource>()` where the other feature services are registered. `IssueToken` takes `IManualCodeSource codes` **and** `IManualCheckInCodeHasher hasher` (Task 13) as parameters.

`IssueToken` runs inside its existing transaction. After computing the strong token, generate the manual code with the bounded loop **and lazy reclaim** of stale colliding rows, then `Create`/`Rotate`:
```csharp
var raw = RandomNumberGenerator.GetBytes(32);
var tokenHash = SHA256.HashData(raw);

ManualCheckInCode code = default;
byte[] manualHash = [];
const int maxAttempts = 5;

for (var attempt = 1; ; attempt++)
{
    code = codes.Next();                       // IManualCodeSource (default: CSPRNG)
    manualHash = hasher.Hash(code);            // IManualCheckInCodeHasher (HMAC-SHA-256, keyed)

    // A row (of ANOTHER reservation) already holds this hash?
    var clash = await db.CheckInTokens
        .SingleOrDefaultAsync(x => x.ReservationId != id && x.ManualCodeHash == manualHash, ct);
    if (clash is null) break;                  // free -> use it

    var resolvable = clash.RevokedAt == null && clash.UsedAt == null && clash.ExpiresAt > now;
    if (!resolvable) { clash.ClearManualCode(); break; }  // lazy reclaim; persisted in this same tx

    if (attempt >= maxAttempts)
    {
        db.AuditEntries.Add(/* CHECK_IN_TOKEN_ISSUE_FAILED, RESERVATION target, NO value / NO hash */);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Json(new ApiError("CHECK_IN_CODE_UNAVAILABLE",
            "Não foi possível gerar o código agora. Tente novamente."), statusCode: 503);
    }
}

var token = await db.CheckInTokens.SingleOrDefaultAsync(x => x.ReservationId == id, ct);
if (token is null) db.CheckInTokens.Add(token = CheckInToken.Create(id, tokenHash, manualHash, now, reservation.EndAt));
else token.Rotate(tokenHash, manualHash, now, reservation.EndAt);

// audit CHECK_IN_TOKEN_ISSUED unchanged (no value)
try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
catch (DbUpdateException)   // rare unique-violation race on UX_CheckInTokens_ManualCodeHash
{
    await transaction.RollbackAsync(ct);
    // caller retries the whole IssueToken up to maxAttempts total, then 503 (structure the
    // handler so this outer retry and the inner attempt counter share the same budget).
}
return Results.Ok(new { token = WebEncoders.Base64UrlEncode(raw), manualCode = code.Value, expiresAt = reservation.EndAt });
```
> Structure the handler so the inner loop attempts + a caught `DbUpdateException` retry share **one** budget of 5, ending in the same `503`. Keep the existing eligibility gate and `CHECK_IN_TOKEN_ISSUED` audit exactly as they are. Never put `code.Value` or `manualHash` in the audit or any log.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter CheckInManualCodeTests`
Expected: PASS (issue cases).

- [ ] **Step 5: Commit**

```bash
git add recepcaototem/Features/Customers/CustomerSchedulingEndpoints.cs tests/GestaoPredio.IntegrationTests/CheckInManualCodeTests.cs
git commit -m "feat(checkin): issue endpoint returns a 6-digit manualCode (collision loop, bounded retry)"
```

---

## Task 15: Resolve/Confirm — dispatch by shape, one eligibility rule

**Files:**
- Modify: `recepcaototem/Features/Totem/TotemEndpoints.cs` (`FindCheckIn`, `ResolveCheckIn`, `ConfirmCheckIn`)
- Test: `tests/GestaoPredio.IntegrationTests/CheckInManualCodeTests.cs` (resolve/confirm + QR↔manual + errors)

**Interfaces:**
- Consumes: `ManualCheckInCode.TryParse` (Task 12); `IManualCheckInCodeHasher.Hash` + `CheckInToken.ManualCodeHash` / transient lifecycle (Task 13); `IManualCodeSource` + `IssueWithScriptedCodeAsync` / `SeedIssuedThenExpiredAsync` fixture helpers (Tasks 14/16); the issue endpoint (Task 14).
- Produces: `POST /api/totem/check-in/resolve` and `/confirm` accept a 6-digit `token`, look it up via `hasher.Hash(code)`, and converge on the same preview/visit as the QR token.

- [ ] **Step 1: Write the failing tests** (append)

```csharp
[Fact]
public async Task Manual_code_resolves_and_confirms_like_the_qr_token()
{
    var ctx = await factory.SeedEligibleReservationAsync();
    var issue = await ctx.IssueAsync();

    var preview = await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issue.ManualCode });
    Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

    var confirm = await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.ManualCode });
    Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
    // idempotent second confirm
    Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.ManualCode })).StatusCode);
}

[Fact]
public async Task Consuming_via_manual_invalidates_the_qr_token_and_vice_versa()
{
    var a = await factory.SeedEligibleReservationAsync();
    var ia = await a.IssueAsync();
    await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = ia.ManualCode });
    Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = ia.Token })).StatusCode);

    var b = await factory.SeedEligibleReservationAsync();
    var ib = await b.IssueAsync();
    await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = ib.Token });
    Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = ib.ManualCode })).StatusCode);
}

[Fact]
public async Task Reissue_kills_the_previous_pair()
{
    var ctx = await factory.SeedEligibleReservationAsync();
    var first = await ctx.IssueAsync();
    var second = await ctx.IssueAsync();
    foreach (var stale in new[] { first.ManualCode, first.Token })
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = stale })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = second.ManualCode })).StatusCode);
}

[Theory]
[InlineData("000000")]   // random miss
[InlineData("123456")]
public async Task Unknown_manual_code_fails_generically(string guess)
{
    var response = await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = guess });
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("Não foi possível validar este código", body);
    Assert.DoesNotContain("reservation", body, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task Consuming_or_revoking_nulls_the_manual_code_hash_keeping_the_token_hash()
{
    var ctx = await factory.SeedEligibleReservationAsync();
    var i1 = await ctx.IssueAsync();
    await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = i1.ManualCode });
    await using (var s = factory.Services.CreateAsyncScope())
    {
        var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx.ReservationId);
        Assert.Null(row.ManualCodeHash);
        Assert.NotNull(row.UsedAt);
        Assert.Equal(32, row.TokenHash.Length);
    }

    var ctx2 = await factory.SeedEligibleReservationAsync();
    await ctx2.IssueAsync();
    await ctx2.CancelAsync();
    await using (var s = factory.Services.CreateAsyncScope())
    {
        var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx2.ReservationId);
        Assert.Null(row.ManualCodeHash);
        Assert.NotNull(row.RevokedAt);
    }
}

[Fact]
public async Task Cancelling_the_reservation_invalidates_both_and_frees_the_code()
{
    var ctx = await factory.SeedEligibleReservationAsync();
    var issue = await ctx.IssueAsync();
    await ctx.CancelAsync();
    foreach (var stale in new[] { issue.ManualCode, issue.Token })
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = stale })).StatusCode);

    // the 6-digit combination is reusable now: script the source to produce it for another reservation.
    var other = await factory.SeedEligibleReservationAsync();
    var reissued = await other.IssueWithScriptedCodeAsync(issue.ManualCode);
    Assert.Equal(issue.ManualCode, reissued.ManualCode);
    Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = reissued.ManualCode })).StatusCode);
}

[Fact]
public async Task Rescheduling_invalidates_the_old_credential_and_the_new_reservation_has_none()
{
    var ctx = await factory.SeedEligibleReservationAsync();
    var issue = await ctx.IssueAsync();
    var newReservationId = await ctx.RescheduleAsync();
    Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issue.ManualCode })).StatusCode);
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Assert.False(await db.CheckInTokens.AnyAsync(x => x.ReservationId == newReservationId));
}

[Fact]
public async Task Issue_reclaims_an_expired_stale_row_but_never_a_resolvable_one()
{
    // stale (expired, not used/revoked, still has ManualCodeHash) — script the new issue to the same code
    var stale = await factory.SeedIssuedThenExpiredAsync();          // helper: issue, then set ExpiresAt in the past directly in the DB
    var fresh = await factory.SeedEligibleReservationAsync();
    var reissued = await fresh.IssueWithScriptedCodeAsync(stale.ManualCode);
    Assert.Equal(stale.ManualCode, reissued.ManualCode);             // reclaimed
    await using (var s = factory.Services.CreateAsyncScope())
    {
        var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null((await db.CheckInTokens.SingleAsync(x => x.ReservationId == stale.ReservationId)).ManualCodeHash);
    }

    // resolvable row — the new issue must pick a DIFFERENT code, leaving the other row untouched
    var live = await factory.SeedEligibleReservationAsync();
    var liveIssue = await live.IssueAsync();
    var another = await factory.SeedEligibleReservationAsync();
    var scripted = await another.IssueWithScriptedCodeAsync(liveIssue.ManualCode, thenFallbackToRandom: true);
    Assert.NotEqual(liveIssue.ManualCode, scripted.ManualCode);
    Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = liveIssue.ManualCode })).StatusCode);
}
```
> New fixture helpers, all modelled on `CustomerApiTests.cs`: `IssueWithScriptedCodeAsync(code, thenFallbackToRandom = false)` swaps a `ScriptedManualCodeSource` into DI for one issue; `SeedIssuedThenExpiredAsync()` issues then pushes `ExpiresAt` into the past via a direct `ApplicationDbContext` update. The scripted-source test double is the same `IManualCodeSource` seam from Task 14 (also used by Task 16).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter CheckInManualCodeTests`
Expected: FAIL — a 6-digit `token` still hits the base64url path → `400` even for a valid code.

- [ ] **Step 3: Implement** (spec §7A.6)

In `TotemEndpoints`, add a shape dispatch used by both `ResolveCheckIn` and `ConfirmCheckIn`. `FindCheckIn` takes the injected `IManualCheckInCodeHasher hasher` and looks up by either hash column:
```csharp
private static async Task<(...)?> FindCheckIn(string raw, IManualCheckInCodeHasher hasher, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
{
    var value = (raw ?? string.Empty).Trim();
    byte[] hash;
    if (ManualCheckInCode.TryParse(value, out var code))
        hash = hasher.Hash(code);                  // HMAC-SHA-256, keyed
    else
    {
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(value); } catch (FormatException) { return null; }
        if (bytes.Length != 32) return null;
        hash = SHA256.HashData(bytes);
    }

    var row = await (from token in db.CheckInTokens
                     join reservation in db.Reservations on token.ReservationId equals reservation.Id
                     join customer in db.Customers on reservation.CustomerId equals customer.Id
                     join professional in db.Professionals on reservation.ProfessionalId equals professional.Id
                     join room in db.Rooms on reservation.RoomId equals room.Id
                     where token.TokenHash == hash || token.ManualCodeHash == hash   // ManualCodeHash null never equals `hash`
                     select new { token, reservation, customer, Name = professional.Name, RoomName = room.Name })
                    .SingleOrDefaultAsync(ct);
    if (row is null) return null;
    var now = time.GetUtcNow();
    if (row.token.RevokedAt is not null || row.token.ExpiresAt <= now
        || row.reservation.Status != ReservationStatus.Approved || !row.customer.IsActive
        || now < row.reservation.StartAt.Subtract(TimeSpan.FromHours(1)) || now >= row.reservation.EndAt)
        return null;
    return (row.token, row.reservation, row.customer, new TotemCheckInPreview(row.Name, row.RoomName, row.reservation.StartAt, row.reservation.EndAt, true));
}
```
> `where token.TokenHash == hash || token.ManualCodeHash == hash` is safe: `hash` is 32 bytes; a strong token never parses as 6 digits, and a manual hash only matches `ManualCodeHash`. Keep the eligibility gate character-for-character identical. `InvalidCheckIn()` message becomes `"Não foi possível validar este código."` (generic for both paths — or keep the current phrasing; the spec text is the contract).

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter "CheckInManualCodeTests|TotemPresenceApiTests|ReservationWorkflowTests|CustomerApiTests"`
Expected: PASS — manual + QR converge; **no regression** in the strong-token flow.

- [ ] **Step 5: Commit**

```bash
git add recepcaototem/Features/Totem/TotemEndpoints.cs tests/GestaoPredio.IntegrationTests/CheckInManualCodeTests.cs
git commit -m "feat(totem): resolve/confirm accept a 6-digit code, converging on the same eligibility rule"
```

---

## Task 16: Security — HMAC key, brute force, collision/reclaim, no-leak

**Files:**
- Test: `tests/GestaoPredio.IntegrationTests/CheckInManualCodeTests.cs` (append); `ScriptedManualCodeSource` test double.
- Modify (only if a gap is found): `HmacManualCheckInCodeHasher.cs` / `IssueToken` / logging.

**Interfaces:**
- Consumes: Tasks 12–15 (incl. `IManualCodeSource`, `IManualCheckInCodeHasher`).
- Produces: no new production code unless a test reveals a gap.

- [ ] **Step 1: Write the tests**

```csharp
[Fact]
public async Task Persisted_hash_is_keyed_hmac_not_plain_sha256()
{
    var ctx = await factory.SeedEligibleReservationAsync();
    var issue = await ctx.IssueAsync();
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IManualCheckInCodeHasher>();
    ManualCheckInCode.TryParse(issue.ManualCode, out var code);
    var row = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx.ReservationId);
    Assert.Equal(hasher.Hash(code), row.ManualCodeHash);                       // matches the app hasher (test key)
    Assert.NotEqual(SHA256.HashData(Encoding.ASCII.GetBytes(issue.ManualCode)), row.ManualCodeHash); // not plain SHA-256
}

[Fact]
public async Task A_different_hmac_key_does_not_resolve_previously_issued_codes()
{
    var ctx = await factory.SeedEligibleReservationAsync();
    var issue = await ctx.IssueAsync();
    using var withOtherKey = factory.WithConfig("CheckIn:ManualCodeHmacKey", "a-totally-different-key");
    Assert.Equal(HttpStatusCode.BadRequest,
        (await withOtherKey.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issue.ManualCode })).StatusCode);
    // ...but the strong QR token still resolves under the new key (hash is keyless)
    Assert.Equal(HttpStatusCode.OK,
        (await withOtherKey.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issue.Token })).StatusCode);
}

[Fact]
public async Task Missing_hmac_key_in_production_fails_closed()
{
    // A factory configured with EnvironmentName=Production and no CheckIn:ManualCodeHmacKey
    // fails to resolve IManualCheckInCodeHasher (host build / first request throws). Assert the
    // failure and that no exception/message carries key material.
}

[Fact]
public async Task Resolve_is_rate_limited_per_ip()
{
    // mirror an existing rate-limit test: fire CustomerIpPermitLimit+1 rapid resolves with random
    // 6-digit codes from the same client; expect a 429 at the limit.
}

[Fact]
public async Task Exhausting_retries_returns_503_without_leaking_value_or_hash()
{
    // ScriptedManualCodeSource that always returns a code colliding with a LIVE row ->
    // 503 CHECK_IN_CODE_UNAVAILABLE; body has no 6-digit run; AuditEntries has
    // CHECK_IN_TOKEN_ISSUE_FAILED and neither the code nor any hex hash nor the HMAC key.
}

[Fact]
public async Task Neither_the_code_nor_the_hash_nor_the_secret_appears_in_audit_or_logs()
{
    var log = factory.CaptureLogs();                       // ILogger collector, as in existing tests
    var ctx = await factory.SeedEligibleReservationAsync();
    var issue = await ctx.IssueAsync();
    await factory.Client.PostAsJsonAsync("/api/totem/check-in/resolve", new { token = issue.ManualCode });
    await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.ManualCode });
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var audit = string.Join("\n", await db.AuditEntries.Select(a => a.Action + "|" + a.Result + "|" + (a.CorrelationId ?? "")).ToListAsync());
    var testKey = factory.Configuration["CheckIn:ManualCodeHmacKey"]!;
    foreach (var haystack in new[] { audit, log.Text })
    {
        Assert.DoesNotContain(issue.ManualCode, haystack);
        Assert.DoesNotContain(testKey, haystack);
    }
}
```

- [ ] **Step 2: Run → gaps** — `dotnet test tests/GestaoPredio.IntegrationTests --filter CheckInManualCodeTests`. The collision/exhaustion/key tests fail until the `ScriptedManualCodeSource` + `factory.WithConfig` helpers exist; the no-leak/rate-limit tests should already pass (characterisation) if Tasks 13–15 were done right.

- [ ] **Step 3: Add the test seams + fix any real gap**

`ScriptedManualCodeSource(params string[] codes)` implements `IManualCodeSource` (returns each parsed code, then falls back to `ManualCheckInCode.Generate()`), registered via `factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IManualCodeSource>(...)))`. `factory.WithConfig(key, value)` returns a factory with an overridden configuration value. Fix any real leak the tests expose — there must be none: nothing logs `code.Value`, `manualHash`, or the HMAC key; `HmacManualCheckInCodeHasher`'s fail-closed message names the config key only, never its value.

- [ ] **Step 4: Run → PASS.**

- [ ] **Step 5: Commit**

```bash
git add tests/GestaoPredio.IntegrationTests/CheckInManualCodeTests.cs "recepcaototem/**" "src/GestaoPredio.Infrastructure/Customers/**"
git commit -m "test(checkin): HMAC key handling, brute-force limit, collision/reclaim, and no-leak guarantees"
```

---

## Task 17: Frontend — API type + CUSTOMER code display

**Files:**
- Modify: `recepcaototem/ClientApp/src/api/modules.ts` (`issueCheckInToken` return type)
- Modify: `recepcaototem/ClientApp/src/pages/customer/CustomerReservationDetail.tsx` (+ `.test.tsx`)

**Interfaces:**
- Consumes: Task 14 response shape.
- Produces: `customerApi.issueCheckInToken(id): Promise<{ token: string; manualCode: string; expiresAt: string }>`.

- [ ] **Step 1: Write the failing test** (add to `CustomerReservationDetail.test.tsx`, create if absent)

```tsx
test('shows the 6-digit code next to the QR after "Gerar QR Code"', async () => {
  vi.mocked(customerApi.issueCheckInToken).mockResolvedValue({ token: 'strong-token', manualCode: '004821', expiresAt: '2026-09-15T14:00:00Z' })
  // render at /cliente/agendamentos/:id with an APPROVED reservation mock
  fireEvent.click(await screen.findByRole('button', { name: /gerar qr code/i }))
  expect(await screen.findByAltText('QR Code de check-in')).toBeInTheDocument()
  expect(screen.getByText('004821')).toBeInTheDocument()               // leading zero preserved
  expect(screen.getByText('Use este código no Totem.')).toBeInTheDocument()
})
test('the manual code is never written to web storage', async () => {
  const setItem = vi.spyOn(Storage.prototype, 'setItem')
  // ... issue as above ...
  expect(setItem).not.toHaveBeenCalledWith(expect.anything(), expect.stringContaining('004821'))
})
```

- [ ] **Step 2: Run to verify failure** — `npx vitest run src/pages/customer/CustomerReservationDetail.test.tsx` → FAIL.

- [ ] **Step 3: Implement**

`modules.ts`: `issueCheckInToken(id): Promise<{ token: string; manualCode: string; expiresAt: string }>`.
`CustomerReservationDetail.tsx`: add `const [manualCode, setManualCode] = useState('')`; in `issueToken()` set it from `result.manualCode`; render, when `qrDataUrl` is present, a small block:
```tsx
<div className="customer-checkin-code">
  <small>Código</small>
  <strong aria-label={`Código ${manualCode.split('').join(' ')}`}>{manualCode}</strong>
  <span>Use este código no Totem.</span>
</div>
```
No storage. Append `.customer-checkin-code` CSS.

- [ ] **Step 4: Run to verify pass** — `npx vitest run src/pages/customer/CustomerReservationDetail.test.tsx` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/api/modules.ts src/pages/customer/CustomerReservationDetail.tsx src/pages/customer/CustomerReservationDetail.test.tsx src/styles.css
git commit -m "feat(customer): show the 6-digit check-in code next to the QR"
```

---

## Task 18: Frontend — Totem 6-digit input

**Files:**
- Create: `recepcaototem/ClientApp/src/features/totem/sixDigitCode.ts` + `.test.ts`
- Modify: `recepcaototem/ClientApp/src/pages/TotemCheckIn.tsx` + `.test.tsx`

**Interfaces:**
- Consumes: nothing new.
- Produces: `onlyDigits6(v: string): string`, `isComplete6(v: string): boolean`.

- [ ] **Step 1: Write the failing tests**

`src/features/totem/sixDigitCode.test.ts`:
```ts
import { expect, test } from 'vitest'
import { isComplete6, onlyDigits6 } from './sixDigitCode'
test('keeps only digits, max 6, leading zeros', () => {
  expect(onlyDigits6('a1b2c3d4')).toBe('1234')
  expect(onlyDigits6('123456789')).toBe('123456')
  expect(onlyDigits6('00 12 34')).toBe('001234')
})
test('isComplete6', () => {
  expect(isComplete6('001234')).toBe(true)
  expect(isComplete6('1234')).toBe(false)
})
```
Add to `TotemCheckIn.test.tsx` (manual segment): renders 6-digit input with `inputMode="numeric"`; typing letters is ignored; paste `"004821"` fills it; "Validar agendamento" disabled at < 6, enabled at 6; `Enter` at 6 calls `totemApi.resolveCheckIn('004821')`; a backend `400` shows the generic message. Keep all existing scan/QR tests green.

- [ ] **Step 2: Run to verify failure** — `npx vitest run src/features/totem/sixDigitCode.test.ts src/pages/TotemCheckIn.test.tsx` → FAIL.

- [ ] **Step 3: Implement**

`sixDigitCode.ts`:
```ts
export const onlyDigits6 = (v: string) => v.replace(/\D/g, '').slice(0, 6)
export const isComplete6 = (v: string) => onlyDigits6(v).length === 6
```
`TotemCheckIn.tsx` manual segment: replace the free-text `<input placeholder="Cole ou digite o código">` with a 6-digit entry — a single `<input inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={token} onChange={e => setToken(onlyDigits6(e.target.value))}>` (or a 6-box group backed by the same `token` string). Submit button `disabled={!isComplete6(token) || loading}`. `submitManual` unchanged (still calls `resolveToken(token, 'manual')`). `Enter` submits the form when `isComplete6`. "Escanear QR" path and `normalizeToken` untouched.

- [ ] **Step 4: Run to verify pass** — `npx vitest run src/features/totem/sixDigitCode.test.ts src/pages/TotemCheckIn.test.tsx` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/features/totem/sixDigitCode.ts src/features/totem/sixDigitCode.test.ts src/pages/TotemCheckIn.tsx src/pages/TotemCheckIn.test.tsx src/styles.css
git commit -m "feat(totem): 6-digit manual code entry on /totem/check-in (QR scan preserved)"
```

---

## Task 19: Full gates + accessibility sweep

**Files:** none new; fixes only where a gate fails.

- [ ] **Step 1: Backend suite**

Run: `dotnet test`
Expected: PASS — including `ManualCheckInCodeTests`, `CheckInTokenTests`, `CheckInManualCodeTests`, `TotemProfessionalStatusTests`, `TotemProfessionalsCarouselTests`, and **no regression** in `TotemPresenceApiTests` / `CustomerApiTests` / `ReservationWorkflowTests`. (The 2 pre‑existing `ReservationWorkflowTests` wall‑clock flakes are known — do not fix here; confirm they fail identically on a clean tree if they appear.)

- [ ] **Step 2: Frontend suite + type + build + verifier + whitespace** (from `recepcaototem/ClientApp`)

```bash
npx vitest run
npx tsc -b
npx vite build
npm run --silent verify:production-bundle
git diff --check
```
Expected: all green. `verify:production-bundle` confirms no `src/dev/` / `AppStore` / `atrium_*` leaked. `production-isolation.test.tsx` still passes.

- [ ] **Step 3: Accessibility sweep (manual checklist against the code)**

- `/totem`, `/totem/profissionais`, `/totem/check-in`: every actionable element is a real `<button>`/`<a>` with a visible `:focus-visible` outline; `aria-label`s present on icon‑only controls; carousel `role="listbox"`/`option`/`aria-selected` + arrow‑key nav + `Home/End`.
- Status shown as **dot + text** (never colour alone).
- `prefers-reduced-motion`: `LightRays`, `BlurFade`, `BorderBeam`, `ProgressiveBlur` shimmer, `RippleButton`, and carousel scrolling all degrade to static/instant (grep the media query + the `usePrefersReducedMotion` branches).
- Photo fallback: no broken image possible (`onError` → initials).
- Camera fallback to manual entry preserved in `TotemCheckIn`.
- **6-digit input:** `inputMode="numeric"` (no alphanumeric keyboard), a real submit `<button>` that is `disabled` under 6 digits, `Enter` submits at 6, leading zeros preserved, visible focus on every position/box.
- **CUSTOMER code block:** the 6 digits carry an `aria-label` with the spaced value; the surrounding copy is present as text, not colour.

- [ ] **Step 4: Commit any fixes**

```bash
git add -A
git commit -m "chore(totem): green gates + a11y sweep for the Totem + manual check-in code"
```

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:
- §4 `/totem` → Task 7. §5 + §12 carousel → Task 8; §5.3 status chip → Task 8; §5.4 Continuar → Task 9.
- §6 `/totem/check-in` → Task 10 (nav/copy) + Task 18 (6-digit input). §7 endpoint+DTO → Task 2; §7.3 mapper → Task 1; §7.4 photo (503 for I/O, 404 for guards — **approved**) → Task 3. §7A credential model → Tasks 12–18; §7A.3 value object → Task 12; §7A.3b keyed HMAC hasher + §7A.4 transient persistence + §7A.11 secret + §26 migration → Task 13; §7A.5 collision/reclaim + §7A.7 issue → Task 14; §7A.6 resolve/confirm dispatch + §7A.2 QR↔manual + transient lifecycle + cancel/reschedule → Task 15; §7A.9 rate-limit + §7A.10 no-leak + §7A.11 fail-closed → Task 16; §7A.7 CUSTOMER display + §7A.8 re-issue → Task 17. §8 status rule → Tasks 1–2.
- §9 photos (initials + endpoint) → Tasks 3, 4, 8. §10 returnUrl → Tasks 5, 11. §11 nav/activeProfessional → Tasks 8–10.
- §13 Magic UI → Task 6. §14 palette / §15 copy → Tasks 6–10, 17, 18 (CSS + literal strings in tests). §16 loading/empty/error → Task 9. §17 KioskClock reuse → Tasks 7, 9, 10. §18 security → Tasks 2, 3, 13, 14, 16. §19 a11y → Task 19 sweep + per‑component tests. §20 CSP → untouched by design (noted in Global Constraints). §21 file map → File Structure. §22 tests → each task's tests + Task 19. §23 out‑of‑scope → Global Constraints. §24 decisions / §25 risks / §26 migration → Tasks 3, 12–17.

**2. Placeholder scan** — intentionally‑deferred details, all with named patterns to mirror: `SeedPhotoFileAsync` (Task 3 → `ProfessionalPhotoTests.cs`); Task 11 test bodies (assertions + mocking pattern named); fixture helpers `SeedEligibleReservationAsync` / `IssueAsync` / `IssueWithScriptedCodeAsync` / `SeedIssuedThenExpiredAsync` / `CancelAsync` / `RescheduleAsync` / `WithConfig` / `CaptureLogs` (Tasks 14–16 → `CustomerApiTests.cs` + existing log-collector tests); the seams `IManualCodeSource` (Task 14, default `DefaultManualCodeSource`) and `ScriptedManualCodeSource` (Task 16). No `TBD`/`TODO`. All production steps carry real code.

**3. Type consistency** —
- `TotemProfessionalCard` (backend) ↔ `TotemProfessionalCardDto` (frontend): same 5 field names, `photoUrl` camelCase in JSON.
- `TotemProfessionalStatus.Resolve(bool,bool)` — Tasks 1, 2.
- `safeCustomerReturnUrl(string|null|undefined): string|null` — Tasks 5, 11.
- `ManualCheckInCode` — `Generate() : ManualCheckInCode`, `TryParse(string?, out ManualCheckInCode) : bool`, `.Value : string`, `ToString()` — **no `Hash()`** — identical in Tasks 12, 13, 14, 15, 16.
- `IManualCheckInCodeHasher.Hash(ManualCheckInCode) : byte[]` (32) — Tasks 13 (produces), 14/15/16 (consume). `ManualCheckInCodeHashingOptions.ManualCodeHmacKey` / `SectionName = "CheckIn"` — Tasks 13, 16.
- `CheckInToken.Create(Guid, byte[] tokenHash, byte[] manualCodeHash, DateTimeOffset, DateTimeOffset)` / `Rotate(byte[], byte[], DateTimeOffset, DateTimeOffset)` / `MarkUsed`+`Revoke` null `ManualCodeHash` / `ClearManualCode()` — Tasks 13, 14, 15. `ManualCodeHash : byte[]?` — Tasks 13, 15, 16.
- `IManualCodeSource.Next() : ManualCheckInCode` (`DefaultManualCodeSource`, `ScriptedManualCodeSource`) — Tasks 14, 15, 16.
- Issue response `{ token: string, manualCode: string, expiresAt: string }` — Tasks 14 (produces), 15/16 (tests), 17 (`customerApi.issueCheckInToken` type + `CustomerReservationDetail`).
- `onlyDigits6` / `isComplete6` — Task 18.

**Photo status codes — RESOLVED (approved):** `503` for real storage/I·O failure (helper preserves the historical admin behaviour for both callers); `404` **only** for professional inexistente / inativo / sem foto / `Purpose` errado. Reflected in spec §7.4, §24.11 and Task 3.

**Manual-code hashing — RESOLVED (2 mandatory corrections applied):** (1) persisted hash is **HMAC-SHA-256 keyed** via `IManualCheckInCodeHasher` + `HmacManualCheckInCodeHasher` (fail-closed in `Production`, dedicated `CheckIn:ManualCodeHmacKey`, no rotation), not plain SHA-256; `ManualCheckInCode` stays a pure value object. (2) `ManualCodeHash` is **transient** — nulled by `MarkUsed`/`Revoke`, replaced by `Rotate`, lazily reclaimed for stale colliding rows in the issue transaction; no cleanup job. Reflected in spec §7A.3/§7A.3b/§7A.4/§7A.5/§7A.9/§7A.10/§7A.11, §24.13–21, §26, and Tasks 12–16.
