# Lumis frontend UX rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the session-revalidation remount bug and rebuild the Operating Hours, weekly-availability, and Totem check-in UIs with a shared day/period editor, a reliable time selector, camera QR scanning, and a responsive layout — frontend only.

**Architecture:** A pure `timeRange.ts` helper and a controlled `WeeklyPeriodsEditor` component are the shared core. `OperatingHoursEditor` (lifted out of `Settings.tsx`) and `AvailabilityEditor` both render `WeeklyPeriodsEditor`. Totem gets a `useQrScanner` hook around the `qr-scanner` library plus a two-segment UI. Responsiveness is a final `styles.css` pass. Section 1 (the remount fix) is already committed as `d3e3a55`; it gets a review task only.

**Tech Stack:** React 18 + TypeScript, react-router-dom, Vite, Vitest + @testing-library/react, `qr-scanner@^1.4`, plain `styles.css`.

**Spec:** `docs/superpowers/specs/2026-09-08-lumis-frontend-ux-design.md`

## Global Constraints

- Work on branch `codex/reception-backend`, in `recepcaototem/ClientApp/` only. No backend (`.cs`) changes. No push, no merge, no deploy, no Supabase/production access.
- No mocks of real app modules in production code. No `location.reload`, polling, or `setTimeout` as control flow.
- Time values are `HH:mm` (24h). Native `<input type="time" step="60">` only — no custom time dropdown.
- Operating Hours payload: exactly 7 days; a closed day sends `intervals: []`; times as `HH:mm`; concurrency token passed through exactly as received (`null` when not configured).
- Operating Hours default period: `08:30`–`18:30`. Professional availability default period (on open / add): `09:00`–`17:00`.
- Button label is "Adicionar período" (not "Adicionar intervalo"). Closed-day label: "Fechado" (Operating Hours) / "Não atende" (availability).
- Period row keys are a stable per-period `id`, never the array index.
- TDD: failing test first for behavioural changes, then implement, then green. Commit after each task. Full `npx vitest run` green before the final report.
- All commands run from `recepcaototem/ClientApp/` unless noted.

---

## Task 1: `timeRange.ts` shared helpers

**Files:**
- Create: `src/features/availability/timeRange.ts`
- Test: `src/features/availability/timeRange.test.ts`

**Interfaces:**
- Produces:
  - `HHMM_RE: RegExp`
  - `parseHm(value: string): number | null` — minutes since midnight, `null` if not `HH:mm`
  - `type Period = { start: string; end: string }`
  - `rangeError(period: Period): boolean` — true if either side unparseable or `start >= end`
  - `overlappingIndexes(periods: Period[]): Set<number>` — indexes overlapping another period in the list
  - `periodsHaveErrors(periods: Period[]): boolean`

- [ ] **Step 1: Write the failing test**

```ts
import { parseHm, rangeError, overlappingIndexes, periodsHaveErrors } from './timeRange'

test('parseHm accepts HH:mm and rejects anything else', () => {
  expect(parseHm('08:30')).toBe(510)
  expect(parseHm('00:00')).toBe(0)
  expect(parseHm('23:59')).toBe(1439)
  expect(parseHm('8:30')).toBeNull()
  expect(parseHm('08:30:00')).toBeNull()
  expect(parseHm('')).toBeNull()
  expect(parseHm('24:00')).toBeNull()
})

test('rangeError flags empty, unparseable and inverted ranges', () => {
  expect(rangeError({ start: '09:00', end: '12:00' })).toBe(false)
  expect(rangeError({ start: '12:00', end: '12:00' })).toBe(true)
  expect(rangeError({ start: '13:00', end: '12:00' })).toBe(true)
  expect(rangeError({ start: '', end: '12:00' })).toBe(true)
})

test('overlappingIndexes finds every period that overlaps another', () => {
  const periods = [
    { start: '09:00', end: '12:00' },
    { start: '11:30', end: '13:00' },
    { start: '14:00', end: '15:00' },
  ]
  expect([...overlappingIndexes(periods)].sort()).toEqual([0, 1])
  expect([...overlappingIndexes([{ start: '09:00', end: '10:00' }, { start: '10:00', end: '11:00' }])]).toEqual([])
})

test('periodsHaveErrors combines range and overlap checks', () => {
  expect(periodsHaveErrors([{ start: '09:00', end: '12:00' }])).toBe(false)
  expect(periodsHaveErrors([{ start: '09:00', end: '12:00' }, { start: '11:00', end: '13:00' }])).toBe(true)
  expect(periodsHaveErrors([{ start: '12:00', end: '09:00' }])).toBe(true)
})
```

