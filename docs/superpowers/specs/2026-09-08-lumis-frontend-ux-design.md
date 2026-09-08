# Lumis frontend — refresh-loop fix and availability/totem UX rework

Date: 2026-09-08
Branch: `codex/reception-backend`
Scope: `recepcaototem/ClientApp/` only. **No backend changes.** No push, no merge, no deploy, no Supabase/production access, no mocks.

## Context

The professional-availability frontend (commits `97771e8`, `81cfb3c`, `986138a`) shipped
with a session-revalidation bug that remounts the whole authenticated app on every window
focus / tab return, and with rough editors for Operating Hours and weekly availability. The
Totem check-in only accepts a pasted code. This spec covers six related fixes/reworks.

Section 1 is already implemented and committed as `d3e3a55` on this branch; it is documented
here for completeness and gets a review pass only.

## Goals

1. No automatic refresh/reload/remount when switching tabs, returning to the window, or
   navigating between screens. Forms in edit keep their state. No `location.reload`,
   polling, or `setTimeout` used as control flow.
2. A clear, responsive Operating Hours editor: visual prefill, "apply to all days",
   per-day open/close, multiple periods per day, "Adicionar período", explicit save.
3. A reliable time selector: native `HH:mm` inputs, not clipped, start < end, no overlap.
4. A clearer "Minha disponibilidade" for professionals: keep both modes, weekly grid with
   add/remove periods, visible global allowed window, no reset on navigation, keep the
   "existing reservations outside availability" warning.
5. Totem check-in with two paths kept side by side: scan a QR with the camera, or
   type/paste a code. Camera path: permission request, start/stop, live decode,
   auto-fill + validate, manual fallback on failure, via a stable library.
6. Responsive layout with no clipped buttons and no controls flush against the viewport
   edge, checked at 1366×768, 1440×900, 1920×1080.

## Non-goals

- Any backend contract, validation rule, or endpoint change.
- Redesign of pages outside Settings, Minha Disponibilidade, and Totem (shared CSS classes
  are adjusted only where they cause a listed defect).
- Switching the styling approach (stay on `styles.css`; Tailwind stays unused).
- Real end-to-end verification of the camera (no webcam in the working environment); the
  non-camera logic and the manual fallback are tested and verified here, the live camera is
  the reviewer's manual check.

## Shared building blocks

### `features/availability/timeRange.ts` (new)

Pure helpers, no React. Consumed by `WeeklyPeriodsEditor`, `OperatingHoursEditor`,
`AvailabilityEditor`.

- `HHMM_RE` and `parseHm(value): number | null` — minutes since midnight, or `null` when
  not `HH:mm`.
- `type Period = { start: string; end: string }` with a field-name adapter at call sites
  (Operating Hours uses `opensAt/closesAt`; availability uses `startTime/endTime`).
- `rangeError(period): boolean` — true when either end is unparseable or `start >= end`.
- `overlappingIndexes(periods): Set<number>` — indexes of periods that overlap another in
  the same day (after sorting by start).
- `periodsHaveErrors(periods): boolean` — `rangeError` on any, or any overlap.

This replaces the ad-hoc `timeToMinutes` / `intervalErrors` in `AvailabilityEditor.tsx` and
the inline logic that `Settings.tsx` currently lacks.

### `features/availability/WeeklyPeriodsEditor.tsx` (new)

The day/period grid shared by Operating Hours (§2) and Custom availability (§4).

```
type DayDraft = { dayOfWeek: string; open: boolean; periods: { start: string; end: string }[] }

type WeeklyPeriodsEditorProps = {
  days: DayDraft[]
  disabled?: boolean
  startLabel: string            // "Abertura" | "Início"
  endLabel: string              // "Fechamento" | "Fim"
  closedLabel: string           // "Fechado" | "Não atende"
  addPeriodLabel?: string       // default "Adicionar período"
  defaultPeriod: { start: string; end: string }   // used when a day is (re)opened / period added
  hintForDay?: (dayOfWeek: string) => string | null   // §4 global-window hint, null in §2
  onChange: (days: DayDraft[]) => void
}
```

