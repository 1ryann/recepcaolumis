# Professional Availability Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add real Lumis frontend workflows for Professional availability, global operating hours, administrative Professional availability, and room blocks without changing backend rules.

**Architecture:** Extend the existing typed API module with the availability contracts already exposed by the backend. Build one reusable weekly schedule and exception editor, render it in the Professional area and the Operations Professional detail, and keep all availability decisions server-driven. Replace the Settings placeholder with operating-hours and room-block panels while preserving existing visual tokens and responsive behavior.

**Tech Stack:** React 19, TypeScript, React Router, Vitest, Testing Library, lucide-react, existing `apiClient` and Lumis CSS.

**Spec:** User-approved frontend availability requirements in the current task; backend contracts in `recepcaototem/Features/Availability`.

## Global Constraints

- Do not modify backend code, migrations, database state, Supabase, or deployment configuration.
- Use real endpoints only; no AppStore/mock data in the new flows.
- Professional identity comes from the authenticated session; Operations may target a route Professional ID.
- Availability is server-authoritative; frontend validates input for usability but displays API errors unchanged through safe messages.
- Preserve custom intervals when mode is `INHERIT_GLOBAL`; display backend `effectiveDays` separately from stored `days`.
- Use `concurrencyToken` on weekly and exception mutations and reload after `409 RESOURCE_MODIFIED`.

### Task 1: Add typed availability and room-block API clients

**Files:**
- Modify: `recepcaototem/ClientApp/src/api/modules.ts`
- Test: `recepcaototem/ClientApp/src/api/modules.test.ts`

**Interfaces:**
- Produce `ProfessionalAvailabilityDto`, `AvailabilityDayDto`, `AvailabilityIntervalDto`, `AvailabilityExceptionDto`, `OperatingHoursDto`, and `RoomBlockDto` types.
- Produce `professionalAvailabilityApi`, `adminProfessionalAvailabilityApi`, `operatingHoursApi`, and `roomBlocksApi` methods matching existing backend routes and request bodies.

- [ ] Write failing tests asserting each client method uses the exact route, query, and `concurrencyToken` payload.
- [ ] Run `npm test -- --run src/api/modules.test.ts` and confirm the new assertions fail because the methods are absent.
- [ ] Implement the types and API wrappers through `apiClient`.
- [ ] Run the focused API test again and confirm it passes with no changed existing assertions.
- [ ] Commit `feat: add availability frontend api clients`.

### Task 2: Build reusable availability editor components

**Files:**
- Create: `recepcaototem/ClientApp/src/features/availability/AvailabilityEditor.tsx`
- Create: `recepcaototem/ClientApp/src/features/availability/AvailabilityEditor.test.tsx`
- Create: `recepcaototem/ClientApp/src/features/availability/availabilityFormat.ts`
- Create: `recepcaototem/ClientApp/src/features/availability/availabilityFormat.test.ts`
- Modify: `recepcaototem/ClientApp/src/styles.css`

**Interfaces:**
- `AvailabilityEditor` accepts `value`, `pending`, `readOnly`, `onChange`, `onSave`, and `onConflict` props and emits a seven-day `{ mode, days }` payload.
- The editor renders `effectiveDays` as read-only context in `INHERIT_GLOBAL`, keeps stored custom `days`, supports multiple intervals, rejects duplicate/overlap/start-at-end in the UI, and displays the non-blocking outside-reservation warning.
- `ExceptionsEditor` accepts exception rows and CRUD callbacks, preserves each row token, and supports all-day/partial entries with optional reason.

- [ ] Add tests for inherit copy, custom seven-day editing, add/remove interval, overlap validation, all-day and partial exception forms, empty operating-hours message, warning count, and stale-token callback.
- [ ] Run the component tests and verify they fail before implementation.
- [ ] Implement the reusable components with controlled state and accessible labels/buttons.
- [ ] Add scoped responsive styles using existing panel/button/field tokens, with bottom safe-area padding and no fixed viewport positioning.
- [ ] Run focused component tests and confirm they pass.
- [ ] Commit `feat: add reusable availability editors`.