- [ ] **Step 2: Run test to verify it fails** — `npx vitest run src/features/availability/timeRange.test.ts` → FAIL (module not found).

- [ ] **Step 3: Implement**

```ts
export const HHMM_RE = /^([01]\d|2[0-3]):[0-5]\d$/

export type Period = { start: string; end: string }

export function parseHm(value: string): number | null {
  if (!HHMM_RE.test(value)) return null
  const [h, m] = value.split(':').map(Number)
  return h * 60 + m
}

export function rangeError({ start, end }: Period): boolean {
  const a = parseHm(start)
  const b = parseHm(end)
  return a === null || b === null || a >= b
}

export function overlappingIndexes(periods: Period[]): Set<number> {
  const rows = periods
    .map((p, index) => ({ index, start: parseHm(p.start), end: parseHm(p.end) }))
    .filter((r): r is { index: number; start: number; end: number } => r.start !== null && r.end !== null && r.start < r.end)
    .sort((a, b) => a.start - b.start)
  const hit = new Set<number>()
  for (let i = 1; i < rows.length; i++) {
    if (rows[i].start < rows[i - 1].end) { hit.add(rows[i].index); hit.add(rows[i - 1].index) }
  }
  return hit
}

export function periodsHaveErrors(periods: Period[]): boolean {
  return periods.some(rangeError) || overlappingIndexes(periods).size > 0
}
```

- [ ] **Step 4: Run test to verify it passes** — same command → PASS.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: add shared time-range helpers for availability editors"`

---

## Task 2: `WeeklyPeriodsEditor` component

**Files:**
- Create: `src/features/availability/WeeklyPeriodsEditor.tsx`
- Test: `src/features/availability/WeeklyPeriodsEditor.test.tsx`

**Interfaces:**
- Consumes: `timeRange.ts` (`rangeError`, `overlappingIndexes`)
- Produces:
  - `type PeriodDraft = { id: string; start: string; end: string }`
  - `type DayDraft = { dayOfWeek: string; open: boolean; periods: PeriodDraft[] }`
  - `function newPeriod(start: string, end: string): PeriodDraft` — id via `crypto.randomUUID()` with a counter fallback
  - `<WeeklyPeriodsEditor>` props:
    ```ts
    {
      days: DayDraft[]
      disabled?: boolean
      startLabel: string
      endLabel: string
      closedLabel: string
      addPeriodLabel?: string          // default "Adicionar período"
      defaultPeriod: { start: string; end: string }
      dayLabel: (dayOfWeek: string) => string
      hintForDay?: (dayOfWeek: string) => string | null
      onChange: (days: DayDraft[]) => void
    }
    ```

Behaviour: one `.wpe-day` block per day (label + optional hint + open/close switch); open day lists period rows (`<input type="time" step="60" class="time-input">` ×2, dash, remove button disabled when it is the only period) and an "Adicionar período" button; closed day shows `closedLabel`. Toggling open on an empty day seeds `[newPeriod(defaultPeriod)]`. Adding appends `newPeriod(defaultPeriod)`. Per-row error text when `rangeError` or index in `overlappingIndexes`. All mutations produce a new `days` array via `onChange`. `disabled` disables every control. No effects, no fetch.

- [ ] **Step 1: Write the failing test**