Behaviour:

- One card/row per day: day label, an open/close toggle (checkbox styled as a switch), the
  list of period rows, and an "Adicionar período" button.
- Toggle off → `open: false`, periods hidden, `closedLabel` shown. Toggle on → `open: true`
  with `periods: [defaultPeriod]` if it had none.
- Period row: two `<input type="time" step="60">`, an en-dash, a remove button. Remove is
  disabled when it is the only period of an open day (an open day always has ≥ 1 period;
  to have none, close the day).
- Per-row inline error (`.wpe-period-error`) when `rangeError` or the row is in
  `overlappingIndexes`.
- `hintForDay` result, when non-null, renders as a muted read-only line under the day label
  ("Estabelecimento: 08:30–18:30").
- Purely controlled: every mutation calls `onChange` with a new `days` array. No internal
  effects, no data fetching, stable keys (`dayOfWeek`, and period rows keyed by a stable
  per-period `id` added to `DayDraft` — never the array index — so editing one period never
  remounts its sibling inputs).

## §1 — Session revalidation no longer remounts the app (done: `d3e3a55`)

### Root cause

- `SessionProvider` binds `revalidate` to `window` `focus`, `pageshow` (persisted), and a
  `BroadcastChannel`. `revalidate` called `invalidate()` → `setUser(null)` +
  `setStatus('loading')`, then `refresh()`.
- `ProtectedRoute` rendered its `Carregando…` branch whenever `status === 'loading'`,
  regardless of whether the current location was already validated. That branch replaces
  `<Outlet/>`, so the professional/customer shell, the active page, and any form being
  edited were unmounted and then remounted when `refresh()` resolved.
- Effect: every tab return / window focus / bfcache restore re-mounted the authenticated
  tree, re-fired every `GET` (`/api/auth/session` ×2, `/api/professional/me`,
  `/reservations`, `/visits`, `/availability`, `/availability/exceptions`), and reset any
  in-progress edits. With DevTools open, `focus` fires often enough to look like a
  continuous reload loop.
- Secondary: `professionalAvailabilityApi.listExceptions` / the admin variant sent no
  `from`/`to`; the endpoint requires an explicit ≤ 365-day window and returns HTTP 400
  `INVALID_DATE_RANGE` otherwise, so `Promise.all` in the page's `load()` always rejected
  and the editor never mounted.

### Fix (implemented)

- `SessionProvider.refresh(options?: { background?: boolean })`: a background call does not
  `setStatus('loading')` and does not clear `user`; it swaps identity only once the new
  `/api/auth/session` response resolves. The stale-response guard (`generation` counter) is
  unchanged, so a slow old response still cannot overwrite a newer one.
- `revalidate` = `() => { if (!changingAccount.current) void refresh({ background: true }) }`.
  The `pagehide → invalidate` listener is removed (`pageshow` persisted already
  revalidates on bfcache restore).
- `ProtectedRoute` gate is `if (validatedLocation !== location.key)` only — it blocks until
  the current location is validated the first time, then trusts the last known
  `status`/`user`; a later background revalidation that flips `status` to `anonymous`/
  `error` still redirects on the next render through the existing checks.
- `api/modules.ts`: `AvailabilityExceptionRange` type + `defaultExceptionRange()` (today →
  today + 365 days, local `YYYY-MM-DD`); both `listExceptions` functions send
  `query: { from, to }`. Signature stays `listExceptions(signal?, range = defaultExceptionRange())`.
- `ProfessionalAvailability.errorMessage` / `AdminProfessionalAvailability.messageFor` map
  `INVALID_PROFESSIONAL_AVAILABILITY` and `INVALID_DATE_RANGE` to real messages instead of
  the generic fallback.

### Tests (implemented)

