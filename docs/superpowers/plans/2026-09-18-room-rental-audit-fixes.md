# Room Rental Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the findings of the 2026-09-18 room rental audit — B1, D1, D2, G1, G2/G3 — without changing business rules.

**Architecture:** B1 freezes the integration-test server clock by default through the existing `ModulesApiFactory.FreezeTime`/`TestTimeProvider` and derives every test timestamp from `factory.UtcNow`. D1 extends the server-built WhatsApp message. D2 is documentation only. G1 adds an idle auto-return to the static Totem success screen. G2/G3 refine the existing `?inquiryId=` handling in `Leases.tsx`.

**Tech Stack:** ASP.NET Core .NET 10, EF Core/Npgsql, xUnit integration tests (temporary `lumis_test_*` schemas on LumisDev), React/TS/Vite, Vitest + RTL.

**Spec:** `docs/superpowers/specs/2026-09-13-room-rental-and-totem-carousel.md` and `docs/superpowers/specs/2026-09-15-room-rental-ux-fixes.md`.

## Global Constraints

- No migration, no Supabase, no Railway, no env change, no push, no merge, no deploy.
- Never change a business rule (availability, reservations, leases) to make a test pass.
- The 11 files already uncommitted before this plan are never modified, staged or committed by it: `PROJECT_CONTEXT.md`, `README.md`, `docs/operations/{authentication-deployment,configuration,dotnet-10-upgrade,foundation-verification,iis-foundation,professionals-rooms-production-migration}.md`, `recepcaototem/ClientApp/src/styles.css`, `recepcaototem/recepcaototem.csproj`, `tests/GestaoPredio.IntegrationTests/ReservationWorkflowTests.cs`. Every commit stages explicit paths only.
- The finance WhatsApp number lives only in configuration (`Whatsapp__FinanceiroPhoneNumber`); the backend builds the URL.
- Lumis visual pattern preserved; no visual countdown on the Totem success screen.
- G4/G5 are out of scope.

---

### Task 1: B1 — deterministic integration-test clock

**Files:**
- Modify: `tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs` (`ResetAsync`, seed helpers)
- Modify: every `ModulesApiFactory`-based test class that reads `DateTimeOffset.UtcNow`/`DateTime.UtcNow` (except `ReservationWorkflowTests.cs`, which already freezes and must not be touched)

**Interfaces:**
- Produces: `ModulesApiFactory.DefaultTestInstant` (`2026-01-15T12:00:00Z`, 08:00 America/Porto_Velho, Thursday); `ResetAsync()` freezes to it instead of returning to the system clock. `factory.UtcNow`, `factory.FreezeTime(...)`, `factory.UnfreezeTime()` unchanged.

- [ ] Step 1: RED — run `CustomerApiTests.Customer_cancel_and_reschedule_require_the_current_xmin_token` at a wall-clock time where `now+4h+2h` crosses local midnight; record the failure (`Expected OK, Actual Conflict`).
- [ ] Step 2: in `ResetAsync()` replace `UnfreezeTime()` with `FreezeTime(DefaultTestInstant)`; make the factory's own helpers use `UtcNow` instead of `DateTimeOffset.UtcNow`.
- [ ] Step 3: in each converted test class replace `DateTimeOffset.UtcNow`/`DateTime.UtcNow` with `factory.UtcNow` (`factory.UtcNow.UtcDateTime` for `DateTime`). Static helpers without `factory` in scope receive `now` as a parameter.
- [ ] Step 4: grep proves no converted class still reads the real clock.
- [ ] Step 5: GREEN — full `GestaoPredio.IntegrationTests` run; the anchor keeps the largest same-day forward offset (+14h +1h) inside the civil day.
- [ ] Step 6: commit `test: freeze integration test clock by default` (explicit paths only).

### Task 2: D1 — desired period in the finance WhatsApp message

**Files:**
- Modify: `recepcaototem/Features/Rooms/RoomRentalInquiryEndpoints.cs` (message template)
- Modify: `tests/GestaoPredio.IntegrationTests/RoomRentalInquiryTests.cs`
- Modify: `docs/superpowers/specs/2026-09-13-room-rental-and-totem-carousel.md` §8.3, `docs/superpowers/specs/2026-09-15-room-rental-ux-fixes.md` Correção 4

- [ ] Step 1: RED — expected message gains `Período desejado: dd/MM/yyyy até dd/MM/yyyy` right after `Disponibilidade:`; run the test, it fails.
- [ ] Step 2: add the line to the template (invariant culture `dd/MM/yyyy`, same "até" wording as the Admin).
- [ ] Step 3: GREEN; also assert the line for the `AVAILABLE_SOON` case.
- [ ] Step 4: update both specs; commit `feat(rooms): include desired period in finance WhatsApp message`.

### Task 3: D2 — document the 1-hour public photo cache

**Files:** Modify: `docs/superpowers/specs/2026-09-13-room-rental-and-totem-carousel.md` §6.3 and §9.

- [ ] Step 1: replace the `immutable`/1-year statement with `public, max-age=3600`, stating it is intentional: a room can become `OCCUPIED`, after which its photos must stop being served publicly.
- [ ] Step 2: commit `docs(spec): document intentional 1h public room photo cache`.

### Task 4: G1 — privacy-safe auto-return on the Totem success screen

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/TotemRoomInterestSuccess.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/TotemRoomInterestSuccess.test.tsx`
- Modify: spec §7.4 item 4

**Behaviour:** `IDLE_RETURN_MS = 45_000`. A single idle timer starts on mount; any `pointerdown`, `keydown` or `touchstart` on the document restarts it. On expiry → `navigate('/totem', { replace: true })`. "Início" and "Voltar para salas" also use `{ replace: true }`. Static hint text: "Esta tela volta ao início após 45 segundos sem interação." No visual countdown, no polling, no network calls. Refresh keeps redirecting to `/totem/salas` without creating a request.

- [ ] Step 1: RED tests (fake timers): no navigation at 44 999 ms; navigation to `/totem` at 45 000 ms; a `pointerdown` at 30 s postpones it to 75 s; `keydown` also resets; after auto-return the history entry holding the QR state is gone (going back does not render the QR); "Início"/"Voltar" replace; no fetch.
- [ ] Step 2: implement; GREEN; replace the old `getTimerCount() === 0` assertions with "exactly one idle timer".
- [ ] Step 3: update the spec; commit `fix(totem): auto-return room interest success screen for privacy`.

### Task 5: G2/G3 — Admin lease modal inquiry context

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/admin/Leases.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/admin/Leases.test.tsx`

**Behaviour:**
- Closing the "Nova locação" modal without saving removes `inquiryId` from the URL.
- `?inquiryId=` of a `CONVERTED` inquiry: do not open the form; remove `inquiryId`; show "Este interesse já foi convertido em uma locação."; open the existing lease detail through `leaseId`.
- Modal opened from an inquiry shows "Período desejado: dd/mm/aaaa até dd/mm/aaaa" as context only (no contract field is prefilled from it).

- [ ] Step 1: RED tests for the three behaviours; Step 2: implement; Step 3: GREEN; Step 4: commit `fix(admin): tidy lease modal inquiry conversion context`.

### Task 6: Final gates

- [ ] `dotnet build recepcaototem.sln`, `dotnet test recepcaototem.sln`, affected test classes individually, `npx vitest run`, `npx tsc -b`, `npx vite build`, `node scripts/verify-production-bundle.mjs`, `git diff --check`.
- [ ] Confirm the diff of the 11 pre-existing files is byte-identical to the pre-plan baseline.