```tsx
import { fireEvent, render, screen, within } from '@testing-library/react'
import { useState } from 'react'
import { WeeklyPeriodsEditor, type DayDraft, newPeriod } from './WeeklyPeriodsEditor'

const seed = (): DayDraft[] => [
  { dayOfWeek: 'MONDAY', open: true, periods: [newPeriod('08:30', '18:30')] },
  { dayOfWeek: 'TUESDAY', open: false, periods: [] },
]

function Harness(props: Partial<React.ComponentProps<typeof WeeklyPeriodsEditor>> = {}) {
  const [days, setDays] = useState<DayDraft[]>(seed)
  return <WeeklyPeriodsEditor days={days} onChange={setDays} startLabel="Abertura" endLabel="Fechamento"
    closedLabel="Fechado" defaultPeriod={{ start: '08:30', end: '18:30' }} dayLabel={(d) => d} {...props} />
}

test('reopening a closed day seeds the default period', () => {
  render(<Harness />)
  const tuesday = screen.getByTestId('wpe-day-TUESDAY')
  expect(within(tuesday).getByText('Fechado')).toBeInTheDocument()
  fireEvent.click(within(tuesday).getByRole('switch'))
  expect(within(tuesday).getAllByLabelText('Abertura')[0]).toHaveValue('08:30')
  expect(within(tuesday).getAllByLabelText('Fechamento')[0]).toHaveValue('18:30')
})

test('adding a period keeps the existing row mounted and its value', () => {
  render(<Harness />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  fireEvent.change(within(monday).getAllByLabelText('Abertura')[0], { target: { value: '09:15' } })
  fireEvent.click(within(monday).getByRole('button', { name: /adicionar período/i }))
  const opens = within(monday).getAllByLabelText('Abertura')
  expect(opens).toHaveLength(2)
  expect(opens[0]).toHaveValue('09:15')
})

test('an inverted range shows an inline error', () => {
  render(<Harness />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  fireEvent.change(within(monday).getAllByLabelText('Fechamento')[0], { target: { value: '07:00' } })
  expect(within(monday).getByText(/início deve ser anterior ao fim/i)).toBeInTheDocument()
})

test('overlapping periods flag both rows', () => {
  render(<Harness />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  fireEvent.click(within(monday).getByRole('button', { name: /adicionar período/i }))
  const opens = within(monday).getAllByLabelText('Abertura')
  const closes = within(monday).getAllByLabelText('Fechamento')
  fireEvent.change(opens[0], { target: { value: '09:00' } })
  fireEvent.change(closes[0], { target: { value: '12:00' } })
  fireEvent.change(opens[1], { target: { value: '11:00' } })
  fireEvent.change(closes[1], { target: { value: '13:00' } })
  expect(within(monday).getAllByText(/não podem se sobrepor/i).length).toBe(2)
})

test('disabled hides interaction', () => {
  render(<Harness disabled />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  expect(within(monday).getAllByLabelText('Abertura')[0]).toBeDisabled()
  expect(within(monday).getByRole('switch')).toBeDisabled()
})
```

- [ ] **Step 2: Run to verify fail** — `npx vitest run src/features/availability/WeeklyPeriodsEditor.test.tsx` → FAIL.

- [ ] **Step 3: Implement** the component per the Interfaces block. Key points: `newPeriod` uses `typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : 'p' + (++counter)`. Switch is a `<button role="switch" aria-checked={day.open}>`. Row error copy: range → "O início deve ser anterior ao fim.", overlap → "Os períodos não podem se sobrepor." Container test id `wpe-day-${dayOfWeek}`.

- [ ] **Step 4: Run to verify pass** — same command → PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: add WeeklyPeriodsEditor for day/period schedules"`

---

## Task 3: `OperatingHoursEditor` + wire into `Settings.tsx`

**Files:**
- Create: `src/features/availability/OperatingHoursEditor.tsx`
- Create: `src/features/availability/OperatingHoursEditor.test.tsx`
- Modify: `src/pages/admin/Settings.tsx` (replace the inline operating-hours JSX + the three `*HoursInterval` helpers with `<OperatingHoursEditor value={hours} pending={pending} onSave={saveHours} />`; `saveHours` now takes `(days: OperatingHoursDayDto[])`)
- Modify: `src/pages/admin/Settings.test.tsx` (adjust to the new component; keep coverage of save + room blocks)

**Interfaces:**
- Consumes: `WeeklyPeriodsEditor`, `DayDraft`, `newPeriod`, `timeRange.periodsHaveErrors`, `availabilityFormat.AVAILABILITY_DAYS`/`findDayLabel`, `api/modules` types.
- Produces: `<OperatingHoursEditor value: OperatingHoursDto, pending: boolean, onSave: (days: OperatingHoursDayDto[]) => void | Promise<void> />`

Behaviour (spec §2):
- Seed draft from `value`: if `!value.configured` → 7 days `open:true`, `periods:[newPeriod('08:30','18:30')]`, and render a `.wpe-suggestion` banner "Sugestão de horário — confirme para salvar." If configured → per day, `open = intervals.length > 0`, periods from intervals (`opensAt→start`, `closesAt→end`).
- Re-seed when `value` identity changes by keying the inner content on `value.concurrencyToken ?? 'new'` (no effect-syncs-state).
- Apply-to-all bar: `<input type="time">` ×2 (state `bulkStart`/`bulkEnd`, default `08:30`/`18:30`) + button "Aplicar a todos os dias". Click → if any open day has `periods.length > 1`, `window.confirm('Isso substitui os períodos de todos os dias por um único período. Continuar?')`; on confirm set every `open` day to `[newPeriod(bulkStart, bulkEnd)]`.
- Dirty = draft `!==` seed (compare a normalised JSON of `{dayOfWeek, open, periods:[{start,end}]}`). When dirty: show "Descartar alterações" link (reset to seed) and register `beforeunload` (`e.preventDefault(); e.returnValue = ''`) via `useEffect` on `dirty`.
- Save button "Salvar horário": disabled when `pending || !dirty || days.some(d => d.open && periodsHaveErrors(d.periods))`. Builds payload: `AVAILABILITY_DAYS.map(({value:dow}) => ({ dayOfWeek: dow, intervals: day.open ? day.periods.map(p => ({ opensAt: p.start, closesAt: p.end })) : [] }))` → `onSave(payload)`.