- `api/modules.test.ts` — "exception listing always sends the mandatory from/to range":
  both calls include a `from`/`to` `YYYY-MM-DD` query spanning `0 < days ≤ 365`.
- `components/ProtectedRoute.test.tsx` — "a background session revalidation keeps the
  protected subtree mounted and its edits": mount a protected child with a text input,
  type into it, dispatch `window` `focus`, assert `apiClient.get` was called again, the
  input keeps its value, and the child mounted exactly once.

### Remaining work for §1

Review pass only. If the review finds another `status`-driven unmount path (e.g. a consumer
that hard-gates on `status === 'loading'` for a page that holds an editor), fix it the same
way (gate on first-validated, not on transient loading) and add a test.

## §2 — Operating Hours UX (`OperatingHoursEditor` + `Settings.tsx`)

### Component

New `features/availability/OperatingHoursEditor.tsx`. `Settings.tsx` keeps ownership of
data loading/saving and the Room Blocks panel; it renders `<OperatingHoursEditor>` for the
hours section, shrinking its giant JSX return.

```
type OperatingHoursEditorProps = {
  value: OperatingHoursDto            // { configured, days, concurrencyToken }
  pending: boolean
  onSave: (draft: OperatingDraftDay[]) => void | Promise<void>
}
```

Internal draft: `DayDraft[]` (the `WeeklyPeriodsEditor` shape) with `id` per period.

- **Seed:**
  - `value.configured === false` → all 7 days `open: true`, one period `08:30–18:30`, and a
    banner "Sugestão de horário — confirme para salvar." The draft is local; nothing is
    sent until Save.
  - `value.configured === true` → from `value.days`; a day with 0 intervals → `open: false`.
- **Apply-to-all bar** (top of the section): a labelled `opensAt`/`closesAt` pair
  (default `08:30`/`18:30`) + button "Aplicar a todos os dias". Click → every `open` day
  becomes exactly `[{ start, end }]`. If any open day currently has > 1 period, ask for
  confirmation first (`window.confirm`, acceptable here — it is a user-initiated bulk edit,
  not a lifecycle hack).
- **Editing:** delegated to `<WeeklyPeriodsEditor startLabel="Abertura" endLabel="Fechamento"
  closedLabel="Fechado" defaultPeriod={{start:'08:30',end:'18:30'}} />`.
- **Dirty tracking:** compare the draft to the server value (normalised). When dirty, show a
  "Descartar alterações" link (resets the draft to the server value) and register a
  `beforeunload` handler that sets `event.returnValue` (native browser prompt only — no
  custom reload logic).
- **Save:** the single "Salvar horário" button. Disabled while `pending`, or while
  `periodsHaveErrors` for any day, or while not dirty. On click → `onSave(payload)` where
  payload is 7 `OperatingHoursDayDto`: closed day → `intervals: []`; open day → its periods
  mapped to `{ opensAt: start, closesAt: end }` (`HH:mm`).
- **Post-save:** `Settings.tsx` already re-normalises from the response and shows a notice;
  the editor re-seeds from the new `value` (via `key={value.concurrencyToken ?? 'new'}` or a
  `useEffect` on `value` that only re-seeds when not dirty — prefer the `key` remount so
  there is no effect-syncs-state pattern).

### Error handling

- Client validation blocks Save so the backend `INVALID_OPERATING_HOURS` 400 is not hit for
  format issues.
- `OPERATING_HOURS_CONFLICT` (409, "deixaria uma reserva fora do funcionamento") and
  `RESOURCE_MODIFIED` are surfaced by `Settings.tsx`'s existing `errorText` + reload path;
  add an explicit message for `OPERATING_HOURS_CONFLICT`.

### Layout

- Section is a `.panel .settings-card`. Day cards in a responsive grid:
  `grid-template-columns: repeat(auto-fit, minmax(min(100%, 20rem), 1fr))` (one column
  under ~460px, two on 1366-wide content, three on 1920).
