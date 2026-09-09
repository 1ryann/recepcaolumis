# Totem: 3-screen split + real professional carousel — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the Totem into `/totem` (decision), `/totem/profissionais` (real swipe carousel), `/totem/check-in` (existing QR/code flow), backed by a minimal public professionals endpoint, and carry the chosen `professionalId` into the CUSTOMER booking flow through a strictly‑validated `returnUrl`.

**Architecture:** ASP.NET Core minimal‑API host (`recepcaototem`) over a clean‑architecture core (`src/GestaoPredio.*`), PostgreSQL/EF Core. Frontend is React 18 + TS + Vite + Vitest, dark "Lumis kiosk" identity already in `styles.css`. No new runtime dependencies (CSP blocks external scripts): the carousel and all "Magic UI" effects are local components. TDD throughout; one local commit per task.

**Spec:** `docs/superpowers/specs/2026-09-09-lumis-totem-entry-and-professional-carousel-design.md` (commit `d90cd5a`). The plan argues from the spec; read both.

## Global Constraints

- Branch `codex/reception-backend`, worktree `.worktrees/reception-backend`. **No push, no `main`, no Railway, no DB migration, no manual DB.**
- Commit trailer on every commit: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- **Do not edit `recepcaototem/Program.cs`.** The CSP `font-src`/`worker-src` fix is a separate prerequisite (spec §20). The camera/QR scan path is not "done" until it ships elsewhere.
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
- `tests/GestaoPredio.UnitTests/TotemProfessionalStatusTests.cs`
- `tests/GestaoPredio.IntegrationTests/TotemProfessionalsCarouselTests.cs`

**Backend (modify)**
- `recepcaototem/Features/Totem/TotemEndpoints.cs` — new `record TotemProfessionalCard`; replace `Professionals` handler body; add `ProfessionalPhoto` handler + route.
- `recepcaototem/Features/Professionals/ProfessionalPhotoEndpoints.cs` — `Get` delegates to the shared helper (behaviour unchanged: `private, no-store`, 503 on storage failure).

**Frontend (create)** — under `recepcaototem/ClientApp/src/`
- `auth/returnUrl.ts` + `auth/returnUrl.test.ts` — `safeCustomerReturnUrl`.
- `features/totem/professionalInitials.ts` + `.test.ts`.
- `features/totem/magic/LightRays.tsx`, `BlurFade.tsx`, `MagicCard.tsx`, `BorderBeam.tsx`, `ProgressiveBlur.tsx`, `RippleButton.tsx`
- `features/totem/magic/magic.test.tsx`
- `features/totem/TotemProfessionalCarousel.tsx` + `.test.tsx`
- `pages/TotemEntry.tsx` + `.test.tsx`
- `pages/TotemProfessionals.tsx` + `.test.tsx`

**Frontend (modify)**
- `api/modules.ts` — `TotemProfessionalCardDto` + `totemApi.professionals()`.
- `App.tsx`, `dev/DevelopmentApp.tsx` — three Totem routes.
- `frontend-portals.test.ts` — updated Totem route assertions.
- `pages/TotemCheckIn.tsx` + `.test.tsx` — `← Voltar` + auto‑return to `/totem` + trimmed aside copy + `LightRays`.
- `components/ProtectedRoute.tsx` + `.test.tsx` — `returnUrl` on the customer redirect.
- `pages/Login.tsx` + `.test.tsx` — honour + propagate `returnUrl` (customer only).
- `pages/customer/CustomerRegister.tsx` + `.test.tsx` — propagate `returnUrl`.
- `pages/customer/CustomerBooking.tsx` + `.test.tsx` — preselect from `?professionalId`.
- `styles.css` — append `.totem-entry-*`, `.totem-carousel-*`, `.totem-status-*`, `.totem-magic-*` blocks (no existing rule changed).

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

## Task 12: Full gates + accessibility sweep

**Files:** none new; fixes only where a gate fails.

- [ ] **Step 1: Backend suite**

Run: `dotnet test`
Expected: PASS. (Note the 2 pre‑existing `ReservationWorkflowTests` wall‑clock flakes are known — do not fix here; confirm they fail identically on a clean tree if they appear.)

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
- Camera fallback to manual code preserved in `TotemCheckIn`.

- [ ] **Step 4: Commit any fixes**

```bash
git add -A
git commit -m "chore(totem): green gates + a11y sweep for the 3-screen Totem"
```

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:
- §4 `/totem` → Task 7. §5 + §12 carousel → Task 8; §5.3 status chip → Task 8; §5.4 Continuar → Task 9.
- §6 `/totem/check-in` → Task 10. §7 endpoint+DTO → Task 2; §7.3 mapper → Task 1; §7.4 photo → Task 3. §8 status rule → Tasks 1–2.
- §9 photos (initials + endpoint) → Tasks 3, 4, 8. §10 returnUrl → Tasks 5, 11. §11 nav/activeProfessional → Tasks 8–10.
- §13 Magic UI → Task 6. §14 palette / §15 copy → Tasks 6–10 (CSS + literal strings in tests). §16 loading/empty/error → Task 9. §17 KioskClock reuse → Tasks 7, 9, 10. §18 security → Tasks 2, 3. §19 a11y → Task 12 sweep + per‑component tests. §20 CSP → untouched by design (noted in Global Constraints). §21 file map → File Structure. §22 tests → each task's tests + Task 12. §23 out‑of‑scope → Global Constraints.

**2. Placeholder scan** — the only intentionally‑deferred detail is `SeedPhotoFileAsync` in Task 3 (explicitly "mirror `ProfessionalPhotoTests.cs`") and the Task 11 test bodies (sketched with exact assertions, mocking pattern named). All implementation steps carry real code.

**3. Type consistency** — `TotemProfessionalCard` (backend) ↔ `TotemProfessionalCardDto` (frontend) share the 5 field names/casing (`photoUrl` camelCase in JSON via default serializer). `TotemProfessionalStatus.Resolve(bool,bool)` signature identical in Tasks 1 and 2. `safeCustomerReturnUrl` signature identical in Tasks 5 and 11. Component prop names in Task 6 match their consumers in Tasks 7–10. `onActiveChange(p: TotemProfessionalCardDto)` identical in Tasks 8 and 9.

**Discovered scope note:** the existing admin photo handler returns **503** (not 404) on storage I/O failure via `PhotoUnavailable`. The shared helper keeps that behaviour for both callers; the Totem endpoint returns **404** only for the not‑found/inactive/no‑photo/wrong‑purpose guards. Spec §7.4 said "404 on I/O failure" — the plan intentionally preserves the existing 503 to avoid changing admin behaviour. Flag for approval.