- [ ] **Step 1: Failing test** (`OperatingHoursEditor.test.tsx`)

```tsx
import { fireEvent, render, screen, within } from '@testing-library/react'
import { OperatingHoursEditor } from './OperatingHoursEditor'
import type { OperatingHoursDto } from '../../api/modules'

const days7 = (fn: (i: number) => { dayOfWeek: string; intervals: { opensAt: string; closesAt: string }[] }) =>
  ['MONDAY','TUESDAY','WEDNESDAY','THURSDAY','FRIDAY','SATURDAY','SUNDAY'].map((d, i) => ({ ...fn(i), dayOfWeek: d }))

const unconfigured: OperatingHoursDto = { configured: false, days: days7(() => ({ dayOfWeek: '', intervals: [] })), concurrencyToken: null }
const configured: OperatingHoursDto = {
  configured: true,
  days: days7((i) => ({ dayOfWeek: '', intervals: i < 5 ? [{ opensAt: '09:00', closesAt: '17:00' }] : [] })),
  concurrencyToken: 'v1',
}

test('unconfigured seeds 7 open days at 08:30-18:30 and does not save until confirmed', () => {
  const onSave = vi.fn()
  render(<OperatingHoursEditor value={unconfigured} pending={false} onSave={onSave} />)
  expect(screen.getByText(/sugestão de horário/i)).toBeInTheDocument()
  expect(screen.getAllByLabelText('Abertura')).toHaveLength(7)
  expect(screen.getAllByLabelText('Abertura')[0]).toHaveValue('08:30')
  expect(onSave).not.toHaveBeenCalled()
})

test('save sends 7 days with HH:mm and empty intervals for closed days', () => {
  const onSave = vi.fn()
  render(<OperatingHoursEditor value={configured} pending={false} onSave={onSave} />)
  fireEvent.click(within(screen.getByTestId('wpe-day-MONDAY')).getByRole('button', { name: /adicionar período/i }))
  const opens = within(screen.getByTestId('wpe-day-MONDAY')).getAllByLabelText('Abertura')
  fireEvent.change(opens[1], { target: { value: '18:00' } })
  fireEvent.change(within(screen.getByTestId('wpe-day-MONDAY')).getAllByLabelText('Fechamento')[1], { target: { value: '20:00' } })
  fireEvent.click(screen.getByRole('button', { name: /salvar horário/i }))
  const payload = onSave.mock.calls[0][0]
  expect(payload).toHaveLength(7)
  expect(payload[0]).toEqual({ dayOfWeek: 'MONDAY', intervals: [
    { opensAt: '09:00', closesAt: '17:00' }, { opensAt: '18:00', closesAt: '20:00' }] })
  expect(payload[6]).toEqual({ dayOfWeek: 'SUNDAY', intervals: [] })
})

test('apply-to-all sets every open day to the top pair', () => {
  const onSave = vi.fn()
  render(<OperatingHoursEditor value={configured} pending={false} onSave={onSave} />)
  fireEvent.change(screen.getByLabelText('Aplicar abertura'), { target: { value: '10:00' } })
  fireEvent.change(screen.getByLabelText('Aplicar fechamento'), { target: { value: '16:00' } })
  fireEvent.click(screen.getByRole('button', { name: /aplicar a todos os dias/i }))
  expect(within(screen.getByTestId('wpe-day-MONDAY')).getAllByLabelText('Abertura')[0]).toHaveValue('10:00')
  expect(within(screen.getByTestId('wpe-day-TUESDAY')).getAllByLabelText('Fechamento')[0]).toHaveValue('16:00')
})

test('save disabled while a range is invalid', () => {
  render(<OperatingHoursEditor value={configured} pending={false} onSave={vi.fn()} />)
  fireEvent.change(within(screen.getByTestId('wpe-day-MONDAY')).getAllByLabelText('Fechamento')[0], { target: { value: '08:00' } })
  expect(screen.getByRole('button', { name: /salvar horário/i })).toBeDisabled()
})
```

