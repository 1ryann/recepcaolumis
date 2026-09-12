# Professional Area Completion — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the five remaining "Estamos preparando esta área" placeholders in the Professional Area (Reservas, Atendimentos, Locações, Financeiro, Meu Perfil) with real pages over real backend data, and add professional self-service profile + photo management (WhatsApp, description, photo upload/crop/remove), with the Totem showing new photos immediately via a versioned URL.

**Architecture:** Reuse existing backend contracts for Reservas/Atendimentos/Locações/Financeiro/Disponibilidade (100% present and correctly scoped to the authenticated professional). Add exactly two new backend capabilities: (1) `PUT /api/professional/me` for profile self-edit, and (2) self-service photo upload/delete (`POST`/`DELETE /api/professional/me/photo`) that extracts and reuses the admin photo-mutation logic through a new internal helper, adding a WebP 512×512 normalization step (the one genuine infrastructure gap). Frontend adds five real pages under the existing `ProfessionalShell`, reusing its dark Lumis shell and the same `.panel`/`.data-table`/`.field-input` CSS family already darkened for Admin — scoped under a new `.professional-content` block, mirroring the exact pattern already used for `.admin-content` and `.customer-content` in this branch's prior rounds.

**Tech Stack:** .NET 10 minimal APIs (backend), EF Core + PostgreSQL, xUnit + `Microsoft.AspNetCore.Mvc.Testing` (integration tests) + xUnit (unit tests); React 19 + TypeScript + Vite (frontend), Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-11-professional-area-completion.md` (revised 2026-09-12). This plan implements that spec exactly; where the spec explicitly deferred a decision to "the plan" (image library, frontend crop library, Agenda customer-name display, Locações status filter, mobile table representation), the decision and its justification are recorded in the task that resolves it.

## Global Constraints

- **No migration.** `Professional.PhotoFileId` (`Guid?`), `PrivateFile` (`Id`, `StorageKey`, `MimeType`, `Length`, `Purpose`, `CreatedAt`), and `IPrivateFileStorage`/`FileSystemPrivateFileStorage` already exist and are reused as-is. Do **not** create `PhotoFileName`, `PhotoVersion`, or `PhotoUpdatedAt` columns. Do **not** run `dotnet ef migrations add` anywhere in this plan. If any task appears to need a new column, STOP and return to spec review instead of adding one.
- **No parallel photo infrastructure.** Every photo mutation flows through the existing `IPrivateFileStorage` (`StageAsync`/`CommitAsync`/`OpenStagedReadAsync`/`OpenReadAsync`/`DeleteAsync`/`DiscardAsync`) and the existing `PrivateFile` entity/`PrivateFilePurposes.ProfessionalPhoto` purpose constant. No new storage abstraction, no new entity.
- **Admin gains no new permission.** `/api/admin/professionals/{id}/photo` keeps policy `"Operations"` unchanged. New self-service routes use policy `"Professional"` only. The two paths share only the extracted mutation *logic* (Task 3), never authorization.
- **Never accept `professionalId` from the client on any new/modified endpoint.** The professional is always resolved via `context.User.FindFirstValue(ClaimTypes.NameIdentifier)` → `db.Professionals.Where(p => p.ApplicationUserId == userId && p.IsActive)`, matching the 5 existing call sites (`ProfessionalReservationEndpoints.cs:159-167`, `VisitEndpoints.cs:361-364`, `ProfessionalLeaseEndpoints.cs:32-36`, `FinanceEndpoints.cs:186-191`, `ProfessionalProfileEndpoints.cs:15-20`).
- **Every new/modified mutation (`PUT`/`POST`/`DELETE`) uses `.AddEndpointFilter<AntiforgeryFilter>()`.** No exceptions — this is a universal, already-established convention in this codebase.
- **Every new request DTO implements `IStrictModuleRequest`** (`recepcaototem/Features/Common/StrictBody.cs`) so `JsonUnmappedMemberHandling.Disallow` rejects unmapped fields (`professionalId`, `name`, `profession`, `photoFileId`, etc.) before any handler logic runs.
- **Error codes:** reuse only `INVALID_PROFESSIONAL` (400), `INVALID_PROFESSIONAL_PHOTO` (400), `PHOTO_UNAVAILABLE` (503), `INVALID_CONCURRENCY_TOKEN` (400), `RESOURCE_MODIFIED` (409), `PROFESSIONAL_PROFILE_NOT_LINKED` (404, already `Results.NotFound()` not 403). Do not introduce `INVALID_IMAGE`, `IMAGE_TOO_LARGE`, `INVALID_WHATSAPP`, `DESCRIPTION_TOO_LONG`, `STORAGE_ERROR`, or `PROFILE_NOT_FOUND`.
- **No status paralelo.** Wire enums stay exactly `ReservationStatus{Pending,Approved,Rejected,Cancelled}`, `VisitStatus{Waiting,InService,Ended,Cancelled}` (wire: `WAITING`/`IN_SERVICE`/`ENDED`/`CANCELLED`), Lease status (`AGENDADA|ATIVA|ENCERRAMENTO_PENDENTE|ENCERRADA|CANCELADA`, already computed server-side), `FinancialChargeStatus{Pending,Paid,Cancelled}` plus computed `OVERDUE`. Only pt-BR *labels* are new, in the presentation layer.
- **Visual:** every new professional page is dark (Lumis `--lumis-*` tokens), reusing `PageHeader`/`EmptyState`/`.panel`/`.data-table`/`.field-input`/`Modal` already used by Admin. No third visual convention. No large light surface.
- **Test commands (confirmed real, from `README.md:22-24` and `package.json`):**
  - Backend: `dotnet restore recepcaototem.sln`, `dotnet build recepcaototem.sln`, `dotnet test recepcaototem.sln` (run from repo root `C:\Users\ryan-\OneDrive\Documents\projetos\recepcaolumis\.worktrees\reception-backend`).
  - Frontend (run from `recepcaototem/ClientApp`): `npx vitest run`, `npx tsc -b`, `npx vite build`, `node scripts/verify-production-bundle.mjs`.
  - Global: `git diff --check` (repo root).
- **`verify-production-bundle.mjs`** fails the build if any emitted `.js` contains `atrium_professionals`/`atrium_rooms`/`atrium_leases`/`atrium_visits`/`atrium_settings` (dev mock-store markers) or references `src/dev/`/`AppStore`/`src/data/mock`. New professional pages must never import anything from `src/dev/`.
- **Staging/deploy is out of scope for every task in this plan.** No task pushes, merges, deploys, or applies a migration. Task 15 documents the staging procedure but does not execute it.

---

### Task 1: Professional shared UI foundation — dark surface scope, StatusBadge, filter bar

**Files:**
- Modify: `recepcaototem/ClientApp/src/components/PageElements.tsx`
- Modify: `recepcaototem/ClientApp/src/styles.css`
- Create: `recepcaototem/ClientApp/src/components/ProfessionalFilterBar.tsx`
- Create: `recepcaototem/ClientApp/src/components/ProfessionalFilterBar.test.tsx`
- Modify: `recepcaototem/ClientApp/src/components/PageElements.test.tsx` (create if it does not exist yet — check first with `Glob` for `PageElements.test.tsx`; if absent, create it)
- Modify: `recepcaototem/ClientApp/src/api/client.ts`
- Modify: `recepcaototem/ClientApp/src/api/client.test.ts`

**Interfaces:**
- Consumes: nothing new — pure frontend foundation.
- Produces (consumed by Tasks 5, 7, 8, 9, 11, 12):
  - `apiClient.postMultipart<T>(path: string, body: FormData): Promise<T>` in `api/client.ts`.
  - `StatusBadge({ tone, label }: { tone: 'paid'|'pending'|'overdue'|'active'|'inactive'|'occupied'|'available'|'cancelled'|'rejected'|'approved'|'waiting'|'in-service'|'ended', label: string })` in `components/PageElements.tsx` — **breaking change to the existing signature**, resolved below.
  - `ProfessionalFilterBar({ children }: { children: ReactNode })` in `components/ProfessionalFilterBar.tsx` — renders `<div className="professional-filter-bar">{children}</div>`.
  - CSS scope `.professional-content <selector>` in `styles.css`, mirroring the existing `.admin-content <selector>` block (added in the prior Admin visual round) for: `.panel`, `.panel-header h2/p`, `.table-toolbar`, `.search-field`(+`:focus-within`), `.data-table th/td/tbody tr:hover`, `.person-cell`, `.row-actions button`(+hover), `.empty-state`, `.pagination`, `.field-label`, `.field-input`(+`::placeholder`/`:focus`/`:disabled`), `.modal-card`, `.modal-header`, `.modal-actions`, `.professional-filter-bar`.

**Step 0 — resolve `StatusBadge` breaking-change risk (do this before writing any test):**
Run `Grep` for `<StatusBadge` across `recepcaototem/ClientApp/src` (component usages) and separately for `status={` near any `StatusBadge` import, to confirm whether the existing fixed-union `StatusBadge` component (`src/components/PageElements.tsx`, current signature `StatusBadge({ status }: { status: 'paid'|'pending'|'overdue'|'active'|'inactive'|'occupied'|'available' })`) has any real caller today. Per this branch's research (2026-09-12), no `.tsx` file besides its own definition currently renders `<StatusBadge`, so changing its prop shape from `status` to `tone`+`label` is safe. If the grep finds a real caller, do **not** proceed with a breaking change — instead add `tone`/`label` as an alternate overload (a union type: `{ status: OldUnion } | { tone: string, label: string }`) so the existing caller keeps compiling, and note this deviation in the task's commit message.

- [ ] **Step 1: Write the failing test for `StatusBadge`'s new prop shape**

```tsx
// recepcaototem/ClientApp/src/components/PageElements.test.tsx
import { render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import { StatusBadge } from './PageElements'

test('StatusBadge renders the given tone class and label text', () => {
  render(<StatusBadge tone="waiting" label="Aguardando" />)
  const badge = screen.getByText('Aguardando')
  expect(badge.className).toContain('status-badge')
  expect(badge.className).toContain('status-waiting')
})
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `cd recepcaototem/ClientApp && npx vitest run src/components/PageElements.test.tsx`
Expected: FAIL — `Property 'tone' does not exist` (TS) or, if TS is not enforced at test time, a runtime assertion failure because the rendered badge still uses the old `status` prop and shows nothing for `tone`.

- [ ] **Step 3: Implement the new `StatusBadge` signature**

```tsx
// recepcaototem/ClientApp/src/components/PageElements.tsx
export function StatusBadge({ tone, label }: { tone: string; label: string }) {
  return <span className={`status-badge status-${tone}`}><i />{label}</span>
}
```
Remove the old fixed `labels` map entirely — every caller now supplies its own pt-BR label (this matches how Reservas/Atendimentos/Locações/Financeiro each already need a *different* label per status, per spec §7-§10, so a fixed internal map was never going to cover all of them).

- [ ] **Step 4: Run it and confirm it passes**

Run: `npx vitest run src/components/PageElements.test.tsx`
Expected: PASS.

- [ ] **Step 5: Write the failing test for `apiClient.postMultipart`**

```ts
// recepcaototem/ClientApp/src/api/client.test.ts (add to existing file — read it first to match its existing mocking style for `fetch`/CSRF)
test('postMultipart sends a POST with FormData body and a CSRF header', async () => {
  const csrfResponse = new Response(JSON.stringify({ token: 'tok' }), { status: 200 })
  const okResponse = new Response(JSON.stringify({ ok: true }), { status: 200 })
  const fetchMock = vi.spyOn(globalThis, 'fetch')
    .mockResolvedValueOnce(csrfResponse)
    .mockResolvedValueOnce(okResponse)
  const form = new FormData()
  form.append('file', new Blob(['x']), 'x.png')
  const result = await apiClient.postMultipart<{ ok: boolean }>('/api/professional/me/photo', form)
  expect(result.ok).toBe(true)
  const [, init] = fetchMock.mock.calls[1]
  expect(init?.method).toBe('POST')
  expect(init?.body).toBe(form)
  expect((init?.headers as Record<string, string>)['X-CSRF-TOKEN']).toBe('tok')
})
```
(Match the exact mocking idiom already used in the rest of `client.test.ts` — read the file first; the repo may already spy on `fetch` via `vi.spyOn(globalThis, 'fetch')` or via a different helper, and resetting `csrfToken` module state between tests, per `resetCsrfToken()` exported from `client.ts`.)

- [ ] **Step 6: Run it and confirm it fails**

Run: `npx vitest run src/api/client.test.ts`
Expected: FAIL — `apiClient.postMultipart is not a function`.

- [ ] **Step 7: Implement `postMultipart`**

```ts
// recepcaototem/ClientApp/src/api/client.ts — inside the apiClient object, next to putMultipart
postMultipart<T>(path: string, body: FormData) {
  return mutate<T>('POST', path, body)
},
```

- [ ] **Step 8: Run it and confirm it passes**

Run: `npx vitest run src/api/client.test.ts`
Expected: PASS.

- [ ] **Step 9: Write the failing test for `ProfessionalFilterBar`**

```tsx
// recepcaototem/ClientApp/src/components/ProfessionalFilterBar.test.tsx
import { render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import { ProfessionalFilterBar } from './ProfessionalFilterBar'

test('renders children inside the professional-filter-bar wrapper', () => {
  render(<ProfessionalFilterBar><button>Filtro</button></ProfessionalFilterBar>)
  const button = screen.getByRole('button', { name: 'Filtro' })
  expect(button.parentElement?.className).toBe('professional-filter-bar')
})
```

- [ ] **Step 10: Run it and confirm it fails**

Run: `npx vitest run src/components/ProfessionalFilterBar.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 11: Implement `ProfessionalFilterBar`**

```tsx
// recepcaototem/ClientApp/src/components/ProfessionalFilterBar.tsx
import type { ReactNode } from 'react'

export function ProfessionalFilterBar({ children }: { children: ReactNode }) {
  return <div className="professional-filter-bar">{children}</div>
}
```

- [ ] **Step 12: Run it and confirm it passes**

Run: `npx vitest run src/components/ProfessionalFilterBar.test.tsx`
Expected: PASS.

- [ ] **Step 13: Add the `.professional-content` dark-surface CSS scope (no test — this is a visual-only addition; verify manually per the checklist below, matching this branch's already-established `.admin-content`/`.customer-content` scoping technique so the shared light base rules are never touched directly)**

Read the exact current `.admin-content .panel`/`.table-toolbar`/`.search-field`/`.data-table`/`.person-cell`/`.row-actions`/`.empty-state`/`.pagination`/`.field-label`/`.field-input`/`.modal-card`/`.modal-header`/`.modal-actions` block in `styles.css` (added earlier in this branch's Admin visual round — search for the comment `/* LUMIS admin — dark surfaces across every back-office route`) and add an equivalent block scoped to `.professional-content` immediately after it, using the identical `--lumis-*` values. Add also:
```css
.professional-filter-bar { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; }
```
Also fix the light-token leak found during research: `.professional-empty`/`.professional-loading` fall back to the shared light-theme rule (`color: var(--muted)`, `styles.css` line 143) whenever they are used **outside** `.professional-dashboard` (e.g. in `ProfessionalAgenda`/`ProfessionalPlaceholder`, which use `.professional-empty panel`/`.professional-loading` directly, not inside a `.professional-dashboard` wrapper). Add:
```css
.professional-content .professional-empty,
.professional-content .professional-loading { color: var(--lumis-muted); }
```

- [ ] **Step 14: Manual verification**

Run `npm run dev` (or reuse the existing mobile-preview harness technique from the prior Admin round — build a throwaway `mobile-preview.html`/`mobile-preview-main.tsx` in the ClientApp root that mocks `fetch` for `/api/auth/session` as a `PROFISSIONAL` user and mounts `<App />` inside `<HashRouter>`, exactly as done in the Admin round — delete both scratch files before committing) and visually confirm `/profissional/agenda` (currently real) shows no light surface where `.professional-empty`/`.professional-loading` render. Delete the scratch harness before the commit step.

- [ ] **Step 15: Run full frontend gates**

Run: `npx vitest run && npx tsc -b && npx vite build && node scripts/verify-production-bundle.mjs` (from `recepcaototem/ClientApp`)
Expected: all four pass, zero regressions (in particular, any hidden caller of the old `StatusBadge` signature would now fail `tsc -b` — if it does, that means Step 0's grep missed a caller; fix that caller's call site to use `tone`/`label` as part of this same task, do not defer).

- [ ] **Step 16: Commit**

```bash
git add recepcaototem/ClientApp/src/components/PageElements.tsx recepcaototem/ClientApp/src/components/PageElements.test.tsx recepcaototem/ClientApp/src/components/ProfessionalFilterBar.tsx recepcaototem/ClientApp/src/components/ProfessionalFilterBar.test.tsx recepcaototem/ClientApp/src/api/client.ts recepcaototem/ClientApp/src/api/client.test.ts recepcaototem/ClientApp/src/styles.css
git commit -m "feat(ui): add professional shared UI foundation and dark surface scope"
```

---

### Task 2: `PUT /api/professional/me` — self-profile update

**Files:**
- Create: `recepcaototem/Features/Professionals/ProfessionalProfileContracts.cs`
- Modify: `recepcaototem/Features/Professionals/ProfessionalProfileEndpoints.cs`
- Create: `tests/GestaoPredio.IntegrationTests/ProfessionalProfileTests.cs`

**Interfaces:**
- Consumes: `Professional.Update(string name, string profession, string whatsApp, DateTimeOffset occurredAt, string? description = null)` (`src/GestaoPredio.Domain/Professionals/Professional.cs:40`, no partial-update overload — call it with the professional's *current* `Name`/`Profession` unchanged); `WhatsAppNormalizer.TryNormalize(string?, out string)` (`src/GestaoPredio.Domain/Professionals/WhatsAppNormalizer.cs`); `ConcurrencyToken.Encode(uint)`/`TryDecode(string?, out uint)` (`recepcaototem/Features/Common/ConcurrencyToken.cs`); `ProfessionalEndpoints.InvalidToken()`/`Modified()` (`recepcaototem/Features/Professionals/ProfessionalEndpoints.cs:200-205`); `IStrictModuleRequest` (`recepcaototem/Features/Common/StrictBody.cs`).
- Produces (consumed by Tasks 5 and 7):
```csharp
public sealed record ProfessionalProfileResponse(
    string Name, string Profession, string? Description, string WhatsApp,
    bool HasPhoto, string? PhotoUrl, string ConcurrencyToken);

public sealed record ProfessionalProfileUpdateRequest(
    string WhatsApp, string? Description, string ConcurrencyToken) : IStrictModuleRequest;
```

- [ ] **Step 1: Write the failing integration test for the extended `GET`**

```csharp
// tests/GestaoPredio.IntegrationTests/ProfessionalProfileTests.cs
// Follow the exact ModulesApiFactory / [Collection(ModulesDatabaseCollection.Name)] pattern used by
// ProfessionalPhotoTests.cs and ProfessionalReservationTests.cs in this same directory — read one of
// them first for the exact professional-seeding + authenticated-client helper methods already available
// (e.g. CreateProfessionalAsync, an authenticated HttpClient factory for role "PROFISSIONAL").
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalProfileTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Get_me_returns_whats_app_and_concurrency_token()
    {
        var (client, professional) = await factory.CreateAuthenticatedProfessionalAsync();
        var response = await client.GetAsync("/api/professional/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK); // or Assert.Equal per this repo's convention — check whether FluentAssertions is referenced; if not, use Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("whatsApp", out var whatsApp));
        Assert.False(string.IsNullOrEmpty(whatsApp.GetString()));
        Assert.True(body.TryGetProperty("concurrencyToken", out _));
    }
}
```
(Before writing further tests, open one existing file in this directory — e.g. `ProfessionalReservationTests.cs` — to copy the *exact* helper method names for creating an authenticated professional client; do not invent method names. Replace the placeholder helper call above with whatever that file actually exposes.)

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~ProfessionalProfileTests.Get_me_returns_whats_app_and_concurrency_token"`
Expected: FAIL — current `GET /api/professional/me` response has no `whatsApp` or `concurrencyToken` property (anonymous object today is `{ Name, Profession, Description, HasPhoto, PhotoUrl }`, confirmed at `ProfessionalProfileEndpoints.cs:16-20`).

- [ ] **Step 3: Create the named DTOs**

```csharp
// recepcaototem/Features/Professionals/ProfessionalProfileContracts.cs
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public sealed record ProfessionalProfileResponse(
    string Name,
    string Profession,
    string? Description,
    string WhatsApp,
    bool HasPhoto,
    string? PhotoUrl,
    string ConcurrencyToken);

public sealed record ProfessionalProfileUpdateRequest(
    string WhatsApp,
    string? Description,
    string ConcurrencyToken) : IStrictModuleRequest;
```

- [ ] **Step 4: Modify the `GET` handler to project the named DTO**

In `recepcaototem/Features/Professionals/ProfessionalProfileEndpoints.cs`, replace the anonymous projection (lines 16-20) with:
```csharp
group.MapGet("", async (HttpContext context, ApplicationDbContext db, ILogger<ProfessionalProfileEndpointsLog> logger, CancellationToken cancellationToken) =>
{
    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var professional = await db.Professionals.AsNoTracking()
        .Where(p => p.ApplicationUserId == userId && p.IsActive)
        .Select(p => new ProfessionalProfileResponse(
            p.Name, p.Profession, p.Description, p.WhatsApp,
            p.PhotoFileId != null, p.PhotoFileId != null ? "/api/professional/me/photo" : null,
            ConcurrencyToken.Encode(p.Version)))
        .SingleOrDefaultAsync(cancellationToken);
    context.Response.Headers.CacheControl = "private, no-store";
    if (professional is null)
    {
        logger.LogWarning("Professional role has no active professional link. UserId: {UserId}", userId);
        return Results.NotFound(new { code = "PROFESSIONAL_PROFILE_NOT_LINKED", message = "O perfil profissional não está vinculado corretamente." });
    }
    return Results.Ok(professional);
});
```
Note: `Professional.WhatsApp` must be selectable in the EF projection — confirm the property is public on the entity (it is, per `Professional.cs`, used already by `ProfessionalResponse` in the admin contracts).

- [ ] **Step 5: Run it and confirm it passes**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~ProfessionalProfileTests.Get_me_returns_whats_app_and_concurrency_token"`
Expected: PASS.

- [ ] **Step 6: Write the failing test for `PUT /api/professional/me` happy path**

```csharp
[Fact]
public async Task Put_me_updates_whats_app_and_description_and_keeps_name_profession()
{
    var (client, professional) = await factory.CreateAuthenticatedProfessionalAsync();
    var getResponse = await client.GetAsync("/api/professional/me");
    var current = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
    var token = current.GetProperty("concurrencyToken").GetString();

    var putResponse = await client.PutAsJsonAsync("/api/professional/me", new
    {
        whatsApp = "11988887777",
        description = "Atendimento humanizado.",
        concurrencyToken = token,
    });

    Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
    var updated = await putResponse.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("+5511988887777", updated.GetProperty("whatsApp").GetString());
    Assert.Equal("Atendimento humanizado.", updated.GetProperty("description").GetString());
    Assert.Equal(professional.Name, updated.GetProperty("name").GetString());
    Assert.Equal(professional.Profession, updated.GetProperty("profession").GetString());
    Assert.NotEqual(token, updated.GetProperty("concurrencyToken").GetString());
}

[Fact]
public async Task Put_me_rejects_unmapped_fields_like_professional_id()
{
    var (client, _) = await factory.CreateAuthenticatedProfessionalAsync();
    var response = await client.PutAsync("/api/professional/me",
        JsonContent.Create(new { whatsApp = "11988887777", concurrencyToken = "x", professionalId = Guid.NewGuid() }));
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task Put_me_with_invalid_whats_app_returns_invalid_professional()
{
    var (client, professional) = await factory.CreateAuthenticatedProfessionalAsync();
    var getResponse = await client.GetAsync("/api/professional/me");
    var token = (await getResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyToken").GetString();
    var response = await client.PutAsJsonAsync("/api/professional/me", new { whatsApp = "123", concurrencyToken = token });
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("INVALID_PROFESSIONAL", body.GetProperty("code").GetString());
}
```

- [ ] **Step 7: Run and confirm failure**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~ProfessionalProfileTests"`
Expected: FAIL — `PUT /api/professional/me` does not exist yet (404/405).

- [ ] **Step 8: Implement the `PUT` handler**

```csharp
// recepcaototem/Features/Professionals/ProfessionalProfileEndpoints.cs
group.MapPut("", async (
    ProfessionalProfileUpdateRequest request, HttpContext context, ApplicationDbContext db,
    TimeProvider timeProvider, CancellationToken cancellationToken) =>
{
    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var expectedVersion))
        return ProfessionalEndpoints.InvalidToken();

    var professional = await db.Professionals
        .SingleOrDefaultAsync(p => p.ApplicationUserId == userId && p.IsActive, cancellationToken);
    if (professional is null)
        return Results.NotFound(new { code = "PROFESSIONAL_PROFILE_NOT_LINKED", message = "O perfil profissional não está vinculado corretamente." });
    if (professional.Version != expectedVersion)
        return ProfessionalEndpoints.Modified();

    if (!WhatsAppNormalizer.TryNormalize(request.WhatsApp, out var canonicalWhatsApp))
        return Results.BadRequest(new ApiError("INVALID_PROFESSIONAL", "Os dados do profissional são inválidos."));
    var description = request.Description?.Trim();
    if (description is { Length: > 500 } || (description is not null && (description.Contains('<') || description.Contains('>'))))
        return Results.BadRequest(new ApiError("INVALID_PROFESSIONAL", "Os dados do profissional são inválidos."));

    var now = timeProvider.GetUtcNow();
    db.Entry(professional).Property(x => x.Version).OriginalValue = expectedVersion;
    professional.Update(professional.Name, professional.Profession, canonicalWhatsApp, now,
        string.IsNullOrWhiteSpace(description) ? null : description);
    db.AuditEntries.Add(ProfessionalEndpoints.CreateAudit(context, professional.Id, "PROFESSIONAL_PROFILE_UPDATED", now));

    try { await db.SaveChangesAsync(cancellationToken); }
    catch (DbUpdateConcurrencyException) { return ProfessionalEndpoints.Modified(); }

    return Results.Ok(new ProfessionalProfileResponse(
        professional.Name, professional.Profession, professional.Description, professional.WhatsApp,
        professional.PhotoFileId != null, professional.PhotoFileId != null ? "/api/professional/me/photo" : null,
        ConcurrencyToken.Encode(professional.Version)));
}).AddEndpointFilter<AntiforgeryFilter>();
```
Add audit action string `"PROFESSIONAL_PROFILE_UPDATED"` (new, additive — audit strings are just data, not a schema change). Confirm `ApiError` (used for the 400 body) is the same type already used across `ProfessionalPhotoEndpoints.cs`/`ProfessionalEndpoints.cs` (check its namespace via those files' `using` list) — reuse it, do not redefine.

- [ ] **Step 9: Run and confirm pass**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~ProfessionalProfileTests"`
Expected: PASS, all cases.

- [ ] **Step 10: Regression — full backend suite**

Run: `dotnet test recepcaototem.sln`
Expected: all pre-existing tests still pass (no other endpoint touches `ProfessionalProfileEndpoints.cs`).

- [ ] **Step 11: Commit**

```bash
git add recepcaototem/Features/Professionals/ProfessionalProfileContracts.cs recepcaototem/Features/Professionals/ProfessionalProfileEndpoints.cs tests/GestaoPredio.IntegrationTests/ProfessionalProfileTests.cs
git commit -m "feat(professional): allow self profile updates via PUT /api/professional/me"
```

---

### Task 3: Extract `ProfessionalPhotoMutation` (behavior-preserving refactor)

**Files:**
- Create: `recepcaototem/Features/Professionals/ProfessionalPhotoMutation.cs`
- Modify: `recepcaototem/Features/Professionals/ProfessionalPhotoEndpoints.cs`

**Interfaces:**
- Consumes: everything the current `Put`/`Delete` handlers already consume (`IPrivateFileStorage`, `IProfessionalPhotoValidator`, `IOptions<PrivateFileStorageOptions>`, `TimeProvider`, `ILoggerFactory`, `PrivateFile`, `ProfessionalEndpoints.InvalidToken/Modified/CreateAudit`).
- Produces (consumed by Task 4 [adds a parameter] and Task 5):
```csharp
internal sealed class PhotoMutationOutcome
{
    public bool Succeeded { get; }
    public IResult? ErrorResult { get; }
    public static PhotoMutationOutcome Ok() => new(true, null);
    public static PhotoMutationOutcome Failed(IResult errorResult) => new(false, errorResult);
}

internal static class ProfessionalPhotoMutation
{
    internal static Task<PhotoMutationOutcome> PutAsync(
        Professional professional, HttpRequest request, HttpContext context, ApplicationDbContext db,
        IPrivateFileStorage storage, IProfessionalPhotoValidator validator,
        IOptions<PrivateFileStorageOptions> storageOptions, TimeProvider timeProvider,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken);

    internal static Task<PhotoMutationOutcome> DeleteAsync(
        Professional professional, string? concurrencyTokenValue, HttpContext context, ApplicationDbContext db,
        IPrivateFileStorage storage, TimeProvider timeProvider, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken);
}
```
This is a **pure extraction** — the professional entity is now a parameter (already resolved+tracked by the caller) instead of being loaded by id inside the method, so the exact same helper serves both "resolve by `{id:guid}` route param" (admin) and "resolve by authenticated identity" (self-serve, Task 5) call sites. No behavior changes; every existing assertion in `ProfessionalPhotoTests.cs`/`ProfessionalPhotoFailureTests.cs` must still pass unmodified.

- [ ] **Step 1: Confirm the safety net — run the existing photo tests before touching anything**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~ProfessionalPhoto"`
Expected: PASS (baseline — this suite is the regression net for this whole task; no new test is written here because this is a pure internal refactor with an existing black-box test suite already covering the exact same HTTP behavior).

- [ ] **Step 2: Create `ProfessionalPhotoMutation.cs` by moving the body of `Put`/`Delete`**

```csharp
// recepcaototem/Features/Professionals/ProfessionalPhotoMutation.cs
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Files;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace recepcaototem.Features.Professionals;

internal sealed class PhotoMutationOutcome
{
    public bool Succeeded { get; }
    public IResult? ErrorResult { get; }
    private PhotoMutationOutcome(bool succeeded, IResult? errorResult) { Succeeded = succeeded; ErrorResult = errorResult; }
    public static PhotoMutationOutcome Ok() => new(true, null);
    public static PhotoMutationOutcome Failed(IResult errorResult) => new(false, errorResult);
}

internal static class ProfessionalPhotoMutation
{
    internal static async Task<PhotoMutationOutcome> PutAsync(
        Professional professional, HttpRequest request, HttpContext context, ApplicationDbContext db,
        IPrivateFileStorage storage, IProfessionalPhotoValidator validator,
        IOptions<PrivateFileStorageOptions> storageOptions, TimeProvider timeProvider,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        // BODY: copy lines 61-168 of the CURRENT ProfessionalPhotoEndpoints.Put verbatim, with these
        // substitutions: remove the `id`/`professional is null` 404 lookup (professional is now a parameter,
        // already resolved by the caller); every `return Results.NotFound()` for "professional not found"
        // is deleted (caller's job); every other `return X` becomes `return PhotoMutationOutcome.Failed(X)`;
        // the final `return Results.Ok(professional.ToResponse())` becomes `return PhotoMutationOutcome.Ok()`
        // (caller builds its own response DTO from the now-mutated `professional` reference).
    }

    internal static async Task<PhotoMutationOutcome> DeleteAsync(
        Professional professional, string? concurrencyTokenValue, HttpContext context, ApplicationDbContext db,
        IPrivateFileStorage storage, TimeProvider timeProvider, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // BODY: copy lines 181-214 of the CURRENT ProfessionalPhotoEndpoints.Delete verbatim, same
        // substitutions as above (professional is a parameter, no 404 lookup; PhotoMutationOutcome instead
        // of IResult; final success path returns PhotoMutationOutcome.Ok()).
    }

    // Move ReadUploadAsync, DiscardSafely, DeleteSafely, RollbackSafely, CleanupPreviousFile,
    // PhotoUnavailable, InvalidPhoto, ParsedUpload, InvalidPhotoRequestException here too — they are
    // only used by Put/Delete, never by Get, so they belong with the mutation logic.
}
```

- [ ] **Step 3: Update `ProfessionalPhotoEndpoints.cs`'s `Put`/`Delete` to call the extracted helper**

```csharp
private static async Task<IResult> Put(Guid id, HttpRequest request, HttpContext context, ApplicationDbContext db,
    IPrivateFileStorage storage, IProfessionalPhotoValidator validator, IOptions<PrivateFileStorageOptions> storageOptions,
    TimeProvider timeProvider, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
{
    var professional = await db.Professionals.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (professional is null) return Results.NotFound();
    var outcome = await ProfessionalPhotoMutation.PutAsync(professional, request, context, db, storage, validator,
        storageOptions, timeProvider, loggerFactory, cancellationToken);
    return outcome.Succeeded ? Results.Ok(professional.ToResponse()) : outcome.ErrorResult!;
}

private static async Task<IResult> Delete(Guid id, [FromBody] ProfessionalPhotoDeleteRequest request, HttpContext context,
    ApplicationDbContext db, IPrivateFileStorage storage, TimeProvider timeProvider, ILoggerFactory loggerFactory,
    CancellationToken cancellationToken)
{
    var professional = await db.Professionals.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (professional is null) return Results.NotFound();
    var outcome = await ProfessionalPhotoMutation.DeleteAsync(professional, request.ConcurrencyToken, context, db,
        storage, timeProvider, loggerFactory, cancellationToken);
    return outcome.Succeeded ? Results.Ok(professional.ToResponse()) : outcome.ErrorResult!;
}
```
Keep `ProfessionalPhotoDeleteRequest`, `Get`, `MapProfessionalPhotoEndpoints`, and the route registration entirely unchanged in this file.

- [ ] **Step 4: Run the full photo test suite and confirm it still passes byte-for-byte**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~ProfessionalPhoto"`
Expected: PASS — identical results to Step 1. If anything differs, the extraction changed behavior; fix the extraction, do not adjust the tests (this step's whole purpose is proving zero behavior change).

- [ ] **Step 5: Full backend regression**

Run: `dotnet test recepcaototem.sln`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add recepcaototem/Features/Professionals/ProfessionalPhotoMutation.cs recepcaototem/Features/Professionals/ProfessionalPhotoEndpoints.cs
git commit -m "refactor(professional): extract photo mutation logic for reuse by self-service endpoints"
```

---

### Task 4: WebP 512×512 normalization

**Files:**
- Modify: `recepcaototem/recepcaototem.csproj` (add package reference)
- Create: `src/GestaoPredio.Application/Files/IImageNormalizer.cs`
- Create: `src/GestaoPredio.Application/Files/NormalizedImage.cs`
- Create: `src/GestaoPredio.Infrastructure/Files/ImageSharpImageNormalizer.cs`
- Modify: `recepcaototem/Features/Professionals/ProfessionalPhotoMutation.cs` (wire the normalization step into `PutAsync`)
- Modify: `recepcaototem/Program.cs` (DI registration)
- Create: `tests/GestaoPredio.UnitTests/ImageSharpImageNormalizerTests.cs`

**Library decision (documented per spec §13.3 instruction to decide in the plan):**
- **Recommended: `SixLabors.ImageSharp`** (NuGet package id `SixLabors.ImageSharp`, pin to the latest stable 3.x release available at implementation time — verify via `dotnet package search SixLabors.ImageSharp` or nuget.org before adding, since this plan does not install it). Reasons: pure managed .NET (no native binaries), so it runs on Railway's Linux containers without extra system packages, matching this repo's existing zero-native-dependency posture (confirmed: no `SkiaSharp`/`Magick.NET`/`System.Drawing` anywhere in any `.csproj`); actively maintained; supports decoding JPEG/PNG/WebP and encoding WebP directly; well-documented `Resize`+crop API for the required 512×512 center-crop.
- **Rejected alternatives:** `SkiaSharp` (native bindings per-platform, heavier container image, more fragile Railway deploys); `Magick.NET` (wraps native ImageMagick binaries, large binary size, licensing friction); `System.Drawing.Common` (Windows-only since .NET 6+, explicitly unsupported on Linux — would break Railway).
- **Licensing note to record, not resolve here:** Six Labors' split license requires a paid commercial license once the product/organization crosses specific revenue/funding thresholds (see sixlabors.com/pricing at implementation time) — confirm license terms still apply favorably before `dotnet add package` in Step 3; if not, fall back to `Magick.NET` and document the container-size tradeoff instead. This check is a real implementation-time gate, not a formality.

**Interfaces:**
- Consumes: nothing new (works on a `Stream` handed to it after validation).
- Produces (consumed by Task 4 Step 6, i.e. wired into `ProfessionalPhotoMutation.PutAsync` itself, and available for Task 5 to depend on transitively via DI):
```csharp
public sealed record NormalizedImage(Stream Content, long Length);

public interface IImageNormalizer
{
    Task<NormalizedImage> NormalizeAsync(Stream source, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write the failing unit test**

```csharp
// tests/GestaoPredio.UnitTests/ImageSharpImageNormalizerTests.cs
using GestaoPredio.Infrastructure.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using Xunit;

namespace GestaoPredio.UnitTests;

public sealed class ImageSharpImageNormalizerTests
{
    [Fact]
    public async Task NormalizeAsync_produces_512x512_webp_from_a_non_square_png()
    {
        using var source = new Image<Rgba32>(300, 600); // tall rectangle, forces a center-crop
        using var input = new MemoryStream();
        await source.SaveAsPngAsync(input);
        input.Position = 0;

        var normalizer = new ImageSharpImageNormalizer();
        var result = await normalizer.NormalizeAsync(input, CancellationToken.None);

        using var output = await Image.LoadAsync(result.Content, CancellationToken.None);
        Assert.Equal(512, output.Width);
        Assert.Equal(512, output.Height);
        Assert.IsType<WebpFormat>(output.Metadata.DecodedImageFormat is WebpFormat f ? f : null!); // or use Image.DetectFormat / Image.Identify per the actual ImageSharp API surface at the installed version — verify exact API before writing this assertion
    }
}
```
(The exact API for asserting "this stream is WebP" varies slightly by ImageSharp version — at implementation time, use `await Image.IdentifyAsync(resultStreamCopy)` and assert on `.Metadata.DecodedImageFormat` or the simpler `WebpFormat.Instance.Equals(...)`; check the installed package's docs rather than guessing further here.)

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test tests/GestaoPredio.UnitTests --filter "FullyQualifiedName~ImageSharpImageNormalizerTests"`
Expected: FAIL — `ImageSharpImageNormalizer` does not exist; also the package is not yet referenced (compile error).

- [ ] **Step 3: Add the package reference (implementation-time install — this is the one point in this entire plan where a new external dependency is added; confirm license terms per the note above immediately before running this)**

```bash
cd recepcaototem && dotnet add package SixLabors.ImageSharp
```
Also add the same package reference (or just a project reference, since `Infrastructure` already references nothing external for imaging) to `src/GestaoPredio.Infrastructure/GestaoPredio.Infrastructure.csproj` — the normalizer implementation lives there, next to `FileSystemPrivateFileStorage`.

- [ ] **Step 4: Define the interface and result type**

```csharp
// src/GestaoPredio.Application/Files/NormalizedImage.cs
namespace GestaoPredio.Application.Files;

public sealed record NormalizedImage(Stream Content, long Length);
```
```csharp
// src/GestaoPredio.Application/Files/IImageNormalizer.cs
namespace GestaoPredio.Application.Files;

public interface IImageNormalizer
{
    Task<NormalizedImage> NormalizeAsync(Stream source, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Implement `ImageSharpImageNormalizer`**

```csharp
// src/GestaoPredio.Infrastructure/Files/ImageSharpImageNormalizer.cs
using GestaoPredio.Application.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GestaoPredio.Infrastructure.Files;

public sealed class ImageSharpImageNormalizer : IImageNormalizer
{
    private const int TargetSize = 512;

    public async Task<NormalizedImage> NormalizeAsync(Stream source, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(source, cancellationToken);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(TargetSize, TargetSize),
            Mode = ResizeMode.Crop, // center-crop to exactly fill 512x512 regardless of source aspect ratio
        }));
        var output = new MemoryStream();
        await image.SaveAsync(output, new WebpEncoder(), cancellationToken);
        output.Position = 0;
        return new NormalizedImage(output, output.Length);
    }
}
```

- [ ] **Step 6: Run the unit test and confirm it passes**

Run: `dotnet test tests/GestaoPredio.UnitTests --filter "FullyQualifiedName~ImageSharpImageNormalizerTests"`
Expected: PASS.

- [ ] **Step 7: Write the failing integration-level test that wires normalization into the mutation flow**

Add to `tests/GestaoPredio.IntegrationTests/ProfessionalPhotoTests.cs` (existing file):
```csharp
[Fact]
public async Task Admin_photo_upload_is_normalized_to_512x512_webp()
{
    var (client, professional) = await CreateProfessionalAsync(); // use this file's existing helper
    var response = await PutPhotoAsync(client, professional.Id, TestImageData.Png(width: 1200, height: 800)); // extend TestImageData.Png to accept dimensions if it doesn't already
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var getResponse = await client.GetAsync($"/api/admin/professionals/{professional.Id}/photo");
    Assert.Equal("image/webp", getResponse.Content.Headers.ContentType?.MediaType);
    using var image = await Image.LoadAsync(await getResponse.Content.ReadAsStreamAsync());
    Assert.Equal(512, image.Width);
    Assert.Equal(512, image.Height);
}
```

- [ ] **Step 8: Run and confirm failure**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~Admin_photo_upload_is_normalized_to_512x512_webp"`
Expected: FAIL — today the endpoint stores the original uploaded bytes/dimensions unchanged.

- [ ] **Step 9: Wire `IImageNormalizer` into `ProfessionalPhotoMutation.PutAsync`**

Insert, in `PutAsync`, right after the existing validation succeeds (`validated.Length != upload.Staged.Size` check passes, i.e. right before the current `storage.CommitAsync(upload.Staged, ...)` call):
```csharp
NormalizedImage normalized;
await using (var validatedContent = await storage.OpenStagedReadAsync(upload.Staged, cancellationToken))
{
    normalized = await imageNormalizer.NormalizeAsync(validatedContent, cancellationToken);
}
var normalizedStaged = await storage.StageAsync(normalized.Content, storageOptions.Value.ProfessionalPhotoMaxBytes, cancellationToken);
await DiscardSafely(storage, upload.Staged); // the original, pre-normalization staged file is no longer needed

string newStorageKey;
try { newStorageKey = await storage.CommitAsync(normalizedStaged, cancellationToken); }
catch { await DiscardSafely(storage, normalizedStaged); throw; }
```
Replace every subsequent reference to `upload.Staged` (in the `PrivateFile.Create(...)` call) with `normalizedStaged`, and change `validated.MimeType`/`validated.Length` to `"image/webp"`/`normalized.Length` in that same `PrivateFile.Create(newStorageKey, "image/webp", normalized.Length, PrivateFilePurposes.ProfessionalPhoto, now)` call. Add `IImageNormalizer imageNormalizer` as a new parameter to `ProfessionalPhotoMutation.PutAsync` (both its own signature and the call site in `ProfessionalPhotoEndpoints.Put`, which now must accept and forward the same DI parameter).

- [ ] **Step 10: Register `IImageNormalizer` in DI**

```csharp
// recepcaototem/Program.cs — next to other Infrastructure service registrations
builder.Services.AddSingleton<IImageNormalizer, ImageSharpImageNormalizer>();
```

- [ ] **Step 11: Run and confirm pass**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~Admin_photo_upload_is_normalized_to_512x512_webp"`
Expected: PASS.

- [ ] **Step 12: Full regression**

Run: `dotnet test recepcaototem.sln`
Expected: PASS — in particular re-run every existing `ProfessionalPhotoTests`/`ProfessionalPhotoFailureTests` case; any test asserting the *original* uploaded MIME type/dimensions come back unchanged will now legitimately need updating to expect `image/webp`/512×512 — this is an intentional, spec-required behavior change (§13.3), not a regression, so update those specific assertions rather than reverting the feature.

- [ ] **Step 13: Commit**

```bash
git add recepcaototem/recepcaototem.csproj src/GestaoPredio.Application/Files/IImageNormalizer.cs src/GestaoPredio.Application/Files/NormalizedImage.cs src/GestaoPredio.Infrastructure/Files/ImageSharpImageNormalizer.cs src/GestaoPredio.Infrastructure/GestaoPredio.Infrastructure.csproj recepcaototem/Features/Professionals/ProfessionalPhotoMutation.cs recepcaototem/Features/Professionals/ProfessionalPhotoEndpoints.cs recepcaototem/Program.cs tests/GestaoPredio.UnitTests/ImageSharpImageNormalizerTests.cs tests/GestaoPredio.IntegrationTests/ProfessionalPhotoTests.cs
git commit -m "feat(professional): normalize uploaded photos to 512x512 WebP"
```

---

### Task 5: Professional self-service photo upload/delete + rate limiter

**Files:**
- Create: `recepcaototem/Features/Professionals/ProfessionalPhotoUploadRateLimiter.cs`
- Modify: `recepcaototem/Features/Professionals/ProfessionalProfileEndpoints.cs` (add `POST`/`DELETE /photo`)
- Modify: `recepcaototem/Program.cs` (register the rate limiter singleton)
- Modify: `recepcaototem/appsettings.json` (add `RateLimiting:PhotoUpload*` keys)
- Create: `tests/GestaoPredio.UnitTests/ProfessionalPhotoUploadRateLimiterTests.cs` (mirror `ProfessionalPresenceTests.cs`'s exact structure — read it first)
- Create: `tests/GestaoPredio.IntegrationTests/ProfessionalSelfPhotoTests.cs`

**Interfaces:**
- Consumes: `ProfessionalPhotoMutation.PutAsync`/`DeleteAsync` (Task 3+4), `ProfessionalProfileResponse` (Task 2), `ProfessionalPhotoDeleteRequest` (already exists, `ProfessionalPhotoEndpoints.cs:16` — reuse literally, do not redefine).
- Produces (consumed by Task 7):
  - `POST /api/professional/me/photo` (multipart: `file`, `concurrencyToken`) → `200 { hasPhoto: true, photoUrl, concurrencyToken }` | `400 INVALID_PROFESSIONAL_PHOTO` | `400 INVALID_CONCURRENCY_TOKEN` | `401` | `404 PROFESSIONAL_PROFILE_NOT_LINKED` | `409 RESOURCE_MODIFIED` | `429` | `503 PHOTO_UNAVAILABLE`.
  - `DELETE /api/professional/me/photo` (`{ concurrencyToken }`) → `200 { hasPhoto: false, photoUrl: null, concurrencyToken }` | same error set minus the photo-specific ones.

- [ ] **Step 1: Write the failing unit test for the new rate limiter (copy `ProfessionalPresenceTests.cs` structure exactly, only changing bucket names/config keys)**

```csharp
// tests/GestaoPredio.UnitTests/ProfessionalPhotoUploadRateLimiterTests.cs — read ProfessionalPresenceTests.cs
// first and mirror its exact test method shapes (it exercises AcquireAsync for IP-bucket exhaustion and
// identifier-bucket exhaustion separately). Example shape:
[Fact]
public async Task AcquireAsync_denies_after_identifier_permit_limit_is_reached()
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["RateLimiting:PhotoUploadIdentifierPermitLimit"] = "2",
        ["RateLimiting:PhotoUploadIpPermitLimit"] = "50",
        ["RateLimiting:PhotoUploadWindowSeconds"] = "600",
    }).Build();
    using var limiter = new ProfessionalPhotoUploadRateLimiter(configuration);
    (await limiter.AcquireAsync("1.1.1.1", "prof-1", CancellationToken.None)).Dispose();
    (await limiter.AcquireAsync("1.1.1.1", "prof-1", CancellationToken.None)).Dispose();
    var thirdLease = await limiter.AcquireAsync("1.1.1.1", "prof-1", CancellationToken.None);
    Assert.False(thirdLease.Acquired); // or whatever property name ProfessionalPresenceRateLimitLease actually exposes — verify against ProfessionalPresenceRateLimiter.cs before writing this assertion
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/GestaoPredio.UnitTests --filter "FullyQualifiedName~ProfessionalPhotoUploadRateLimiterTests"`
Expected: FAIL — class does not exist.

- [ ] **Step 3: Implement `ProfessionalPhotoUploadRateLimiter`, copying `ProfessionalPresenceRateLimiter.cs` structure exactly**

```csharp
// recepcaototem/Features/Professionals/ProfessionalPhotoUploadRateLimiter.cs
// Same shape as ProfessionalPresenceRateLimiter.cs (recepcaototem/Features/Professionals/ProfessionalPresenceRateLimiter.cs):
// two PartitionedRateLimiter<string> fields (ip, identifier), config keys
// RateLimiting:PhotoUploadIpPermitLimit (default 20), RateLimiting:PhotoUploadIdentifierPermitLimit (default 10),
// RateLimiting:PhotoUploadWindowSeconds (default 600) — read from IConfiguration exactly like the presence limiter,
// identifier partition key hashed via SHA-256 the same way, IDisposable, AcquireAsync(string ip, string identifier,
// CancellationToken) returning a lease type analogous to ProfessionalPresenceRateLimitLease.
```

- [ ] **Step 4: Run and confirm pass**

Run: `dotnet test tests/GestaoPredio.UnitTests --filter "FullyQualifiedName~ProfessionalPhotoUploadRateLimiterTests"`
Expected: PASS.

- [ ] **Step 5: Register in DI and config**

```csharp
// recepcaototem/Program.cs — next to builder.Services.AddSingleton<ProfessionalPresenceRateLimiter>();
builder.Services.AddSingleton<ProfessionalPhotoUploadRateLimiter>();
```
```json
// recepcaototem/appsettings.json — inside the existing "RateLimiting" section
"PhotoUploadIdentifierPermitLimit": 10,
"PhotoUploadIpPermitLimit": 20,
"PhotoUploadWindowSeconds": 600
```

- [ ] **Step 6: Write the failing integration tests for the self-service endpoints**

```csharp
// tests/GestaoPredio.IntegrationTests/ProfessionalSelfPhotoTests.cs
[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalSelfPhotoTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Post_me_photo_uploads_and_returns_versioned_photo_url()
    {
        var (client, professional) = await factory.CreateAuthenticatedProfessionalAsync();
        var token = await GetConcurrencyTokenAsync(client);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "concurrencyToken");
        var imageContent = new ByteArrayContent(TestImageData.Png());
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imageContent, "file", "photo.png");

        var response = await client.PostAsync("/api/professional/me/photo", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("hasPhoto").GetBoolean());
        Assert.Contains("/api/professional/me/photo", body.GetProperty("photoUrl").GetString());
    }

    [Fact]
    public async Task Delete_me_photo_removes_photo()
    {
        var (client, professional) = await factory.CreateAuthenticatedProfessionalAsync();
        var uploadToken = await GetConcurrencyTokenAsync(client);
        await UploadAsync(client, uploadToken); // helper wrapping the multipart POST above
        var afterUploadToken = await GetConcurrencyTokenAsync(client);

        var response = await client.PostAsJsonAsync("/api/professional/me/photo/delete-does-not-exist", (object?)null); // placeholder line — DELETE with a body needs HttpRequestMessage, see below
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/professional/me/photo")
        {
            Content = JsonContent.Create(new { concurrencyToken = afterUploadToken }),
        };
        var deleteResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var body = await deleteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task Professional_a_cannot_alter_professional_b_photo()
    {
        var (clientA, _) = await factory.CreateAuthenticatedProfessionalAsync();
        var (_, professionalB) = await factory.CreateAuthenticatedProfessionalAsync();
        var tokenForA = await GetConcurrencyTokenAsync(clientA);
        // clientA's own POST only ever touches its own professional (resolved by identity) — assert the
        // *returned* profile still reflects clientA's professional, never professionalB's, by checking
        // GET /api/professional/me before/after belongs to the same professional id via a DB-side check
        // if the test harness exposes one, or by asserting professionalB's own GET (via clientB) is unaffected.
    }
}
```
(Remove the accidental placeholder `PostAsJsonAsync("/api/professional/me/photo/delete-does-not-exist", ...)` line above before running — it was left in this plan only to flag that a body-bearing DELETE needs `HttpRequestMessage`, not a convenience extension method; do not ship that line.)

- [ ] **Step 7: Run and confirm failure**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~ProfessionalSelfPhotoTests"`
Expected: FAIL — routes don't exist (404).

- [ ] **Step 8: Implement the two new handlers in `ProfessionalProfileEndpoints.cs`**

```csharp
group.MapPost("/photo", async (
    HttpRequest request, HttpContext context, ApplicationDbContext db, IPrivateFileStorage storage,
    IProfessionalPhotoValidator validator, IImageNormalizer imageNormalizer,
    IOptions<PrivateFileStorageOptions> storageOptions, ProfessionalPhotoUploadRateLimiter rateLimiter,
    TimeProvider timeProvider, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
{
    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    await using var lease = await rateLimiter.AcquireAsync(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", userId, cancellationToken);
    if (!lease.Acquired) return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    var professional = await db.Professionals
        .SingleOrDefaultAsync(p => p.ApplicationUserId == userId && p.IsActive, cancellationToken);
    if (professional is null)
        return Results.NotFound(new { code = "PROFESSIONAL_PROFILE_NOT_LINKED", message = "O perfil profissional não está vinculado corretamente." });

    var outcome = await ProfessionalPhotoMutation.PutAsync(professional, request, context, db, storage, validator,
        storageOptions, imageNormalizer, timeProvider, loggerFactory, cancellationToken);
    if (!outcome.Succeeded) return outcome.ErrorResult!;
    return Results.Ok(new { hasPhoto = true, photoUrl = "/api/professional/me/photo", concurrencyToken = ConcurrencyToken.Encode(professional.Version) });
}).AddEndpointFilter<AntiforgeryFilter>();

group.MapDelete("/photo", async (
    ProfessionalPhotoDeleteRequest request, HttpContext context, ApplicationDbContext db,
    IPrivateFileStorage storage, TimeProvider timeProvider, ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    var professional = await db.Professionals
        .SingleOrDefaultAsync(p => p.ApplicationUserId == userId && p.IsActive, cancellationToken);
    if (professional is null)
        return Results.NotFound(new { code = "PROFESSIONAL_PROFILE_NOT_LINKED", message = "O perfil profissional não está vinculado corretamente." });

    var outcome = await ProfessionalPhotoMutation.DeleteAsync(professional, request.ConcurrencyToken, context, db,
        storage, timeProvider, loggerFactory, cancellationToken);
    if (!outcome.Succeeded) return outcome.ErrorResult!;
    return Results.Ok(new { hasPhoto = false, photoUrl = (string?)null, concurrencyToken = ConcurrencyToken.Encode(professional.Version) });
}).AddEndpointFilter<AntiforgeryFilter>();
```
Note the reused, literal `ProfessionalPhotoDeleteRequest` type (from `ProfessionalPhotoEndpoints.cs`) as the DELETE body type — do not declare a second record for this.

- [ ] **Step 9: Run and confirm pass**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~ProfessionalSelfPhotoTests"`
Expected: PASS.

- [ ] **Step 10: Write and confirm the remaining backend-required test cases from spec §21**

Add to `ProfessionalSelfPhotoTests.cs`: unauthenticated POST → 401; no professional link → 404 `PROFESSIONAL_PROFILE_NOT_LINKED`; file > 5 MB → 400 `INVALID_PROFESSIONAL_PHOTO`; corrupted magic bytes → 400; `.jpg`/`.png`/`.webp` all accepted; troca de foto muda `PhotoFileId`; falha simulada no meio do processo preserva a foto anterior (inject a storage double that throws mid-`CommitAsync` in a unit test around `ProfessionalPhotoMutation` directly, since integration tests can't easily simulate mid-flight storage failure); Totem 404 quando profissional sem foto (covered by Task 6's tests instead, do not duplicate here).

- [ ] **Step 11: Run and confirm pass, then full regression**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~ProfessionalSelfPhotoTests"` then `dotnet test recepcaototem.sln`
Expected: PASS.

- [ ] **Step 12: Commit**

```bash
git add recepcaototem/Features/Professionals/ProfessionalPhotoUploadRateLimiter.cs recepcaototem/Features/Professionals/ProfessionalProfileEndpoints.cs recepcaototem/Program.cs recepcaototem/appsettings.json tests/GestaoPredio.UnitTests/ProfessionalPhotoUploadRateLimiterTests.cs tests/GestaoPredio.IntegrationTests/ProfessionalSelfPhotoTests.cs
git commit -m "feat(professional): add self-service photo management"
```

---

### Task 6: Totem `photoUrl` cache-busting

**Files:**
- Modify: `recepcaototem/Features/Totem/TotemEndpoints.cs`
- Modify: `tests/GestaoPredio.IntegrationTests/TotemProfessionalsCarouselTests.cs`

**Interfaces:**
- Consumes: `Professional.PhotoFileId` (already exists), `ProfessionalPhotoStreaming.StreamAsync(..., cacheControl, ...)` (already exists, `cacheControl` already a parameter — this task only changes the literal string passed at this one call site).
- Produces: `TotemProfessionalCardDto.photoUrl` now formatted as `/api/totem/professionals/{id}/photo?v={PhotoFileId}` when a photo exists. No frontend change needed — `TotemProfessionalCarousel.tsx`'s `Photo` component already treats `photoUrl` as an opaque string and only checks it for null/`onError`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/GestaoPredio.IntegrationTests/TotemProfessionalsCarouselTests.cs — add to the existing file
[Fact]
public async Task Professional_with_photo_gets_a_version_busted_photo_url_with_long_lived_cache_header()
{
    var (adminClient, professional) = await CreateProfessionalAsync(); // reuse whatever helper this file already has
    await PutPhotoAsync(adminClient, professional.Id, TestImageData.Png());
    var updated = await GetProfessionalAsync(adminClient, professional.Id); // however this file already fetches current PhotoFileId-bearing state

    var cardsResponse = await Client.GetAsync("/api/totem/professionals");
    var cards = await cardsResponse.Content.ReadFromJsonAsync<JsonElement[]>();
    var card = cards!.Single(c => c.GetProperty("id").GetString() == professional.Id.ToString());
    var photoUrl = card.GetProperty("photoUrl").GetString();
    Assert.Contains("?v=", photoUrl);

    var photoResponse = await Client.GetAsync(photoUrl);
    Assert.Equal("public, max-age=31536000, immutable", photoResponse.Headers.CacheControl?.ToString());
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~TotemProfessionalsCarouselTests"`
Expected: FAIL — `photoUrl` today has no `?v=`, and cache-control is `public, max-age=300`.

- [ ] **Step 3: Implement**

In `recepcaototem/Features/Totem/TotemEndpoints.cs`, change the card-building line:
```csharp
p.PhotoFileId is null ? null : $"/api/totem/professionals/{p.Id}/photo?v={p.PhotoFileId}",
```
and the `ProfessionalPhoto` handler's `StreamAsync` call:
```csharp
return await ProfessionalPhotoStreaming.StreamAsync(professional, db, storage, loggerFactory, context,
    "public, max-age=31536000, immutable", notFoundWhenMetadataUnusable: true, ct);
```
The handler ignores the `v` querystring value entirely (it is never read/bound as a parameter) — it exists purely for the browser's own cache key.

- [ ] **Step 4: Run and confirm pass**

Run: `dotnet test recepcaototem.sln --filter "FullyQualifiedName~TotemProfessionalsCarouselTests"`
Expected: PASS.

- [ ] **Step 5: Regression — confirm the "no photo" and "remove photo" paths still behave**

Existing tests in this file already cover `photoUrl: null` when there is no photo; add one more assertion if not already present: after `DELETE`ing a professional's photo (admin route), the next `GET /api/totem/professionals` shows `photoUrl: null` for that professional again (already implied by existing behavior — `PhotoFileId is null` check — just confirm the existing test suite exercises this path; if it doesn't, add it here since it is explicitly required by spec §21).

Run: `dotnet test recepcaototem.sln`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add recepcaototem/Features/Totem/TotemEndpoints.cs tests/GestaoPredio.IntegrationTests/TotemProfessionalsCarouselTests.cs
git commit -m "feat(totem): version professional photo URLs by PhotoFileId for immediate cache-busting"
```

---

### Task 7: Meu Perfil frontend + crop

**Files:**
- Modify: `recepcaototem/ClientApp/src/api/modules.ts` (add `professionalProfileApi`, `ProfessionalProfileResponse`/`ProfessionalProfileUpdateRequest` types)
- Create: `recepcaototem/ClientApp/src/api/modules.professionalProfile.test.ts`
- Create: `recepcaototem/ClientApp/src/features/professionals/ProfessionalPhotoCropper.tsx`
- Create: `recepcaototem/ClientApp/src/features/professionals/ProfessionalPhotoCropper.test.tsx`
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalProfile.tsx`
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalProfile.test.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx` (swap the `perfil` route)
- Modify: `recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx` (same swap, keep dev router in sync per existing convention)
- Modify: `recepcaototem/ClientApp/src/styles.css` (crop modal + profile page classes, scoped under `.professional-content`)

**Frontend crop library decision (per spec §19 instruction to decide in the plan, after inspecting `package.json`):**
- **Recommended: hand-rolled, no new dependency.** `package.json` has zero UI-interaction libraries today (no `framer-motion`, no `react-dnd`, no existing crop/canvas package) — every drag/zoom/pointer interaction in this codebase (`TotemProfessionalCarousel.tsx`) is hand-rolled directly on native Pointer Events. A 1:1 crop-with-drag-and-zoom is a bounded problem (one `<canvas>`, one image, uniform scale + pan) that does not justify a new dependency, and matches this project's established convention of avoiding UI libraries entirely. Build `ProfessionalPhotoCropper` using the exact `onPointerDown`/`onPointerMove`/`onPointerUp`/`onPointerCancel`/`onPointerLeave` + `setPointerCapture`/`releasePointerCapture` (wrapped in `try/catch` for jsdom, matching `TotemProfessionalCarousel.tsx`'s established pattern) technique for drag, and a simple range `<input type="range">` (or wheel/pinch, decide during implementation) for zoom, drawing the cropped 1:1 region to an offscreen `<canvas>` and exporting via `canvas.toBlob('image/png')` (or `.jpeg` — the server re-validates and re-normalizes regardless, per spec §19, so the export format only needs to pass the existing extension/MIME/magic-byte validator, e.g. PNG is simplest to produce losslessly from canvas).
- **Fallback if hand-rolling proves harder than expected during implementation:** `react-easy-crop` (small, ~5 KB gzipped, itself built on native pointer events, MIT-licensed) — only introduce this if the hand-rolled version cannot reliably handle both mouse and touch within a reasonable implementation budget; if so, add it in this task's Step 3 instead of hand-rolling, and update this section's decision record accordingly before committing.

**Interfaces:**
- Consumes: `apiClient.get/put/postMultipart/delete` (Task 1), `ProfessionalProfileResponse`/`ProfessionalProfileUpdateRequest` shape (Task 2, mirrored as TS types), `POST`/`DELETE /api/professional/me/photo` (Task 5), `PageHeader`/`EmptyState` (`components/PageElements.tsx`), `.professional-content` dark scope (Task 1).
- Produces:
```ts
export interface ProfessionalProfileDto {
  name: string; profession: string; description: string | null; whatsApp: string
  hasPhoto: boolean; photoUrl: string | null; concurrencyToken: string
}
export interface ProfessionalProfileUpdateInput { whatsApp: string; description: string | null; concurrencyToken: string }
export const professionalProfileApi = {
  get(signal?: AbortSignal): Promise<ProfessionalProfileDto>
  update(input: ProfessionalProfileUpdateInput): Promise<ProfessionalProfileDto>
  uploadPhoto(file: Blob, concurrencyToken: string): Promise<{ hasPhoto: boolean; photoUrl: string | null; concurrencyToken: string }>
  deletePhoto(concurrencyToken: string): Promise<{ hasPhoto: boolean; photoUrl: string | null; concurrencyToken: string }>
}
```
`<ProfessionalPhotoCropper file={File} onCancel={() => void} onCropped={(blob: Blob) => void} />` — an isolated modal component, not coupled to `ProfessionalProfile.tsx`'s data layer.

- [ ] **Step 1: Write the failing test for `professionalProfileApi`**

```ts
// recepcaototem/ClientApp/src/api/modules.professionalProfile.test.ts
import { afterEach, expect, test, vi } from 'vitest'
import { apiClient } from './client'
import { professionalProfileApi } from './modules'

afterEach(() => vi.restoreAllMocks())

test('professionalProfileApi.get GETs /api/professional/me', async () => {
  const get = vi.spyOn(apiClient, 'get').mockResolvedValue({ name: 'Ana', profession: 'Fisio', description: null, whatsApp: '+5511999998888', hasPhoto: false, photoUrl: null, concurrencyToken: 'tok' } as never)
  const result = await professionalProfileApi.get()
  expect(get).toHaveBeenCalledWith('/api/professional/me', { signal: undefined })
  expect(result.whatsApp).toBe('+5511999998888')
})

test('professionalProfileApi.update PUTs the editable fields', async () => {
  const put = vi.spyOn(apiClient, 'put').mockResolvedValue({} as never)
  await professionalProfileApi.update({ whatsApp: '11999998888', description: 'Oi', concurrencyToken: 'tok' })
  expect(put).toHaveBeenCalledWith('/api/professional/me', { whatsApp: '11999998888', description: 'Oi', concurrencyToken: 'tok' })
})

test('professionalProfileApi.uploadPhoto POSTs multipart with file and concurrencyToken', async () => {
  const postMultipart = vi.spyOn(apiClient, 'postMultipart').mockResolvedValue({ hasPhoto: true, photoUrl: '/x', concurrencyToken: 'tok2' } as never)
  const file = new Blob(['x'], { type: 'image/png' })
  await professionalProfileApi.uploadPhoto(file, 'tok')
  const [path, form] = postMultipart.mock.calls[0]
  expect(path).toBe('/api/professional/me/photo')
  expect(form.get('concurrencyToken')).toBe('tok')
  expect(form.get('file')).toBe(file)
})

test('professionalProfileApi.deletePhoto DELETEs with concurrencyToken', async () => {
  const del = vi.spyOn(apiClient, 'delete').mockResolvedValue({ hasPhoto: false, photoUrl: null, concurrencyToken: 'tok3' } as never)
  await professionalProfileApi.deletePhoto('tok')
  expect(del).toHaveBeenCalledWith('/api/professional/me/photo', { concurrencyToken: 'tok' })
})
```

- [ ] **Step 2: Run and confirm failure**

Run: `cd recepcaototem/ClientApp && npx vitest run src/api/modules.professionalProfile.test.ts`
Expected: FAIL — `professionalProfileApi` is not exported.

- [ ] **Step 3: Implement `professionalProfileApi` in `modules.ts`**

```ts
export interface ProfessionalProfileDto {
  name: string; profession: string; description: string | null; whatsApp: string
  hasPhoto: boolean; photoUrl: string | null; concurrencyToken: string
}
export interface ProfessionalProfileUpdateInput { whatsApp: string; description: string | null; concurrencyToken: string }
export const professionalProfileApi = {
  get(signal?: AbortSignal) { return apiClient.get<ProfessionalProfileDto>('/api/professional/me', { signal }) },
  update(input: ProfessionalProfileUpdateInput) { return apiClient.put<ProfessionalProfileDto>('/api/professional/me', input) },
  uploadPhoto(file: Blob, concurrencyToken: string) {
    const form = new FormData()
    form.append('concurrencyToken', concurrencyToken)
    form.append('file', file, file instanceof File ? file.name : 'photo.png')
    return apiClient.postMultipart<{ hasPhoto: boolean; photoUrl: string | null; concurrencyToken: string }>('/api/professional/me/photo', form)
  },
  deletePhoto(concurrencyToken: string) {
    return apiClient.delete<{ hasPhoto: boolean; photoUrl: string | null; concurrencyToken: string }>('/api/professional/me/photo', { concurrencyToken })
  },
}
```

- [ ] **Step 4: Run and confirm pass**

Run: `npx vitest run src/api/modules.professionalProfile.test.ts`
Expected: PASS.

- [ ] **Step 5: Write the failing test for `ProfessionalPhotoCropper`**

```tsx
// recepcaototem/ClientApp/src/features/professionals/ProfessionalPhotoCropper.test.tsx
import { render, screen, fireEvent } from '@testing-library/react'
import { expect, test, vi } from 'vitest'
import { ProfessionalPhotoCropper } from './ProfessionalPhotoCropper'

test('calls onCropped with a Blob when the user confirms the crop', async () => {
  const file = new File([new Uint8Array([137, 80, 78, 71])], 'photo.png', { type: 'image/png' })
  const onCropped = vi.fn()
  render(<ProfessionalPhotoCropper file={file} onCancel={() => {}} onCropped={onCropped} />)
  fireEvent.click(screen.getByRole('button', { name: /salvar foto/i }))
  await vi.waitFor(() => expect(onCropped).toHaveBeenCalledWith(expect.any(Blob)))
})

test('calls onCancel when the user dismisses the modal', () => {
  const file = new File([new Uint8Array([137, 80, 78, 71])], 'photo.png', { type: 'image/png' })
  const onCancel = vi.fn()
  render(<ProfessionalPhotoCropper file={file} onCancel={onCancel} onCropped={() => {}} />)
  fireEvent.click(screen.getByRole('button', { name: /cancelar/i }))
  expect(onCancel).toHaveBeenCalled()
})
```
(jsdom does not implement real `<canvas>` pixel operations or `Image` decoding — verify during implementation whether `canvas.toBlob` needs a jsdom polyfill/mock in `src/test/setup.ts`; if so, add a minimal `HTMLCanvasElement.prototype.toBlob` mock there, following whatever existing polyfill pattern `src/test/setup.ts` already uses for other jsdom gaps.)

- [ ] **Step 6: Run and confirm failure**

Run: `npx vitest run src/features/professionals/ProfessionalPhotoCropper.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 7: Implement `ProfessionalPhotoCropper`**

Build a modal (reuse `Modal` from `components/Modal.tsx`) containing: an `<img>` or `<canvas>` preview of `file` (via `URL.createObjectURL`), a drag handler using the exact pointer-capture technique from `TotemProfessionalCarousel.tsx` (`onPointerDown`/`onPointerMove`/`onPointerUp`/`onPointerCancel`/`onPointerLeave`, `setPointerCapture`/`releasePointerCapture` wrapped in `try/catch`) to pan, a zoom `<input type="range">`, a circular preview overlay (CSS `border-radius: 50%` clip, matching spec §19's "preview circular"), "Cancelar" and "Salvar foto" buttons. On "Salvar foto": draw the current pan/zoom transform onto an offscreen 512×512 (or any square size — the server re-normalizes to 512×512 regardless per Task 4, so the client only needs to produce a reasonably-sized square crop) `<canvas>`, call `canvas.toBlob(blob => onCropped(blob!), 'image/png')`.

- [ ] **Step 8: Run and confirm pass**

Run: `npx vitest run src/features/professionals/ProfessionalPhotoCropper.test.tsx`
Expected: PASS.

- [ ] **Step 9: Write the failing test for `ProfessionalProfile` page**

```tsx
// recepcaototem/ClientApp/src/pages/professional/ProfessionalProfile.test.tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalProfileApi } from '../../api/modules'
import { ProfessionalProfile } from './ProfessionalProfile'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalProfileApi: { get: vi.fn(), update: vi.fn(), uploadPhoto: vi.fn(), deletePhoto: vi.fn() },
}))

beforeEach(() => {
  vi.mocked(professionalProfileApi.get).mockResolvedValue({
    name: 'Maria Clara', profession: 'Psicóloga', description: 'Atendimento humanizado.',
    whatsApp: '+5511999998888', hasPhoto: false, photoUrl: null, concurrencyToken: 'tok-1',
  })
})

test('renders read-only name/profession and editable WhatsApp/description', async () => {
  render(<ProfessionalProfile />)
  expect(await screen.findByText('Maria Clara')).toBeInTheDocument()
  expect(screen.getByText('Psicóloga')).toBeInTheDocument()
  expect(screen.getByDisplayValue('+5511999998888')).toBeInTheDocument()
  expect(screen.getByDisplayValue('Atendimento humanizado.')).toBeInTheDocument()
})

test('saves WhatsApp/description via PUT and reflects the returned profile', async () => {
  vi.mocked(professionalProfileApi.update).mockResolvedValue({
    name: 'Maria Clara', profession: 'Psicóloga', description: 'Nova descrição.',
    whatsApp: '+5511988887777', hasPhoto: false, photoUrl: null, concurrencyToken: 'tok-2',
  })
  render(<ProfessionalProfile />)
  await screen.findByDisplayValue('+5511999998888')
  fireEvent.change(screen.getByLabelText(/whatsapp/i), { target: { value: '11988887777' } })
  fireEvent.click(screen.getByRole('button', { name: /salvar/i }))
  await waitFor(() => expect(professionalProfileApi.update).toHaveBeenCalledWith({
    whatsApp: '11988887777', description: 'Atendimento humanizado.', concurrencyToken: 'tok-1',
  }))
  expect(await screen.findByDisplayValue('+5511988887777')).toBeInTheDocument()
})
```

- [ ] **Step 10: Run and confirm failure**

Run: `npx vitest run src/pages/professional/ProfessionalProfile.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 11: Implement `ProfessionalProfile`**

Structure: `<section className="professional-section page-enter">` → `<PageHeader eyebrow="Área do profissional" title="Meu perfil" description="..." />` → loading/error states (`professional-loading`/`form-error`, matching every other professional page) → a `.panel` with: photo circle (current `photoUrl` or initials fallback, matching the Totem carousel's initials logic conceptually but a local implementation — do not import from `features/totem/`), "Trocar foto"/"Remover foto" buttons (disabled while `uploading`), read-only `Name`/`Profession` (plain text, not inputs), editable `whatsApp`/`description` as `.field-input`s inside a form, a "Salvar" `.primary-button` (disabled while `saving` or if nothing changed). "Trocar foto" opens a native `<input type="file" accept="image/png,image/jpeg,image/webp">`, and on file selection opens `ProfessionalPhotoCropper`; its `onCropped(blob)` calls `professionalProfileApi.uploadPhoto(blob, concurrencyToken)` and updates local state from the response. "Remover foto" calls `professionalProfileApi.deletePhoto(concurrencyToken)` directly (no crop step). On `RESOURCE_MODIFIED` from any call, reload via `professionalProfileApi.get()` and show a conflict message (same idiom as `Settings.tsx`/`ProfessionalAvailability.tsx`).

- [ ] **Step 12: Run and confirm pass**

Run: `npx vitest run src/pages/professional/ProfessionalProfile.test.tsx`
Expected: PASS.

- [ ] **Step 13: Wire the route**

```tsx
// recepcaototem/ClientApp/src/App.tsx
<Route path="perfil" element={<ProfessionalProfile />} />
```
Remove the `ProfessionalPlaceholder title="Meu perfil"` route and its now-unused import if `ProfessionalPlaceholder` is no longer referenced anywhere (it still is, by Reservas/Atendimentos/Locações/Financeiro until Tasks 8-12 land — keep the import until the last of those tasks removes the final usage). Apply the identical route swap in `recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx` to keep the dev router in sync (per this repo's existing convention, confirmed during research).

- [ ] **Step 14: Full frontend gates**

Run: `npx vitest run && npx tsc -b && npx vite build && node scripts/verify-production-bundle.mjs`
Expected: PASS.

- [ ] **Step 15: Commit**

```bash
git add recepcaototem/ClientApp/src/api/modules.ts recepcaototem/ClientApp/src/api/modules.professionalProfile.test.ts recepcaototem/ClientApp/src/features/professionals/ProfessionalPhotoCropper.tsx recepcaototem/ClientApp/src/features/professionals/ProfessionalPhotoCropper.test.tsx recepcaototem/ClientApp/src/pages/professional/ProfessionalProfile.tsx recepcaototem/ClientApp/src/pages/professional/ProfessionalProfile.test.tsx recepcaototem/ClientApp/src/App.tsx recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx recepcaototem/ClientApp/src/styles.css
git commit -m "feat(ui): add professional profile photo editor"
```

---

### Task 8: Reservas profissional

**Files:**
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalReservations.tsx`
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalReservations.test.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx`, `recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx`

**Design note (resolving spec §7's open item about reusing `admin/Reservations.tsx`):** `admin/Reservations.tsx` already contains a `professionalMode` branch (gated by role), but it is only mounted at `/admin/reservas`, which a `PROFISSIONAL`-role user can never reach (`ProtectedRoute allowedRoles={['ADMINISTRADOR']}` on `/admin/*`) — so that branch is dead code today. Rather than mounting the admin component at a professional route (which would pull in `.admin-content`-scoped CSS expectations and admin-only imports), this task builds a small dedicated page reusing the same proven skeleton (`PageHeader`/`.panel.table-panel`/`ProfessionalFilterBar`/`.data-table`/`Modal`/`StatusBadge`) against `professionalReservationsApi` only. This keeps the professional bundle free of admin-only code and keeps `.professional-content` as the only CSS scope this page depends on (Task 1). The pre-existing `professionalMode` branch in `admin/Reservations.tsx` is left untouched — removing dead code there is out of scope for this plan.

**Interfaces:**
- Consumes: `professionalReservationsApi.list/detail/requestReschedule/requestCancellation` (already exists, `api/modules.ts:634-654`), `ReservationDto`/`ReservationStatus` (already exists), `PageHeader`/`EmptyState`/`Modal`/`StatusBadge` (Task 1), `ProfessionalFilterBar` (Task 1).
- Produces: nothing new consumed by later tasks (leaf page).

- [ ] **Step 1: Write the failing test**

```tsx
// recepcaototem/ClientApp/src/pages/professional/ProfessionalReservations.test.tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalReservationsApi } from '../../api/modules'
import { ProfessionalReservations } from './ProfessionalReservations'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalReservationsApi: { list: vi.fn(), detail: vi.fn(), requestReschedule: vi.fn(), requestCancellation: vi.fn() },
}))

const reservation = {
  id: 'r1', roomId: 'room1', roomName: 'Sala 1', professionalId: 'p1', professionalName: 'Maria',
  originalReservationId: null, kind: 'NEW' as const, status: 'APPROVED' as const,
  startAt: '2026-09-15T13:00:00Z', endAt: '2026-09-15T14:00:00Z', requestedAt: '2026-09-10T00:00:00Z',
  decidedAt: '2026-09-10T00:00:00Z', rejectionReason: null, createdAt: '2026-09-10T00:00:00Z',
  updatedAt: '2026-09-10T00:00:00Z', concurrencyToken: 'tok-1',
}

beforeEach(() => {
  vi.mocked(professionalReservationsApi.list).mockResolvedValue({ items: [reservation], page: 1, pageSize: 20, totalCount: 1 })
})

test('lists reservations and shows a cancel action but no approve/reject controls', async () => {
  render(<ProfessionalReservations />)
  expect(await screen.findByText('Sala 1')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /solicitar cancelamento/i })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /aprovar/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /recusar/i })).not.toBeInTheDocument()
})

test('cancel action calls requestCancellation with the concurrency token', async () => {
  vi.mocked(professionalReservationsApi.requestCancellation).mockResolvedValue({ ...reservation, status: 'CANCELLED' })
  render(<ProfessionalReservations />)
  fireEvent.click(await screen.findByRole('button', { name: /solicitar cancelamento/i }))
  fireEvent.click(screen.getByRole('button', { name: /confirmar/i })) // if a confirm step exists in the built modal — adjust per actual implementation
  await waitFor(() => expect(professionalReservationsApi.requestCancellation).toHaveBeenCalledWith('r1', 'tok-1'))
})
```

- [ ] **Step 2: Run and confirm failure**

Run: `npx vitest run src/pages/professional/ProfessionalReservations.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `ProfessionalReservations`**

Structure (mirroring `admin/Reservations.tsx`'s non-admin branch, per spec §7): `<div className="page-enter">` → `<PageHeader eyebrow="Agenda" title="Reservas" description="Acompanhe e gerencie suas solicitações de reserva." />` → `<section className="panel table-panel">` → `<div className="table-toolbar">` with a `ProfessionalFilterBar` containing a status `<select className="field-input compact-select">` (`all`/`PENDING`/`APPROVED`/`REJECTED`/`CANCELLED`) and a count `<span>` → loading/error/`EmptyState` → `<div className="table-scroll"><table className="data-table">` with columns Sala, Data, Horário, Status (`<StatusBadge tone={...} label={...} />`, mapping `PENDING→pending/"Pendente"`, `APPROVED→approved/"Aprovada"`, `REJECTED→rejected/"Recusada"`, `CANCELLED→cancelled/"Cancelada"`), Ações (only "Remarcar" for `APPROVED`/`PENDING` and "Cancelar" for any non-terminal status — never render a button for an action without a matching endpoint) → pagination when `totalCount > pageSize` → a `Modal` for remarcar (date/time inputs → `requestReschedule`) and a confirm step for cancelar (`requestCancellation`). Use the `resolveFailure`/`upsert`/`AbortController` idiom already established in `admin/Reservations.tsx` (read it for the exact idiom, do not reinvent).

- [ ] **Step 4: Run and confirm pass**

Run: `npx vitest run src/pages/professional/ProfessionalReservations.test.tsx`
Expected: PASS.

- [ ] **Step 5: Wire the route in `App.tsx` and `dev/DevelopmentApp.tsx`**

```tsx
<Route path="reservas" element={<ProfessionalReservations />} />
```

- [ ] **Step 6: Full frontend gates**

Run: `npx vitest run && npx tsc -b && npx vite build && node scripts/verify-production-bundle.mjs`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add recepcaototem/ClientApp/src/pages/professional/ProfessionalReservations.tsx recepcaototem/ClientApp/src/pages/professional/ProfessionalReservations.test.tsx recepcaototem/ClientApp/src/App.tsx recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx
git commit -m "feat(ui): complete professional reservations page"
```

---

### Task 9: Atendimentos profissional

**Files:**
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalVisits.tsx`
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalVisits.test.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx`, `recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx`

**Interfaces:**
- Consumes: `professionalVisitsApi.list/detail/start/end/cancel` (already exists, `api/modules.ts:668-676`), `VisitDto`/`VisitStatus` (already exists), `PageHeader`/`EmptyState`/`StatusBadge` (Task 1).

- [ ] **Step 1: Write the failing test**

```tsx
// recepcaototem/ClientApp/src/pages/professional/ProfessionalVisits.test.tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalVisitsApi } from '../../api/modules'
import { ProfessionalVisits } from './ProfessionalVisits'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalVisitsApi: { list: vi.fn(), detail: vi.fn(), start: vi.fn(), end: vi.fn(), cancel: vi.fn() },
}))

const waiting = {
  id: 'v1', professionalId: 'p1', professionalName: 'Maria', roomId: 'room1', roomName: 'Sala 1',
  reservationId: null, visitorName: 'João', status: 'WAITING' as const, arrivedAt: '2026-09-12T13:00:00Z',
  serviceStartedAt: null, endedAt: null, cancelledAt: null, createdAt: '2026-09-12T13:00:00Z',
  updatedAt: '2026-09-12T13:00:00Z', concurrencyToken: 'tok-1', history: [],
}

beforeEach(() => {
  vi.mocked(professionalVisitsApi.list).mockImplementation(async (query) => {
    if (query.status === 'WAITING') return { items: [waiting], page: 1, pageSize: 50, totalCount: 1 }
    return { items: [], page: 1, pageSize: 50, totalCount: 0 }
  })
})

test('shows João under Aguardando with an Iniciar atendimento action', async () => {
  render(<ProfessionalVisits />)
  expect(await screen.findByText('João')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /iniciar atendimento/i })).toBeInTheDocument()
})

test('Iniciar atendimento calls start with the concurrency token', async () => {
  vi.mocked(professionalVisitsApi.start).mockResolvedValue({ ...waiting, status: 'IN_SERVICE' })
  render(<ProfessionalVisits />)
  fireEvent.click(await screen.findByRole('button', { name: /iniciar atendimento/i }))
  await waitFor(() => expect(professionalVisitsApi.start).toHaveBeenCalledWith('v1', 'tok-1'))
})
```

- [ ] **Step 2: Run and confirm failure**

Run: `npx vitest run src/pages/professional/ProfessionalVisits.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `ProfessionalVisits`**

Structure: `<div className="page-enter">` → `PageHeader` ("Atendimentos") → three sections/tabs (`ProfessionalFilterBar` as tab-like buttons or three stacked panels — pick one consistent with `.professional-section` conventions), each backed by its own `professionalVisitsApi.list({ status, page:1, pageSize:50, from?, to? })` call per spec §8 (`WAITING`, `IN_SERVICE`, `ENDED` with `from`/`to` = today). Each row: visitante, sala, horário de chegada, `<StatusBadge tone label>` (`WAITING→waiting/"Aguardando"`, `IN_SERVICE→in-service/"Em atendimento"`, `ENDED→inactive/"Encerrado"`, `CANCELLED→overdue/"Cancelado"` — reuse existing CSS tone slugs where they already exist in `styles.css`, only introduce a new tone class if genuinely none fits; verify current `status-*` classes in `styles.css` before deciding), and conditional actions: `Waiting` → "Iniciar atendimento" (`professionalVisitsApi.start`), `InService` → "Encerrar atendimento" (`.end`), any active state → "Cancelar" (`.cancel`). Never render an action for a state the API doesn't support (e.g. no admin-only "Corrigir").

- [ ] **Step 4: Run and confirm pass**

Run: `npx vitest run src/pages/professional/ProfessionalVisits.test.tsx`
Expected: PASS.

- [ ] **Step 5: Wire the route**

```tsx
<Route path="atendimentos" element={<ProfessionalVisits />} />
```
(both `App.tsx` and `dev/DevelopmentApp.tsx`)

- [ ] **Step 6: Full frontend gates + commit**

Run: `npx vitest run && npx tsc -b && npx vite build && node scripts/verify-production-bundle.mjs`
```bash
git add recepcaototem/ClientApp/src/pages/professional/ProfessionalVisits.tsx recepcaototem/ClientApp/src/pages/professional/ProfessionalVisits.test.tsx recepcaototem/ClientApp/src/App.tsx recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx
git commit -m "feat(ui): complete professional attendances page"
```

---

### Task 10: Agenda profissional (Hoje/Semana views)

**Files:**
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalAgenda.tsx` (moved out of `ProfessionalHome.tsx`, then extended)
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalAgenda.test.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/professional/ProfessionalHome.tsx` (remove the old inline `ProfessionalAgenda` export)
- Modify: `recepcaototem/ClientApp/src/App.tsx`, `recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx` (update the import source for `ProfessionalAgenda`)

**Design decision (resolving spec §6's deferred customer-name question):** do not extend `ReservationResponse` with a new `CustomerName` field — no backend change in this task. Per the spec's own "alternativa sem mudança de contrato": show `Visit.VisitorName` when a matching `Visit` exists (joined by `reservation.id === visit.reservationId`, the same correlation already used by `ProfessionalHome.tsx`'s existing `deriveAgendaStatus` helper), and the generic label `"Cliente"` when only a `Reservation` exists with no linked `Visit` yet. This keeps Task 10 purely additive/frontend and avoids reopening backend/migration scope for a cosmetic label.

**Interfaces:**
- Consumes: `professionalReservationsApi.list({ from, to, orderBy: 'asc', status: 'all', page, pageSize })`, `professionalVisitsApi.list({ from, to, status: 'all', page, pageSize })` (both already exist), the existing `deriveAgendaStatus`/`timeLabel`/`shortDateLabel`/`todayWindow` helpers currently private to `ProfessionalHome.tsx` (move them alongside the relocated component, or export them from `ProfessionalHome.tsx` if `ProfessionalDashboard` still needs them — check before deleting; `ProfessionalDashboard` has its own separate fetch/derivation logic per research, so these helpers may be safely moved wholesale).
- Produces: nothing new consumed elsewhere (leaf page), but this task changes the import path other files use for `ProfessionalAgenda` — `App.tsx` and `dev/DevelopmentApp.tsx` must both update from `'../pages/professional/ProfessionalHome'` to `'../pages/professional/ProfessionalAgenda'` for this one named export.

- [ ] **Step 1: Confirm the safety net — run the existing `ProfessionalAgenda`-adjacent tests before moving anything**

Search for any existing test importing `ProfessionalAgenda` from `ProfessionalHome` (none were found by this branch's research, but re-confirm with `Grep` for `ProfessionalAgenda` across `*.test.tsx` before proceeding, since a stale/missed test would need updating in this same step rather than silently breaking).

- [ ] **Step 2: Write the failing test for the Hoje/Semana views**

```tsx
// recepcaototem/ClientApp/src/pages/professional/ProfessionalAgenda.test.tsx
import { render, screen, fireEvent } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalReservationsApi, professionalVisitsApi } from '../../api/modules'
import { ProfessionalAgenda } from './ProfessionalAgenda'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalReservationsApi: { list: vi.fn() },
  professionalVisitsApi: { list: vi.fn() },
}))

beforeEach(() => {
  vi.mocked(professionalReservationsApi.list).mockResolvedValue({
    items: [{ id: 'r1', roomId: 'room1', roomName: 'Sala 1', professionalId: 'p1', professionalName: 'Maria',
      originalReservationId: null, kind: 'NEW', status: 'APPROVED', startAt: '2026-09-12T13:00:00Z',
      endAt: '2026-09-12T14:00:00Z', requestedAt: '2026-09-01T00:00:00Z', decidedAt: '2026-09-01T00:00:00Z',
      rejectionReason: null, createdAt: '2026-09-01T00:00:00Z', updatedAt: '2026-09-01T00:00:00Z', concurrencyToken: 'tok' }],
    page: 1, pageSize: 50, totalCount: 1,
  })
  vi.mocked(professionalVisitsApi.list).mockResolvedValue({ items: [], page: 1, pageSize: 50, totalCount: 0 })
})

test('Hoje view shows Agendado for a reservation with no matching visit and Cliente as the generic name', async () => {
  render(<ProfessionalAgenda />)
  expect(await screen.findByText('Sala 1')).toBeInTheDocument()
  expect(screen.getByText('Cliente')).toBeInTheDocument()
  expect(screen.getByText('Agendado')).toBeInTheDocument()
})

test('switching to Semana view re-fetches with a week-wide range', async () => {
  render(<ProfessionalAgenda />)
  await screen.findByText('Sala 1')
  fireEvent.click(screen.getByRole('button', { name: /semana/i }))
  await vi.waitFor(() => expect(professionalReservationsApi.list).toHaveBeenCalledTimes(2))
})
```

- [ ] **Step 3: Run and confirm failure**

Run: `npx vitest run src/pages/professional/ProfessionalAgenda.test.tsx`
Expected: FAIL — module not found (still defined inside `ProfessionalHome.tsx`).

- [ ] **Step 4: Move and extend**

Move `ProfessionalAgenda` and the helpers it needs (`timeLabel`, `shortDateLabel`, `deriveAgendaStatus`, `agendaStatusClass`) out of `ProfessionalHome.tsx` into the new `ProfessionalAgenda.tsx` file. Add a `view: 'today' | 'week'` local state with two buttons; `today` computes `from`/`to` as the current civil day (reuse the existing `todayWindow()` helper — move it too, or duplicate the tiny date-math if `ProfessionalDashboard` still needs its own copy; check first), `week` computes `from`/`to` spanning the current week, grouped by day in the render. Join `Reservation`s to `Visit`s by `visit.reservationId === reservation.id`; when found, derive status from the `Visit`, else `"Agendado"`/`"Cancelado"` from the `Reservation` alone (exact mapping table already specified in spec §6). Customer-name column: `visit?.visitorName ?? 'Cliente'`.

- [ ] **Step 5: Run and confirm pass**

Run: `npx vitest run src/pages/professional/ProfessionalAgenda.test.tsx`
Expected: PASS.

- [ ] **Step 6: Update import sites**

```tsx
// recepcaototem/ClientApp/src/App.tsx and src/dev/DevelopmentApp.tsx
import { ProfessionalAgenda } from './pages/professional/ProfessionalAgenda' // adjust relative path per file
```
Remove `ProfessionalAgenda` from `ProfessionalHome.tsx`'s exports.

- [ ] **Step 7: Regression — run `ProfessionalDashboard.test.tsx`/`ProfessionalShell.test.tsx`**

Run: `npx vitest run src/pages/professional/`
Expected: PASS — confirms the extraction didn't break `ProfessionalDashboard`'s independent fetch logic or `ProfessionalShell`'s outlet context.

- [ ] **Step 8: Full frontend gates + commit**

Run: `npx vitest run && npx tsc -b && npx vite build && node scripts/verify-production-bundle.mjs`
```bash
git add recepcaototem/ClientApp/src/pages/professional/ProfessionalAgenda.tsx recepcaototem/ClientApp/src/pages/professional/ProfessionalAgenda.test.tsx recepcaototem/ClientApp/src/pages/professional/ProfessionalHome.tsx recepcaototem/ClientApp/src/App.tsx recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx
git commit -m "feat(ui): add Hoje/Semana views to the professional agenda"
```

---

### Task 11: Locações profissional (read-only)

**Files:**
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalLeases.tsx`
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalLeases.test.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx`, `recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx`

**Design decision (resolving spec §9's deferred `status` filter question):** do **not** add the additive `status` querystring to `GET /api/professional/leases`. A professional's own lease list is small (one professional, a handful of rooms/contracts at most) and the existing `page`/`pageSize` response already includes `Status` per row — client-side filtering over the already-fetched page satisfies the spec's read-only requirement without adding backend surface, matching the spec's own instruction: "Se o frontend puder cumprir a spec de forma correta sem isso, não adicionar por conveniência." If a later real usage pattern shows this is insufficient (e.g. professionals with dozens of concurrent leases), revisit as a separate additive change — not in this plan.

**Interfaces:**
- Consumes: `professionalLeasesApi.list/detail` (already exists, read-only, `api/modules.ts:601-608`), `ProfessionalLeaseDto`/`LeaseStatus` (already exists), `PageHeader`/`EmptyState`/`StatusBadge` (Task 1).

- [ ] **Step 1: Write the failing test**

```tsx
// recepcaototem/ClientApp/src/pages/professional/ProfessionalLeases.test.tsx
import { render, screen } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalLeasesApi } from '../../api/modules'
import { ProfessionalLeases } from './ProfessionalLeases'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalLeasesApi: { list: vi.fn(), detail: vi.fn() },
}))

beforeEach(() => {
  vi.mocked(professionalLeasesApi.list).mockResolvedValue({
    items: [{ id: 'l1', tenantName: 'Maria Clara', roomId: 'room1', roomName: 'Sala 1', mode: 'MONTHLY',
      contractedRate: 1800, billingStartAt: '2026-01-01T00:00:00Z', billingDueDay: 5,
      occupancyStartAt: '2026-01-01T00:00:00Z', occupancyEndAt: null, status: 'ATIVA' }],
    page: 1, pageSize: 20, totalCount: 1,
  })
})

test('lists leases read-only with no edit controls', async () => {
  render(<ProfessionalLeases />)
  expect(await screen.findByText('Sala 1')).toBeInTheDocument()
  expect(screen.getByText('ATIVA')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /editar/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /excluir/i })).not.toBeInTheDocument()
})
```

- [ ] **Step 2: Run and confirm failure**

Run: `npx vitest run src/pages/professional/ProfessionalLeases.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `ProfessionalLeases`**

`PageHeader` ("Locações") → `.panel.table-panel` → `ProfessionalFilterBar` with a client-side status `<select>` filtering the already-fetched `items` in memory (no new query param sent to the server) → `.data-table` columns: sala, período (`occupancyStartAt`–`occupancyEndAt` or "Em aberto"), valor (`contractedRate`), status (`<StatusBadge>`). No action buttons at all.

- [ ] **Step 4: Run and confirm pass, wire route, full gates, commit**

Run: `npx vitest run src/pages/professional/ProfessionalLeases.test.tsx`
```tsx
<Route path="locacoes" element={<ProfessionalLeases />} />
```
Run: `npx vitest run && npx tsc -b && npx vite build && node scripts/verify-production-bundle.mjs`
```bash
git add recepcaototem/ClientApp/src/pages/professional/ProfessionalLeases.tsx recepcaototem/ClientApp/src/pages/professional/ProfessionalLeases.test.tsx recepcaototem/ClientApp/src/App.tsx recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx
git commit -m "feat(ui): complete professional leases page"
```

---

### Task 12: Financeiro profissional (read-only)

**Files:**
- Create: `recepcaototem/ClientApp/src/api/modules.professionalFinance.test.ts` (new API module — this endpoint has **no** frontend wrapper today, confirmed by research)
- Modify: `recepcaototem/ClientApp/src/api/modules.ts` (add `professionalFinanceApi` + `FinancialChargeDto`/`FinancialChargeStatus` types)
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalFinance.tsx`
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalFinance.test.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx`, `recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx`

**Interfaces:**
- Consumes: `GET /api/professional/finance/charges` (backend already exists and is scoped to the authenticated professional — `FinanceEndpoints.ListProfessional`, per spec §1/§10 — but has **no** existing frontend module, this task adds one for the first time).
- Produces: `professionalFinanceApi.list(query)` / `.detail(id)`, `FinancialChargeDto`.

- [ ] **Step 1: Write the failing test for the new API module**

```ts
// recepcaototem/ClientApp/src/api/modules.professionalFinance.test.ts
import { afterEach, expect, test, vi } from 'vitest'
import { apiClient } from './client'
import { professionalFinanceApi } from './modules'

afterEach(() => vi.restoreAllMocks())

test('professionalFinanceApi.list GETs /api/professional/finance/charges with filters', async () => {
  const get = vi.spyOn(apiClient, 'get').mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 } as never)
  await professionalFinanceApi.list({ status: 'all', page: 1, pageSize: 20 })
  expect(get).toHaveBeenCalledWith('/api/professional/finance/charges', { query: { status: 'all', page: 1, pageSize: 20 }, signal: undefined })
})
```

- [ ] **Step 2: Run and confirm failure**

Run: `npx vitest run src/api/modules.professionalFinance.test.ts`
Expected: FAIL — `professionalFinanceApi` not exported.

- [ ] **Step 3: Implement `professionalFinanceApi` in `modules.ts`**

```ts
export type FinancialChargeStatus = 'PENDING' | 'OVERDUE' | 'PAID' | 'CANCELLED'
export interface FinancialChargeDto {
  id: string; leaseId: string; professionalId: string; tenantId: string
  referencePeriodStart: string; referencePeriodEnd: string; dueDate: string
  calculatedAmount: number; finalAmount: number; status: FinancialChargeStatus
  calculationDetails: string; adjustmentReason: string | null; cancellationReason: string | null
  paidAt: string | null; createdAt: string; updatedAt: string; concurrencyToken: string
}
export const professionalFinanceApi = {
  list(query: { status: FinancialChargeStatus | 'all', page: number, pageSize: number, referenceFrom?: string, referenceTo?: string }, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<FinancialChargeDto>>('/api/professional/finance/charges', { query: { ...query }, signal })
  },
  detail(id: string, signal?: AbortSignal) {
    return apiClient.get<FinancialChargeDto>(`/api/professional/finance/charges/${encodeURIComponent(id)}`, { signal })
  },
}
```
(Match field casing exactly to the backend's `FinancialChargeResponse` as serialized — confirm the project's JSON naming policy, e.g. camelCase, by checking one existing DTO's real wire shape via an existing test/response fixture before finalizing field names.)

- [ ] **Step 4: Run and confirm pass**

Run: `npx vitest run src/api/modules.professionalFinance.test.ts`
Expected: PASS.

- [ ] **Step 5: Write the failing test for the page**

```tsx
// recepcaototem/ClientApp/src/pages/professional/ProfessionalFinance.test.tsx
import { render, screen } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalFinanceApi } from '../../api/modules'
import { ProfessionalFinance } from './ProfessionalFinance'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalFinanceApi: { list: vi.fn(), detail: vi.fn() },
}))

beforeEach(() => {
  vi.mocked(professionalFinanceApi.list).mockResolvedValue({
    items: [{ id: 'c1', leaseId: 'l1', professionalId: 'p1', tenantId: 't1',
      referencePeriodStart: '2026-09-01T00:00:00Z', referencePeriodEnd: '2026-09-30T00:00:00Z',
      dueDate: '2026-09-05', calculatedAmount: 1800, finalAmount: 1800, status: 'OVERDUE',
      calculationDetails: '', adjustmentReason: null, cancellationReason: null, paidAt: null,
      createdAt: '2026-09-01T00:00:00Z', updatedAt: '2026-09-01T00:00:00Z', concurrencyToken: 'tok' }],
    page: 1, pageSize: 20, totalCount: 1,
  })
})

test('shows a summary of open amount and a table with computed OVERDUE status, no write actions', async () => {
  render(<ProfessionalFinance />)
  expect(await screen.findByText('OVERDUE')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /marcar como pago/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /excluir/i })).not.toBeInTheDocument()
})
```

- [ ] **Step 6: Run and confirm failure, implement, confirm pass**

Run: `npx vitest run src/pages/professional/ProfessionalFinance.test.tsx` → FAIL → implement `PageHeader` ("Financeiro") → summary strip (open amount = sum of `finalAmount` where status is `PENDING`/`OVERDUE`, next due date = earliest such `dueDate`) → `.panel.table-panel` → `.data-table` (competência, vencimento, valor, `<StatusBadge>`), no actions at all → PASS.

- [ ] **Step 7: Wire route, full gates, commit**

```tsx
<Route path="financeiro" element={<ProfessionalFinance />} />
```
Run: `npx vitest run && npx tsc -b && npx vite build && node scripts/verify-production-bundle.mjs`
```bash
git add recepcaototem/ClientApp/src/api/modules.ts recepcaototem/ClientApp/src/api/modules.professionalFinance.test.ts recepcaototem/ClientApp/src/pages/professional/ProfessionalFinance.tsx recepcaototem/ClientApp/src/pages/professional/ProfessionalFinance.test.tsx recepcaototem/ClientApp/src/App.tsx recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx
git commit -m "feat(ui): complete professional finance page"
```
After this task, `ProfessionalPlaceholder` has zero remaining callers in `App.tsx`/`dev/DevelopmentApp.tsx` — confirm with `Grep` and, if true, remove the now-dead export from `ProfessionalHome.tsx` and its associated import in `App.tsx`/`dev/DevelopmentApp.tsx` as part of this same commit (dead-code removal that falls directly out of this task's own change, not scope creep).

---

### Task 13: Disponibilidade visual coerência

**Files:**
- Modify: `recepcaototem/ClientApp/src/styles.css` (only if a real light-token leak is found — see Step 1)
- No test file changes expected unless Step 1 finds a behavioral gap (unlikely — spec explicitly says "não refatorar o domínio").

**Interfaces:** none new — this is a coherence audit, not a feature.

- [ ] **Step 1: Audit `ProfessionalAvailability.tsx` and its sub-components for light-token leakage**

Run `Grep` for `var(--muted)`, `var(--brand)`, `var(--ink)`, `#fff`, `white`, `rgba(255,255,255,.8` etc. across `src/features/availability/AvailabilityEditor.tsx`, `WeeklyPeriodsEditor.tsx`, `OperatingHoursEditor.tsx`'s CSS rules (`.wpe-*`, `.oh-*`, `.availability-*`, `.exception-*`, `.time-input`, `.timefield-*` in `styles.css`). These classes are shared with the Admin `Configurações` page (`Settings.tsx`), which was already fully darkened in the prior Admin visual round via `.admin-content`-scoped overrides — the Professional route (`/profissional/disponibilidade`) renders the **same** `AvailabilityEditor`/`ExceptionsEditor`/`OperatingHoursEditor` components but inside `.professional-content`, not `.admin-content`, so those admin-scoped overrides do **not** apply here. This is the one place in the whole plan where a class shared between Admin and Professional must be "analisada antes de modificar" (per the round's instruction) — confirm this exact scoping gap by rendering `/profissional/disponibilidade` in the manual-preview harness (Task 1 Step 14's technique) and visually checking for light surfaces in the weekly period rows/time fields/exception rows.

- [ ] **Step 2: If a gap is confirmed, add a `.professional-content`-scoped mirror of the equivalent `.admin-content` rules**

Add, immediately after Task 1's `.professional-content` block in `styles.css`, the same `.professional-content .wpe-day`, `.professional-content .time-input`, `.professional-content .timefield-open`, `.professional-content .availability-*`, `.professional-content .exception-*` overrides already written for `.admin-content` in the prior round (copy the exact declarations, only changing the selector prefix) — never touch the shared base rules directly (they still serve the Admin Configurações page, and potentially other unaudited call sites).

- [ ] **Step 3: Manual verification**

Re-render `/profissional/disponibilidade` in the preview harness; confirm no light surface remains, confirm the actual editing behavior (toggle a day, add/remove a period, save, create/edit/delete an exception) is pixel-for-pixel unchanged from before this task — this task is CSS-only.

- [ ] **Step 4: Regression — run the existing availability test suite untouched**

Run: `npx vitest run src/features/availability/ src/pages/professional/ProfessionalAvailability.test.tsx`
Expected: PASS, zero changes to any assertion (proves the domain/logic was not touched).

- [ ] **Step 5: Full frontend gates + commit**

Run: `npx vitest run && npx tsc -b && npx vite build && node scripts/verify-production-bundle.mjs`
```bash
git add recepcaototem/ClientApp/src/styles.css
git commit -m "fix(ui): unify professional availability page with the dark surface scope"
```
If Step 1 finds no gap (i.e. the components already render correctly dark under `.professional-content` because their rules never specialized on `.admin-content` in the first place and were dark by default), skip Steps 2 onward and record that finding instead of a commit — do not commit a no-op change.

---

### Task 14: Responsiveness, empty/loading/error state consolidation

**Files:**
- Modify: `recepcaototem/ClientApp/src/styles.css` (only for concrete findings — no speculative changes)
- No new test files expected unless a genuine component defect is found (in which case, add a regression test to that component's existing test file, following this plan's established pattern).

**Interfaces:** none new.

- [ ] **Step 1: Desktop pass (1920×1080, 1366×768)**

Using the manual-preview harness (Task 1 Step 14's technique, extended to authenticate as `PROFISSIONAL` and mock every new endpoint from Tasks 2/5/6 with representative fixtures), visit every one of the 8 professional routes at both widths. Confirm: sidebar/topbar unchanged from what already exists (this plan does not touch `ProfessionalShell`'s shell chrome); tables (Reservas/Locações/Financeiro) render complete without truncation; Agenda's Hoje/Semana lists are legible; the profile photo editor and crop modal are usable.

- [ ] **Step 2: Tablet/mobile pass (768px, 390×844)**

Same 8 routes. Confirm: drawer opens/closes correctly (already-existing `ProfessionalShell` behavior, just verify it still works with the new Outlet content); `ProfessionalFilterBar` wraps (flex-wrap, added in Task 1) instead of overflowing; Reservas/Locações/Financeiro tables use `.table-scroll`'s own horizontal scroll (already the established Admin pattern — confirm the new pages actually wrap their `<table>` in `.table-scroll`, since this is a per-page implementation detail, not something CSS alone guarantees); Agenda/Atendimentos favor stacked list/card layout over a wide table at narrow widths (matching the already-existing `.professional-agenda-*` pattern — confirm the new `ProfessionalVisits`/`ProfessionalAgenda` layouts follow the same responsive idiom rather than introducing a second one); touch targets ≥44px on every new button (crop modal's drag handle, "Salvar foto", table row actions); the crop modal's drag/zoom works via simulated touch (jsdom pointer-event dispatch in `ProfessionalPhotoCropper.test.tsx`, already covered in Task 7 — this step is the *visual* confirmation, not a new automated test).

- [ ] **Step 3: Record and fix any concrete finding**

For each real defect found (not a hypothetical), apply the minimal CSS/markup fix, following the same "scope to the narrowest safe selector, verify no shared-class regression" discipline used throughout this branch's prior visual rounds. If a fix touches a shared class (e.g. `.table-scroll`, `.data-table`), re-verify Admin/Customer pages are unaffected (visit at least one Admin route and one Customer route in the same preview pass) before committing.

- [ ] **Step 4: Full frontend gates + commit (only if Step 3 produced a change)**

Run: `npx vitest run && npx tsc -b && npx vite build && node scripts/verify-production-bundle.mjs`
```bash
git add recepcaototem/ClientApp/src/styles.css # plus any specific page file touched
git commit -m "fix(ui): responsive polish for professional area pages"
```

---

### Task 15: Full verification and staging preparation (no code changes)

**Files:** none created or modified — this task only runs commands and prepares documentation of what a future, separately-authorized staging pass will need.

**Interfaces:** none.

- [ ] **Step 1: Run every backend gate fresh**

Run, from repo root:
```bash
dotnet restore recepcaototem.sln
dotnet build recepcaototem.sln
dotnet test recepcaototem.sln
```
Expected: all green — this is the first point where the *entire* accumulated backend change set (Tasks 2-6) is exercised together in one run.

- [ ] **Step 2: Run every frontend gate fresh**

Run, from `recepcaototem/ClientApp`:
```bash
npx vitest run
npx tsc -b
npx vite build
node scripts/verify-production-bundle.mjs
```
Expected: all green.

- [ ] **Step 3: Global check**

Run, from repo root: `git diff --check`
Expected: no whitespace errors across the whole accumulated diff.

- [ ] **Step 4: Manually walk the mandatory homologation scenario from spec §23, using the preview harness**

```
Professional → Meu Perfil → seleciona foto → crop/zoom → salva → abre Totem → nova foto aparece
Professional → remove foto → Totem → iniciais aparecem novamente
```
Confirm both lines pass visually in the harness before considering this plan done. Delete the scratch preview harness files afterward — they must never be committed (verify with `git status --porcelain -uall`).

- [ ] **Step 5: Confirm zero migration**

Run: `git log --oneline -- src/GestaoPredio.Infrastructure/Migrations` (from before Task 2's first commit to now) and confirm no new migration file was added by any task in this plan. Cross-check `find . -newer <marker-file-from-before-task-2> -path "*/Migrations/*"` finds nothing new, or simply `git status`/`git diff --stat` across all commits made by this plan shows no file under any `Migrations/` directory.

- [ ] **Step 6: Write the staging procedure as a follow-up note (do not execute any of it)**

Document, in the final report (not in a new file — this plan does not create a staging doc, since spec §22 already documents the exact procedure): push authorized by the user, Railway deploy, smoke of all 8 professional routes with real staging data, real photo upload, Totem confirmation, photo removal → initials, responsiveness on a real device. None of this runs in this task.

- [ ] **Step 7: No commit for this task** — it is verification-only. If any gate fails, return to the task that owns the failing area and fix it there (with its own small commit), then re-run this task's Steps 1-3 from the top.

---

## Self-Review

1. **Spec coverage:** every spec section maps to a task — §5 Dashboard (no task, explicitly "nenhuma mudança funcional", covered by Task 14's visual pass only); §6 Agenda → Task 10; §7 Reservas → Task 8; §8 Atendimentos → Task 9; §9 Locações → Task 11; §10 Financeiro → Task 12; §11 Disponibilidade → Task 13; §12 Meu Perfil (read+write) → Tasks 2, 7; §13 Foto → Tasks 3, 4, 5, 6; §14 DTOs → Tasks 2, 5; §15 Rate limiting → Task 5; §16 Erros → enforced throughout via Global Constraints; §17 Segurança → enforced throughout; §18 Migration → Global Constraints + Task 15 Step 5; §19 Crop → Task 7; §20 Responsividade → Task 14; §21 Testes → distributed across every task's own test steps; §22 Rollout → Task 15 (documented, not executed); §23 Critérios de aceite → covered by the union of all tasks + Task 15's homologation walk.
2. **Placeholder scan:** no `TODO`/`TBD`/"adicionar testes depois" left in this document — every task shows the actual test code (or, where a project-specific helper name is genuinely unknown until the implementer opens the referenced sibling file, an explicit instruction to read that exact file first and substitute the real name, never a vague "figure it out"). The two library decisions the spec deferred (image processing, frontend crop) are resolved with a named package/approach and an explicit rejection rationale, not left open.
3. **Type/name consistency check:** `ProfessionalProfileResponse`/`ProfessionalProfileUpdateRequest` (Task 2) are the exact same names/shapes consumed by Task 5 (self-serve endpoints build the same anonymous shape matching §14's contract) and Task 7 (`ProfessionalProfileDto` on the frontend mirrors the same fields). `PhotoMutationOutcome`/`ProfessionalPhotoMutation.PutAsync`/`DeleteAsync` (Task 3) gain the `IImageNormalizer imageNormalizer` parameter in Task 4 and that exact updated signature is what Task 5's new endpoints call — Task 5's code block already includes the extra parameter. `IImageNormalizer`/`NormalizedImage` (Task 4) are not referenced again until Task 5's DI-injected handler parameter, consistent. `professionalProfileApi`/`professionalFinanceApi` (Tasks 7, 12) match the exact `PagedResponse<T>` generic already used by every other `professional*Api` module.
4. **Migration check:** no task in this plan adds an EF Core migration; Global Constraints states this explicitly twice (top-level and Task 4's package-add is the only new external dependency in the whole plan, and it is a NuGet package, not a schema change); Task 15 Step 5 verifies this at the end.
5. **No parallel photo infrastructure check:** Task 3 extracts existing logic (does not duplicate it); Task 4 adds exactly one new interface (`IImageNormalizer`) for the one genuinely new capability (image resizing/re-encoding), which is explicitly called out in the spec (§13.3) as the sole real gap; no new storage abstraction, no new entity, no new purpose constant beyond the existing `PrivateFilePurposes.ProfessionalPhoto`.
6. **No invented buttons/actions:** every action button specified in Tasks 8, 9, 11, 12 is explicitly tied to an existing backend endpoint (Reservas: remarcar/cancelar only, no aprovar/recusar; Atendimentos: iniciar/encerrar/cancelar only, no corrigir; Locações/Financeiro: zero write actions, confirmed by explicit negative test assertions in each task's Step 1).