- Day header row: label + hint + toggle, `flex-wrap: wrap`.
- Period row: `display:flex; flex-wrap:wrap; gap` so `start – end [x]` wraps instead of
  overflowing on a narrow column.
- Save bar: `position: sticky; bottom: 0` inside the card with `padding-block` and a
  background, so it never sits flush against the viewport bottom.

## §3 — Time selector

- Keep native `<input type="time" step="60">` everywhere (Operating Hours, availability,
  and — unchanged in behaviour — the Room Block `datetime-local` inputs). `step="60"`
  keeps it at `HH:mm` (no seconds spinner).
- `timeRange.ts` provides the validation; `WeeklyPeriodsEditor` renders the inline errors;
  Save is disabled while any error exists (both §2 and §4).
- CSS (`styles.css`): a `.time-input` rule (applied to these inputs) with
  `min-width: 8.5rem; max-width: 10rem; width: 100%` inside a `flex` row that wraps, so the
  control is never clipped to `--:--`. Remove any `overflow: hidden` on the immediate
  period-row container (the native picker is OS-drawn and not actually clipped by CSS, but
  the row must not hide an adjacent wrapped input).
- No custom time dropdown is introduced or kept.

## §4 — Minha disponibilidade (`AvailabilityEditor.tsx`)

- Keep the mode fieldset (`INHERIT_GLOBAL` / `CUSTOM`) and its descriptive copy.
- Replace the hand-rolled per-day interval JSX in the "Agenda personalizada" block with
  `<WeeklyPeriodsEditor startLabel="Início" endLabel="Fim" closedLabel="Não atende"
  addPeriodLabel="Adicionar período" defaultPeriod={{start:'09:00',end:'17:00'}}
  hintForDay={globalWindowLabel} disabled={mode !== 'CUSTOM'} />`.
  - The draft passed in is derived from the existing `AvailabilityDraft` (`days[].intervals`
    → `periods` with `id`; a day with 0 intervals and `mode === 'CUSTOM'` → `open:false`).
  - `onChange` maps back to `AvailabilityDraft` and calls the existing `onChange` prop
    (`setDraft` in the page) — no new state, no effect syncing query data into the draft.
- `globalWindowLabel(dayOfWeek)` reads `value.effectiveDays` (mode INHERIT) /
  the global intervals already present in `value` to render "Estabelecimento: 08:30–18:30"
  or "Estabelecimento: fechado" per day. In `INHERIT_GLOBAL` mode the existing "Agenda
  efetiva" read-only block stays as the primary display; in `CUSTOM` mode the per-day hint
  is the mechanism for "mostrar claramente o horário global permitido".
- Validation via `timeRange.ts`; Save disabled on any range/overlap error (keeps current
  behaviour, now shared).
- The "X agendamento(s) já existente(s) fora da nova disponibilidade" warning
  (`value.existingReservationsOutsideAvailabilityCount`, and the post-save notice) is
  unchanged.
- "Não resetar ao navegar" is delivered by §1. Add a test that renders
  `ProfessionalAvailability` under a real `SessionProvider` + `ProtectedRoute`, edits a
  period, dispatches `window` `focus`, and asserts the edited value survives.

## §5 — Totem check-in QR camera (`TotemCheckIn.tsx` + `useQrScanner` + `qr-scanner`)

### Dependency

Add `"qr-scanner": "^1.4.2"` to `recepcaototem/ClientApp/package.json` dependencies and
`npm install`. v1.4 bundles its decode worker through `new Worker(new URL('./qr-scanner-worker.min.js', import.meta.url))`,
which Vite bundles locally — no CDN, offline-safe. Confirm in `npm run build` output (a
`qr-scanner-worker` chunk appears) and in `verify:production-bundle`.

### `features/totem/useQrScanner.ts` (new)

```
type QrScannerState = 'idle' | 'starting' | 'scanning' | 'denied' | 'unsupported' | 'error'

function useQrScanner(onDecode: (raw: string) => void): {
  videoRef: RefObject<HTMLVideoElement>
  state: QrScannerState
  start: () => Promise<void>
  stop: () => void
}
```