- [ ] **Step 2: Run to verify fail** → FAIL.
- [ ] **Step 3: Implement** `OperatingHoursEditor.tsx`; then edit `Settings.tsx` to use it (`saveHours` signature becomes `async (days: OperatingHoursDayDto[]) => { ... operatingHoursApi.update({ days, concurrencyToken: hours.concurrencyToken }) ... }`; delete `updateHoursInterval`/`addHoursInterval`/`removeHoursInterval`, `emptyHours`, `normalizeHours` stays for nothing — remove if unused; keep `hoursDraft` removed). Add `OPERATING_HOURS_CONFLICT` message to `errorText`.
- [ ] **Step 4: Run** `npx vitest run src/features/availability/OperatingHoursEditor.test.tsx src/pages/admin/Settings.test.tsx` → PASS (fix Settings.test.tsx as needed).
- [ ] **Step 5: Commit** — `git commit -am "feat: rebuild Operating Hours editor with prefill, apply-all, open/close, periods"`

---

## Task 4: `AvailabilityEditor` uses `WeeklyPeriodsEditor` + global-window hint

**Files:**
- Modify: `src/features/availability/AvailabilityEditor.tsx` (replace the `.availability-days` hand-rolled interval map + `replaceInterval`/`intervalErrors`/`addInterval`/`removeInterval` with a `WeeklyPeriodsEditor`; keep mode fieldset, `INHERIT_GLOBAL` effective block, warning, actions)
- Modify: `src/features/availability/availabilityFormat.ts` (keep `normalizeAvailabilityDays`; `timeToMinutes` can be dropped once unused)
- Modify: `src/features/availability/AvailabilityEditor.test.tsx` (update selectors to the new UI; keep the inherit/custom/exception coverage)

**Interfaces:**
- Consumes: `WeeklyPeriodsEditor`, `DayDraft`, `newPeriod`, `timeRange.periodsHaveErrors`.
- The page contract (`value`, `draft`, `onChange`, `onSave`, `pending`, `readOnly`) is unchanged. Internally convert `AvailabilityDraft.days[].intervals[{startTime,endTime}]` ↔ `DayDraft.periods[{id,start,end}]`; a CUSTOM day with 0 intervals → `open:false`.

Details:
- `toDayDrafts(draft)`: for each of the 7 canonical days, `open = draft.mode === 'CUSTOM' && intervals.length > 0`, `periods = intervals.map(i => newPeriod(i.startTime, i.endTime))`. Keep period `id`s stable across renders by memoising on a ref keyed by day+index isn't reliable — instead keep the `DayDraft[]` as the component's own `useMemo` seed only once and thereafter drive it from `onChange`; when `draft` changes identity from outside (save re-seed), re-derive. Simplest: hold `DayDraft[]` in a `useMemo(() => toDayDrafts(draft), [draftSeedKey])` where `draftSeedKey` is `pending` transitions — **actually** keep it controlled: derive `DayDraft[]` on every render from `draft` but preserve ids via a `useRef<Map<string,string>>` keyed by `${dayOfWeek}:${index}`. Provide `newPeriodId(dayOfWeek, index)` that reuses the map.
- `handleChange(days: DayDraft[])`: map back to `AvailabilityDraft`: `mode` stays whatever it is (editing periods implies CUSTOM — if any day is open, force `mode: 'CUSTOM'`), `days` = 7 canonical, `intervals = day.open ? day.periods.map(p => ({ startTime: p.start, endTime: p.end })) : []`. Call `onChange`.
- `hintForDay(dayOfWeek)`: from `normalizeAvailabilityDays(value.effectiveDays)` when INHERIT, or the establishment global (already in `value` — use `effectiveDays` as the allowed window reference in both modes for the hint text) → `"Estabelecimento: 08:30–18:30"` or `"Estabelecimento: fechado"`.
- Save button enable check switches to `periodsHaveErrors` per open day.
- `readOnly` → pass `disabled` to `WeeklyPeriodsEditor` and hide actions (unchanged).