### Task 3: Add Professional “Minha disponibilidade” route

**Files:**
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalAvailability.tsx`
- Create: `recepcaototem/ClientApp/src/pages/professional/ProfessionalAvailability.test.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/professional/ProfessionalHome.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx`
- Modify: `recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx`

**Interfaces:**
- Route `/profissional/disponibilidade` is protected by the existing Professional guard and calls only `/api/professional/availability` and exception routes.

- [ ] Add route/navigation tests proving the new sidebar item points to the page and the page renders loading/error/inherit/custom states from real API calls.
- [ ] Run those tests and verify failure because the route/page is missing.
- [ ] Implement fetch, save, exception CRUD, stale-token reload message, and `existingReservationsOutsideAvailabilityCount` warning handling.
- [ ] Reuse `AvailabilityEditor`/`ExceptionsEditor` and add a logout-safe refresh path through existing session behavior.
- [ ] Run focused Professional tests and confirm pass.
- [ ] Commit `feat: add professional availability page`.

### Task 4: Replace admin Settings placeholder with operating hours and room blocks

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/admin/Settings.tsx`
- Create: `recepcaototem/ClientApp/src/pages/admin/Settings.test.tsx` (or extend existing test if present)
- Modify: `recepcaototem/ClientApp/src/styles.css`

**Interfaces:**
- Settings reads/writes `/api/admin/operating-hours` and lists/creates/updates/cancels `/api/admin/room-blocks` through typed APIs.

- [ ] Add tests for configured and unconfigured operating hours, seven-day interval editing, room-block validation, cancellation token, and API error states.
- [ ] Run focused Settings tests and verify failure while the page still uses AppStore demo data.
- [ ] Implement real data loading and mutations; keep existing general/WhatsApp cards only where they do not conflict, removing demo operating-hours/room-block data.
- [ ] Add responsive grid/card styles and ensure action controls remain inside the scrollable content at 1366x768, 1440x900, and 1920x1080.
- [ ] Run focused Settings tests and confirm pass.
- [ ] Commit `feat: connect operating hours and room blocks settings`.

### Task 5: Add Operations Professional availability editor

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/admin/Professionals.tsx`
- Create/modify: `recepcaototem/ClientApp/src/pages/admin/Professionals.test.tsx`
- Modify: `recepcaototem/ClientApp/src/styles.css`

**Interfaces:**
- The existing Professional detail/edit interaction opens a “Disponibilidade” section and calls `/api/admin/professionals/{id}/availability` plus its exception routes.
- The same editor component is reused; Operations receives the selected Professional ID from the admin page.

- [ ] Add tests for loading the selected Professional schedule, saving as Operations, exception CRUD, warning display, and 403/error rendering.
- [ ] Run focused tests and verify the new assertions fail before wiring the section.
- [ ] Implement the section/modal or detail panel without duplicating editor logic or changing Professional CRUD semantics.
- [ ] Run focused Professional admin tests and confirm pass.
- [ ] Commit `feat: add admin professional availability editor`.

### Task 6: Final route, integration, and responsive verification

**Files:**
- Modify: `recepcaototem/ClientApp/src/frontend-portals.test.ts` if coverage needs extension
- Modify: `recepcaototem/ClientApp/src/styles.css` only for verified fixes

- [ ] Add/adjust tests proving Customer/Totem continue to render backend slots unchanged and no frontend availability rule is duplicated.
- [ ] Run the focused availability test set once.
- [ ] Run `npm run build`.
- [ ] Run the full frontend test suite with `npm test -- --run`.
- [ ] Run `git diff --check` and inspect `git status --short`.
- [ ] Capture a local browser check at the requested viewport sizes if the development server is available; correct only clipping or accessibility issues found.
- [ ] Commit `test: verify professional availability frontend` if a test-only change remains.