- Lazy `import('qr-scanner')` inside `start()` so the library is not in the initial Totem
  bundle path until the user opens the camera segment.
- `QrScanner.hasCamera()` → `unsupported` when false.
- `new QrScanner(video, result => onDecode(result.data), { preferredCamera: 'environment',
  highlightScanRegion: true, maxScansPerSecond: 5 })`; `await scanner.start()`.
- Map a `start()` rejection: `NotAllowedError`/`SecurityError` → `denied`; anything else →
  `error`.
- `stop()` → `scanner.stop()`; `destroy()` on unmount. The hook also `stop()`s itself when
  it is unmounted. No timers.
- `onDecode` is wrapped so it fires once and then `stop()`s (avoid repeat decodes of the
  same frame).

### `TotemCheckIn.tsx`

- A segmented control: **"Escanear QR"** | **"Digitar código"** (`segment` state,
  default `'scan'` when `QrScanner.hasCamera()` resolved true, else `'manual'`).
- Scan segment: a framed `<video>`, and a button that is
  `Ativar câmera` (`idle`) / `Parar` (`scanning`) / `Ativar câmera` again (`error`/`denied`).
  States rendered:
  - `starting` → "Abrindo a câmera…"
  - `scanning` → the video + "Aponte o QR Code para a câmera"
  - `denied` → "Permissão de câmera negada. Use o código abaixo." + auto-switch to
    `manual`, with a "Tentar câmera novamente" affordance.
  - `unsupported` / `error` → similar, auto-switch to `manual`.
- Manual segment: the current form, unchanged, kept as the always-available fallback.
- On decode: `normalizeToken(raw)` — trim; if `raw` parses as a URL, take the last path
  segment or a `token`/`code` query param; strip surrounding whitespace. Set `token`, stop
  the scanner, switch to a "validando" state, and call the existing `resolve()` flow, which
  shows the preview card. The user still presses **"Confirmar chegada"** (`confirm()`),
  unchanged.
- The scanner is stopped whenever `segment !== 'scan'` and on unmount.

### Layout

- The Totem card gets a `max-width` and `margin-inline: auto`, `padding-block-end` so the
  confirm button is never flush to the bottom. Video element `width: 100%; aspect-ratio:
  1 / 1; object-fit: cover; border-radius`. Segmented control wraps on narrow widths.

### Tests

`pages/TotemCheckIn.test.tsx` (new), mocking `../features/totem/useQrScanner` and
`../api/modules`:

- default segment is `scan` when a camera is reported, `manual` otherwise;
- switching segments calls `stop()`;
- `denied` state auto-switches to `manual` and keeps the manual form working;
- a decoded raw value is normalised (URL → token) and triggers `resolve()`, then the
  preview renders;
- unmount calls `stop()`.

A small `normalizeToken` unit test covers plain code, code with whitespace, full URL with a
trailing token, and URL with `?token=`.

## §6 — Responsive audit (`styles.css`)

Check Settings (Operating Hours + Room Blocks), Minha Disponibilidade, Totem, and the
shared classes they pull in (`.settings-real-grid`, `.operating-hours-*`,
`.availability-*`, `.row-actions`, `.settings-save`, `.totem-checkin-*`, `.field-label`,
`.field-input`, modal body) at 1366×768, 1440×900, 1920×1080, plus one ~400px pass.

Expected fixes:

- Action/toolbar rows (`.room-blocks-toolbar`, `.availability-editor-actions`,
  `.settings-save`, `.page-header-actions`): `flex-wrap: wrap; gap; align-items` so no
  button is pushed out of view or clipped.
- Grids (`.settings-real-grid`, `.operating-hours-grid`, `.customer-slot-grid`):
  `repeat(auto-fit, minmax(min(100%, 18rem), 1fr))` instead of fixed column counts.