- [ ] **Step 1: Update the failing test** — adjust `AvailabilityEditor.test.tsx`:
  - "custom mode allows adding and removing intervals while rejecting an inverted range" → use "Adicionar período", `getAllByLabelText('Início')`, expect "O início deve ser anterior ao fim."
  - add: "custom mode shows the establishment window per day" → render with `mode:'CUSTOM'`, `effectiveDays:[{dayOfWeek:'MONDAY',intervals:[{startTime:'08:30',endTime:'18:30'}]}]`, expect `within(getByTestId('wpe-day-MONDAY')).getByText(/Estabelecimento: 08:30–18:30/)`.
  - keep the inherit-mode and exceptions tests (exceptions editor is untouched).

- [ ] **Step 2: Run** `npx vitest run src/features/availability/AvailabilityEditor.test.tsx` → FAIL.
- [ ] **Step 3: Implement** the `AvailabilityEditor.tsx` changes.
- [ ] **Step 4: Run** the same + `ProfessionalAvailability.test.tsx` + `AdminProfessionalAvailability.test.tsx` → PASS.
- [ ] **Step 5: Commit** — `git commit -am "feat: weekly availability editor reuses WeeklyPeriodsEditor and shows global window"`

---

## Task 5: availability edits survive a background revalidation (integration test for §1/§4)

**Files:**
- Modify: `src/pages/professional/ProfessionalAvailability.test.tsx` (add one test; keep existing)

**Interfaces:** consumes `SessionProvider`, `ProtectedRoute`, mocked `api/modules` + real `apiClient` session mock (follow the pattern in `components/ProtectedRoute.test.tsx`).

- [ ] **Step 1: Failing test**

```tsx
test('edited periods survive a window focus revalidation', async () => {
  vi.mocked(professionalAvailabilityApi.get).mockResolvedValue({ ...availability, mode: 'CUSTOM' })
  // render ProfessionalAvailability under MemoryRouter + SessionProvider + ProtectedRoute (allowedRoles ['PROFISSIONAL'])
  // with apiClient.get('/api/auth/session') mocked to an identity with roles ['PROFISSIONAL']
  // 1. wait for "Minha disponibilidade"
  // 2. change the first "Início" input to '10:15'
  // 3. fireEvent(window, new Event('focus'))
  // 4. await a tick; assert the "Início" input still has value '10:15'
})
```

(Full body written during execution, mirroring `ProtectedRoute.test.tsx` wiring.)

- [ ] **Step 2: Run** → it should PASS already (the §1 fix is in). If it FAILS, there is another unmount path — fix `ProtectedRoute`/consumer per spec §1 "remaining work", then green.
- [ ] **Step 3: Commit** — `git commit -am "test: availability edits survive a background session revalidation"`

---

## Task 6: add `qr-scanner` + `useQrScanner` hook

**Files:**
- Modify: `recepcaototem/ClientApp/package.json` (add `"qr-scanner": "^1.4.2"` to `dependencies`)
- Create: `src/features/totem/useQrScanner.ts`
- Create: `src/features/totem/normalizeToken.ts`
- Test: `src/features/totem/normalizeToken.test.ts`

**Interfaces:**
- Produces:
  - `normalizeToken(raw: string): string` — trims; if `raw` is a URL, returns its `token`/`code` query param, else its last non-empty path segment; otherwise returns the trimmed string. Strips surrounding whitespace and zero-width chars.
  - `type QrScannerState = 'idle' | 'starting' | 'scanning' | 'denied' | 'unsupported' | 'error'`
  - `useQrScanner(onDecode: (raw: string) => void): { videoRef: React.RefObject<HTMLVideoElement>; state: QrScannerState; start: () => Promise<void>; stop: () => void }`

`useQrScanner`: lazy `const { default: QrScanner } = await import('qr-scanner')` inside `start`. `await QrScanner.hasCamera()` false → `state='unsupported'`. Construct `new QrScanner(videoRef.current!, (r) => { stop(); onDecode(r.data) }, { preferredCamera: 'environment', highlightScanRegion: true, maxScansPerSecond: 5 })`; `await scanner.start()` → `state='scanning'`. Catch: `err?.name === 'NotAllowedError' || err?.name === 'SecurityError'` → `'denied'`, else `'error'`. `stop()` → `scanner?.stop()`. `useEffect` cleanup → `scanner?.destroy()`. No timers.

- [ ] **Step 1: Failing test** (`normalizeToken.test.ts`)

```ts
import { normalizeToken } from './normalizeToken'
test('normalizeToken handles plain codes, whitespace and URLs', () => {
  expect(normalizeToken('  ABC-123  ')).toBe('ABC-123')
  expect(normalizeToken('https://lumis.app/totem/check-in?token=XYZ789')).toBe('XYZ789')
  expect(normalizeToken('https://lumis.app/c/QWE-456')).toBe('QWE-456')
  expect(normalizeToken('lumis://check-in?code=Z1')).toBe('Z1')
  expect(normalizeToken('​TOKEN\n')).toBe('TOKEN')
})
```

- [ ] **Step 2: Run** `npx vitest run src/features/totem/normalizeToken.test.ts` → FAIL.
- [ ] **Step 3: Implement** `normalizeToken.ts` and `useQrScanner.ts`. Run `npm install` after editing `package.json`.
- [ ] **Step 4: Run** the test → PASS; run `npx tsc -b` → clean (hook types compile).
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: add qr-scanner dependency, useQrScanner hook and token normalizer"`

---

## Task 7: Totem two-segment UI (camera + manual)

**Files:**
- Modify: `src/pages/TotemCheckIn.tsx`
- Create: `src/pages/TotemCheckIn.test.tsx`

**Interfaces:** consumes `useQrScanner`, `normalizeToken`, existing `totemApi`.

Behaviour (spec §5): `segment` state `'scan' | 'manual'`. On mount, `QrScanner.hasCamera()` via a `useState` set from `useQrScanner` is not available before start — instead default `segment='scan'` and let the scanner report `unsupported`/`denied`, which auto-switches to `'manual'` through a `useEffect` on `state`. Segmented control buttons ("Escanear QR" / "Digitar código"); switching to `manual` (or unmount) calls `stop()`. Scan panel: framed `<video ref={videoRef}>`, a start/stop button whose label depends on `state`, and status text per state. On decode → `setToken(normalizeToken(raw))` then call `resolve()` (reuse existing) → preview card → existing `confirm()`. Manual panel = the current `<form>` unchanged. Keep all existing states (`preview`, `confirmed`, `error`, `loading`).

- [ ] **Step 1: Failing test** (`TotemCheckIn.test.tsx`), mocking `../features/totem/useQrScanner` and `../api/modules`:

```tsx
vi.mock('../features/totem/useQrScanner')
vi.mock('../api/modules', async (o) => ({ ...await o<any>(), totemApi: { resolveCheckIn: vi.fn(), confirmCheckIn: vi.fn() } }))
// helper to render <MemoryRouter><TotemCheckIn/></MemoryRouter>

test('defaults to the scan segment and shows the manual form when switched', () => { /* click "Digitar código", assert the code input appears, assert stop() called */ })
test('a denied camera auto-switches to manual', () => { /* useQrScanner mock returns state:'denied'; assert manual form visible */ })
test('a decoded URL is normalized and resolves the appointment', async () => {
  // capture the onDecode passed to useQrScanner; call it with 'https://x/totem/check-in?token=ABC'
  // assert totemApi.resolveCheckIn called with 'ABC'; preview renders
})
test('switching away from scan and unmount call stop()', () => { /* ... */ })
```

- [ ] **Step 2: Run** `npx vitest run src/pages/TotemCheckIn.test.tsx` → FAIL.
- [ ] **Step 3: Implement** the Totem rework.
- [ ] **Step 4: Run** the test → PASS.
- [ ] **Step 5: Commit** — `git commit -am "feat: Totem check-in gains camera QR scanning alongside manual code"`

---

## Task 8: responsive `styles.css` pass

**Files:**
- Modify: `src/styles.css`
- Create: `src/features/availability/wpe.css`? No — append to `styles.css` (single stylesheet is the project pattern).