- Bottom breathing room: `padding-block-end` on `.settings-page`, `.availability-page`,
  `.totem-checkin-page`; sticky save bars carry their own padding + background.
- Modal (`Modal` component / `.modal-*`): body `max-height: min(70vh, …); overflow: auto`
  so the footer/actions never clip; already `size="large"` for the availability modal —
  verify the editor scrolls inside it.
- Long day rows wrap rather than causing horizontal page scroll; verify `body`/page
  containers never scroll horizontally at any of the tested widths.

No new breakpoints framework; use the existing media-query style already in `styles.css`.

## Data flow summary

- **Load** (unchanged): `Settings`/`ProfessionalAvailability`/`AdminProfessionalAvailability`
  fetch on mount with an `AbortController`; `listExceptions` now carries a `from`/`to`
  window.
- **Edit:** page holds the draft state; `WeeklyPeriodsEditor` / `OperatingHoursEditor` /
  `AvailabilityEditor` are controlled and call `onChange` up. No component copies server
  data into state via `useEffect`; re-seeding after save is done by remounting the editor
  with a `key` derived from the concurrency token.
- **Save:** only on the explicit button; payload shapes match the current backend contracts
  exactly (7 days, `HH:mm`, closed = empty intervals, concurrency token as received).
- **Totem:** `useQrScanner` owns the camera lifecycle; decode → normalise → existing
  `resolve()` → existing `confirm()`.

## Testing

- New vitest specs: `timeRange.test.ts`, `WeeklyPeriodsEditor.test.tsx`,
  `OperatingHoursEditor.test.tsx`, additions to `AvailabilityEditor.test.tsx` and
  `ProfessionalAvailability.test.tsx`, `TotemCheckIn.test.tsx`, `useQrScanner` covered
  through the Totem test with a mock.
- Assertions that reproduce the reported problems and then pass:
  - Operating Hours with `configured:false` renders 7 open days pre-filled `08:30–18:30`
    and sends nothing until Save; "Aplicar a todos os dias" sets every open day to the top
    pair; closing a day sends `intervals: []`; an inverted or overlapping period disables
    Save with an inline message; the saved payload has exactly 7 days in `HH:mm`.
  - `WeeklyPeriodsEditor` keeps sibling inputs mounted when one period changes (stable
    keys), and re-opening a closed day restores the default period.
  - `AvailabilityEditor` CUSTOM mode shows the per-day global window hint.
  - Totem: segment default, `stop()` on switch/unmount, `denied` → manual fallback,
    URL-encoded token normalisation, decode → `resolve()`.
- Full `npx vitest run` green.
- `npm run build` (tsc + vite) green; a `qr-scanner-worker` asset present;
  `npm run verify:production-bundle` green.
- Manual: `npm run dev`, drive Settings / Minha Disponibilidade / Totem in a browser at
  1366×768, 1440×900, 1920×1080 and ~400px — no clipped buttons, nothing flush to the
  viewport edge, no horizontal page scroll, editors keep edits across a tab switch. Camera
  decode itself is the reviewer's check on a device with a webcam; the manual-code path and
  the permission-denied fallback are verified here.

## Sequencing (for the implementation plan)

1. `timeRange.ts` + tests.
2. `WeeklyPeriodsEditor.tsx` + tests.
3. `OperatingHoursEditor.tsx`, wire into `Settings.tsx`, + tests. (§2, §3)
4. `AvailabilityEditor.tsx` switch to `WeeklyPeriodsEditor` + global hint + tests;
   `ProfessionalAvailability` revalidation-survival test. (§4)
5. `qr-scanner` dependency, `useQrScanner.ts`, `TotemCheckIn.tsx` rework + tests. (§5)
6. `styles.css` responsive pass across all three screens. (§6)
7. §1 review pass; full suite; build; production-bundle check; manual walkthrough; report
   + `git status`.

Each step: failing test first where the change is behavioural (TDD), then implement, then
green. No step touches backend code.