**Changes (spec §3 & §6):**
- Add `.time-input { width: 100%; min-width: 8.5rem; max-width: 10.5rem; min-height: 38px; padding: 0 10px; border: 1px solid #d7e3e1; border-radius: 9px; background: #fff; font: inherit; }` and `.time-input:focus { outline: 0; border-color: #4c9b85; box-shadow: 0 0 0 3px rgba(76,155,133,.12); }`.
- `.wpe-day { padding: 15px 16px; border: 1px solid #e1eaea; border-radius: 14px; background: rgba(255,255,255,.85); }` ; `.wpe-days { display: grid; gap: 10px; }` ; `.wpe-day-head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 10px; }` ; `.wpe-hint { margin-top: 4px; color: var(--muted); font-size: 10px; }` ; `.wpe-period-row { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 9px; margin-top: 8px; }` ; `.wpe-period-error { flex-basis: 100%; color: #a24e4e; font-size: 10px; }` ; switch styles `.wpe-switch`.
- `.oh-bulk-bar { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 10px; padding: 12px; border: 1px solid #e3ebea; border-radius: 12px; background: #f7faf9; margin-bottom: 12px; }`
- `.oh-save-bar { position: sticky; bottom: 0; display: flex; flex-wrap: wrap; justify-content: space-between; gap: 12px; margin-top: 16px; padding: 12px 0 4px; background: linear-gradient(#fff0, #fff 30%); }`
- `.settings-real-grid { grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr)); }` (currently just `gap`).
- `.operating-hours-grid { grid-template-columns: repeat(auto-fit, minmax(min(100%, 20rem), 1fr)); }`
- `.settings-page, .availability-page { padding-bottom: clamp(40px, 8vh, 90px); }`
- Totem: `.totem-segments { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; }` ; `.totem-segment { flex: 1 1 140px; min-height: 44px; border-radius: 11px; border: 1px solid #d7e3e1; background: #fff; font-weight: 700; cursor: pointer; }` ; `.totem-segment.is-active { color: #236b5b; border-color: #61ac98; background: #e8f5f0; }` ; `.totem-scanner-frame { width: 100%; aspect-ratio: 1 / 1; max-height: 60vh; border-radius: 16px; overflow: hidden; background: #06212a; }` ; `.totem-scanner-frame video { width: 100%; height: 100%; object-fit: cover; }` ; `.totem-checkin-card { margin-block: clamp(24px, 6vh, 72px); }` (reduce the big top margin so the confirm button clears the viewport at 768px height).
- In the `@media (max-width: 820px)` block: `.oh-save-bar, .availability-editor-actions { flex-direction: column; align-items: stretch; }` and `.oh-save-bar .primary-button { width: 100%; }`.

- [ ] **Step 1:** Apply the CSS edits.
- [ ] **Step 2:** `npx vitest run` → all green (CSS-only, no behaviour change). `npm run build` → green.
- [ ] **Step 3:** Manual: `npm run dev`; in a browser at 1366×768, 1440×900, 1920×1080 and ~400px, open `/recepcao/configuracoes`, `/profissional/disponibilidade`, `/totem/check-in`; confirm no clipped buttons, nothing flush to the viewport bottom, no horizontal scroll, time inputs fully visible. Note findings.
- [ ] **Step 4: Commit** — `git commit -am "style: responsive pass for hours, availability and totem screens"`

---

## Task 9: full verification + report

- [ ] `npx vitest run` — all suites green (record count).
- [ ] `npm run build` — tsc + vite green; confirm a `qr-scanner` worker asset in the output.
- [ ] `npm run verify:production-bundle` — green.
- [ ] `git -C ../.. diff --check` — clean.
- [ ] `npm run dev` and drive the three screens (§6 manual list) at the three resolutions; capture what was checked and any residual issue.
- [ ] Report: root causes, files changed, tests added, build result, manual-test notes, `git status`, `git log --oneline` of the new commits. No push, no merge.

---

## Self-Review

**Spec coverage:**
- §1 remount fix → committed `d3e3a55`; Task 5 guards it; Task 4 depends on it. ✓
- §2 Operating Hours (prefill, apply-all, open/close, multi-period, "Adicionar período", explicit save, layout) → Tasks 2, 3, 8. ✓
- §3 time selector (native `type=time`, not clipped, start<end, no overlap) → Tasks 1, 2, 8 (`.time-input`). ✓
- §4 Minha disponibilidade (modes kept, weekly grid, add/remove periods, global window shown, no reset, warning kept) → Tasks 4, 5. ✓
- §5 Totem (two paths, permission, start/stop, live decode, auto-fill+validate, manual fallback, `qr-scanner`) → Tasks 6, 7. ✓
- §6 responsive at the 3 resolutions → Task 8 + manual in Tasks 8/9. ✓
- `WeeklyPeriodsEditor` shared base → Task 2, consumed by Tasks 3 and 4. ✓

**Placeholder scan:** Task 5 and Task 7 test bodies are described rather than fully spelled; they mirror `components/ProtectedRoute.test.tsx` wiring which exists in-repo. Acceptable — the executor has the pattern file. No "TBD/handle edge cases" left elsewhere.

**Type consistency:** `DayDraft`/`PeriodDraft`/`newPeriod` defined in Task 2 and used verbatim in Tasks 3–4. `OperatingHoursDayDto` payload shape (`{dayOfWeek, intervals:[{opensAt,closesAt}]}`) matches `api/modules.ts` and the backend `OperatingHoursDayRequest`. `QrScannerState` / `useQrScanner` signature consistent between Tasks 6 and 7. `normalizeToken` signature consistent. ✓
