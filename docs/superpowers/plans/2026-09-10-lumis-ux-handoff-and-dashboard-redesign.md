# LUMIS UX Round — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the approved LUMIS visual/UX round — shared design-system tokens + animated background, the Totem check-in layout fix, the Totem→phone booking handoff (atomic + idempotent), redesigned login screens, and the three dashboards — against the real `codex/reception-backend` branch without touching unrelated code.

**Architecture:** One shared token layer in `styles.css` plus two new React primitives (`LumisBackground`, `LumisPageShell`) that every migrated screen composes. The handoff is one new EF entity (`TotemBookingHandoff`) with a dedicated rate limiter and five anonymous/customer endpoints; completion is folded into `POST /api/customer/reservations` so the reservation and the handoff commit in one transaction, with an idempotent replay path. Dashboards reuse existing endpoints; two exact server-side aggregates are added (`DashboardCounts.TodayCheckIns`, optional `from`/`to` on `GET /api/professional/reservations`).

**Tech Stack:** ASP.NET Core .NET 10 minimal APIs, EF Core 10 / Npgsql / PostgreSQL, clean architecture (`GestaoPredio.Domain|Application|Infrastructure` + `recepcaototem` host). React 19 + react-router-dom 7 + Vite 5 + Vitest 5 + `@testing-library/react` 16, plain `styles.css` (no Tailwind utility use), `qrcode`@1.5.4 + `qr-scanner`@1.4.2. Tests: `GestaoPredio.UnitTests` (domain), `GestaoPredio.IntegrationTests` (`WebApplicationFactory` + real Postgres schema), Vitest (jsdom).

**Spec:** `docs/superpowers/specs/2026-09-10-lumis-ux-handoff-and-dashboard-redesign.md` (HEAD `35d5796`). Read it alongside this plan.

## Global Constraints

- **No push / no merge to `main` / no remote migration / no Supabase / no Railway.** All work is local commits on `codex/reception-backend` (worktree `.worktrees/reception-backend`).
- **The migration file is created in Task 9 but NEVER applied to any remote.** Local integration tests apply it via `db.Database.MigrateAsync()` against the ephemeral test schema only.
- **No mock/fake data in production paths.** No hardcoded names (Mariana / Rafael / Camila / Dra. Helena), no hardcoded codes (`4 7 2 9 1 6`), no fabricated notifications/activity. Blocks with no domain data are omitted, not stubbed.
- **Preserve every existing rule:** check-in (`useQrScanner`, HMAC, `allowUsed`, resolve/confirm, Visit `WAITING`, 12 s auto-return, generic errors), carousel `165f2f1` pointer-drag (do NOT replace with native touch-scroll), CSP string in `Program.cs` (do NOT edit), `ProtectedRoute` literal ternary, `safeCustomerReturnUrl` allowlist + `CUSTOMER_QUERY` regex.
- **Tokens `handoffToken` / `statusToken`:** 32-byte CSPRNG, base64url, distinct, SHA-256 hashed at rest, plaintext only in the create response, never logged, never in an audit row, never in a URL of an API call (`statusToken` travels in POST bodies only).
- **CSP is unchanged.** The handoff QR is a `data:` PNG (`img-src 'self' data: blob:` already allows it).
- **Generic errors only** for handoff failures: `INVALID_HANDOFF` (400), `HANDOFF_EXPIRED` (410), `HANDOFF_ALREADY_USED` (409). No oracle distinguishing "not found" from "wrong token". `COMPLETED` status/replay responses carry only `professionalName` / `startAt` / `roomName` — never customer name/phone/email/CPF/`customerId`/`reservationId`/token.
- **`prefers-reduced-motion: reduce`** must stop all decorative motion (background rays, BlurFade) with the UI still fully correct and functional.
- **Accessibility floor:** `focus-visible` kept, `aria-label`/roles kept, keyboard nav kept, touch targets ≥ 44 px, status conveyed by text + icon (never colour alone), decorative layers `aria-hidden` + `pointer-events: none`.
- **No new secret, no new required config.** All `RateLimiting:Handoff*` keys have code defaults.
- **Gates (run from `recepcaototem/ClientApp` for the JS ones, repo root for .NET):** `dotnet test tests/GestaoPredio.IntegrationTests`, `dotnet test tests/GestaoPredio.UnitTests`, `npx vitest run`, `npx tsc -b`, `npx vite build`, `npm run --silent verify:production-bundle`, `git diff --check`.

## Commit Convention

Every task ends with a local commit. Message body ends with:
```
Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
```

---

## File Structure

### Backend (new)
- `src/GestaoPredio.Domain/Customers/TotemBookingHandoff.cs` — entity + `TotemBookingHandoffStatus` enum. One responsibility: the handoff aggregate and its state transitions.
- `src/GestaoPredio.Infrastructure/Persistence/Configurations/TotemBookingHandoffConfiguration.cs` — EF mapping.
- `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/<timestamp>_TotemBookingHandoff.cs` (+ `.Designer.cs`, snapshot update) — additive table only.
- `recepcaototem/Features/Totem/TotemHandoffRateLimiter.cs` — dedicated fixed-window limiter (mirrors `ProfessionalPresenceRateLimiter`).
- `recepcaototem/Features/Totem/TotemBookingHandoffEndpoints.cs` — `create` / `status` / `cancel` / `claim` (all `AllowAnonymous`).

### Backend (modified)
- `src/GestaoPredio.Infrastructure/Persistence/ApplicationDbContext.cs` — `DbSet<TotemBookingHandoff>`.
- `recepcaototem/Program.cs` — `AddSingleton<TotemHandoffRateLimiter>()`, `app.MapTotemBookingHandoffEndpoints()`.
- `recepcaototem/Features/Customers/CustomerSchedulingEndpoints.cs` — `CustomerReservationRequest` gains `string? HandoffToken`; `CreateReservation` gains the atomic-completion + idempotent-replay branch; new `resolve` endpoint (`POST /api/customer/booking-handoffs/resolve`).
- `recepcaototem/Features/Reservations/ProfessionalReservationEndpoints.cs` — `List` gains optional `from`/`to`.
- `src/GestaoPredio.Application/Dashboard/DashboardModels.cs` — `DashboardCounts` gains `int TodayCheckIns`.
- `src/GestaoPredio.Infrastructure/Dashboard/PostgreSqlDashboardReader.cs` — one `CountAsync` for `TodayCheckIns`.
- `tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs` — `RateLimiting:Handoff*` test settings; `ResetDatabaseAsync` deletes `TotemBookingHandoffs` (before `Reservations`/`Professionals`).

### Frontend (new)
- `src/features/lumis/LumisBackground.tsx` (+ `.test.tsx`)
- `src/features/lumis/LumisPageShell.tsx` (+ `.test.tsx`)
- `src/features/totem/SixDigitCode.tsx` (+ `.test.tsx`) — the 6-box code input.
- `src/pages/TotemHandoff.tsx` (+ `.test.tsx`)
- `src/pages/admin/AdminDashboard.tsx` (+ `.test.tsx`)

### Frontend (modified)
- `src/styles.css` — `:root` `--lumis-*` block; legacy `--tk/tc/tp/te-*` remapped to `var(--lumis-*)`; `.lumis-*` rules; `.totem-magic-fade.is-in.is-settled`; `.totem-kiosk` rebuilt; login card rules.
- `src/features/totem/magic/BlurFade.tsx` — add `is-settled` on `transitionend`.
- `src/pages/Login.tsx` (+ `Login.test.tsx`) — discrete card, 3 audiences, remove marketing panel / social / forgot / remember-me; `claim` on mount for customer+handoff.
- `src/pages/TotemCheckIn.tsx` (+ `TotemCheckIn.test.tsx`) — topbar + centred stage + 6 boxes + `LumisBackground`.
- `src/pages/TotemProfessionals.tsx` (+ `TotemProfessionalsPage.test.tsx` if present) — "Continuar" creates the handoff.
- `src/pages/customer/CustomerBooking.tsx` — `?handoff=` handling.
- `src/pages/customer/CustomerHome.tsx` — shell → sidebar+drawer; `CustomerHome` dashboard cards.
- `src/pages/professional/ProfessionalHome.tsx` — shell restyle; `ProfessionalDashboard` exact KPIs.
- `src/components/AdminLayout.tsx` — restyle; `App.tsx` `/admin` index → `<AdminDashboard/>`.
- `src/api/modules.ts` — handoff clients; `dashboardApi`; `receptionApi.professionals`; `createReservation` gains `handoffToken?`; `professionalReservationsApi.list` query gains `from?`/`to?`.
- `src/App.tsx` — route `<Route path="/totem/handoff" element={<TotemHandoff/>} />`.
- `src/frontend-portals.test.ts` — Totem never routes to login; `/totem/handoff` anonymous.

---

# GROUP A — Design System / LumisBackground

### Task 1: LUMIS design tokens + legacy alias remap

**Files:**
- Modify: `recepcaototem/ClientApp/src/styles.css` (`:root` block near line 4; alias blocks at ~1126, ~1380, ~1460, ~1638)
- Test: `recepcaototem/ClientApp/src/styles.lumis-tokens.test.ts` (new)

**Interfaces:**
- Produces: CSS custom properties on `:root` — `--lumis-bg`, `--lumis-surface`, `--lumis-surface-2`, `--lumis-surface-3`, `--lumis-border`, `--lumis-border-soft`, `--lumis-border-strong`, `--lumis-text`, `--lumis-text-dim`, `--lumis-muted`, `--lumis-success`, `--lumis-warning`, `--lumis-danger`, `--lumis-info`. Consumed by every later frontend task.

- [ ] **Step 1: Write the failing test**

```ts
// src/styles.lumis-tokens.test.ts
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { expect, test } from 'vitest'

const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')

test(':root declares the full LUMIS token set with the approved values', () => {
  const root = css.match(/:root\s*\{[^}]*\}/)?.[0] ?? ''
  expect(root).toMatch(/--lumis-bg:\s*#181818/)
  expect(root).toMatch(/--lumis-surface:\s*#1[Cc]1[Cc]1[Cc]/)
  expect(root).toMatch(/--lumis-surface-2:\s*#222222/)
  expect(root).toMatch(/--lumis-surface-3:\s*#272727/)
  expect(root).toMatch(/--lumis-border:\s*#3[Dd]3[Dd]3[Dd]/)
  expect(root).toMatch(/--lumis-text:\s*#[Ff]{6}/)
  expect(root).toMatch(/--lumis-muted:\s*#888888/)
  expect(root).toMatch(/--lumis-success:/)
  expect(root).toMatch(/--lumis-warning:/)
  expect(root).toMatch(/--lumis-danger:/)
})

test('legacy per-page aliases resolve through --lumis-* (single source of truth)', () => {
  // every --tk-bg / --tc-bg / --tp-bg / --te-* base colour now references a --lumis-* var
  expect(css).toMatch(/--tk-bg:\s*var\(--lumis-bg\)/)
  expect(css).toMatch(/--tc-bg:\s*var\(--lumis-bg\)/)
  expect(css).toMatch(/--tp-bg:\s*var\(--lumis-bg\)/)
})
```

- [ ] **Step 2: Run it, verify it fails**

Run: `npx vitest run src/styles.lumis-tokens.test.ts`
Expected: FAIL (`--lumis-bg` not found).

- [ ] **Step 3: Add the token block to `:root`**

In `src/styles.css`, inside the existing `:root { … }` (after `--surface: #ffffff;`), add:

```css
  /* LUMIS identity — single source of truth. Legacy --tk/--tc/--tp/--te aliases map to these. */
  --lumis-bg: #181818;
  --lumis-surface: #1c1c1c;
  --lumis-surface-2: #222222;
  --lumis-surface-3: #272727;
  --lumis-border: #3d3d3d;
  --lumis-border-soft: rgba(255, 255, 255, .08);
  --lumis-border-strong: rgba(255, 255, 255, .22);
  --lumis-text: #ffffff;
  --lumis-text-dim: #f2f2f2;
  --lumis-muted: #888888;
  --lumis-success: #3fb98c;
  --lumis-warning: #e0a93b;
  --lumis-danger: #e27878;
  --lumis-info: #5b9bd5;
```

- [ ] **Step 4: Remap the legacy alias declarations**

For each block that hard-codes the identity palette, replace the literal with the token. Do NOT change any other property. Examples:
- `.totem-kiosk` block: `--tk-bg: #181818;` → `--tk-bg: var(--lumis-bg);`; `--tk-bg-2: #1c1c1c;` → `var(--lumis-surface);`; `--tk-card: #222222;` → `var(--lumis-surface-2);`; `--tk-card-hover: #272727;` → `var(--lumis-surface-3);`; `--tk-ink: #f2f2f2;` → `var(--lumis-text-dim);`; `--tk-muted: #888888;` → `var(--lumis-muted);`; `--tk-line`/`--tk-line-strong` → `var(--lumis-border-soft)` / `var(--lumis-border-strong)`.
- Same mapping for `--tc-*` (carousel), `--tp-*` (`/totem/profissionais`; note `--tp-line: #3d3d3d` → `var(--lumis-border)`, `--tp-card-2: #2b2b2b` → `var(--lumis-surface-3)`), and the `--te-*`→`--tk-*` bridge block.

- [ ] **Step 5: Run the test + full vitest**

Run: `npx vitest run src/styles.lumis-tokens.test.ts && npx vitest run`
Expected: PASS; no other test regresses (pure alias indirection, values unchanged).

- [ ] **Step 6: Build check + commit**

Run: `npx vite build`
```bash
git add src/styles.css src/styles.lumis-tokens.test.ts
git commit -m "feat(ui): LUMIS design tokens on :root; legacy aliases resolve through them"
```

---

### Task 2: `LumisBackground` component

**Files:**
- Create: `recepcaototem/ClientApp/src/features/lumis/LumisBackground.tsx`
- Create: `recepcaototem/ClientApp/src/features/lumis/LumisBackground.test.tsx`
- Modify: `recepcaototem/ClientApp/src/styles.css` (append `.lumis-bg` rules)

**Interfaces:**
- Produces: `export function LumisBackground({ intensity }: { intensity?: 'default' | 'muted' }): JSX.Element` — a `<div class="lumis-bg" aria-hidden style="pointer-events:none">` with three `<span class="lumis-bg-ray">`.

- [ ] **Step 1: Write the failing test**

```tsx
// src/features/lumis/LumisBackground.test.tsx
import { render } from '@testing-library/react'
import { expect, test } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { LumisBackground } from './LumisBackground'

test('is decorative: aria-hidden + pointer-events none + three rays', () => {
  const { container } = render(<LumisBackground />)
  const bg = container.querySelector('.lumis-bg') as HTMLElement
  expect(bg).not.toBeNull()
  expect(bg.getAttribute('aria-hidden')).toBe('true')
  expect(bg.style.pointerEvents).toBe('none')
  expect(bg.querySelectorAll('.lumis-bg-ray')).toHaveLength(3)
})

test('muted intensity is exposed as a data attribute', () => {
  const { container } = render(<LumisBackground intensity="muted" />)
  expect(container.querySelector('.lumis-bg')?.getAttribute('data-intensity')).toBe('muted')
})

test('CSS animates only transform/opacity and disables under reduced motion', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  const block = css.slice(css.indexOf('.lumis-bg'))
  expect(block).toMatch(/@keyframes lumis-ray-drift/)
  expect(block).not.toMatch(/\.lumis-bg-ray\s*\{[^}]*filter:/)          // no filter on the animated element
  expect(block).toMatch(/prefers-reduced-motion: reduce[\s\S]*\.lumis-bg-ray\s*\{[^}]*animation:\s*none/)
})
```

- [ ] **Step 2: Run it, verify it fails**

Run: `npx vitest run src/features/lumis/LumisBackground.test.tsx`
Expected: FAIL (module missing).

- [ ] **Step 3: Implement the component**

```tsx
// src/features/lumis/LumisBackground.tsx
// The single LUMIS animated backdrop: slow diagonal light rays over #181818. Purely decorative —
// no pointer events, hidden from assistive tech. All motion lives in styles.css (.lumis-bg) and is
// disabled under prefers-reduced-motion. `intensity="muted"` dims it behind dense screens.
export function LumisBackground({ intensity = 'default' }: { intensity?: 'default' | 'muted' }) {
  return (
    <div className="lumis-bg" data-intensity={intensity} aria-hidden="true" style={{ pointerEvents: 'none' }}>
      <span className="lumis-bg-ray" />
      <span className="lumis-bg-ray" />
      <span className="lumis-bg-ray" />
    </div>
  )
}
```

- [ ] **Step 4: Append the CSS**

Append to `src/styles.css`:

```css
/* --- LUMIS animated backdrop (LumisBackground / LumisPageShell) --- */
.lumis-bg { position: absolute; inset: 0; z-index: 0; overflow: hidden; pointer-events: none; }
.lumis-bg-ray {
  position: absolute; top: -20%; left: -30%; width: 42vw; height: 140%;
  background: linear-gradient(115deg, transparent 0%, rgba(255, 255, 255, .045) 50%, transparent 100%);
  transform: translate3d(-40vw, -10vh, 0) rotate(9deg);
  opacity: 0; will-change: transform, opacity;
  animation: lumis-ray-drift 26s linear infinite;
}
.lumis-bg-ray:nth-child(2) { top: 10%; width: 30vw; animation-duration: 34s; animation-delay: -9s; }
.lumis-bg-ray:nth-child(3) { top: -30%; width: 52vw; animation-duration: 19s; animation-delay: -15s; }
.lumis-bg[data-intensity="muted"] { opacity: .55; }
@keyframes lumis-ray-drift {
  0%   { transform: translate3d(-40vw, -10vh, 0) rotate(9deg); opacity: 0; }
  12%  { opacity: .5; }
  88%  { opacity: .5; }
  100% { transform: translate3d(120vw, 34vh, 0) rotate(9deg); opacity: 0; }
}
@media (prefers-reduced-motion: reduce) {
  .lumis-bg-ray { animation: none; opacity: .16; transform: translate3d(30vw, 10vh, 0) rotate(9deg); }
}
```

- [ ] **Step 5: Run tests + commit**

Run: `npx vitest run src/features/lumis/LumisBackground.test.tsx && npx tsc -b`
```bash
git add src/features/lumis/LumisBackground.tsx src/features/lumis/LumisBackground.test.tsx src/styles.css
git commit -m "feat(ui): LumisBackground — GPU-friendly animated light rays"
```

---

### Task 3: `LumisPageShell` wrapper

**Files:**
- Create: `recepcaototem/ClientApp/src/features/lumis/LumisPageShell.tsx`
- Create: `recepcaototem/ClientApp/src/features/lumis/LumisPageShell.test.tsx`
- Modify: `recepcaototem/ClientApp/src/styles.css` (append `.lumis-shell` rules)

**Interfaces:**
- Consumes: `LumisBackground` (Task 2).
- Produces: `export function LumisPageShell({ intensity, className, children }: { intensity?: 'default'|'muted', className?: string, children: ReactNode }): JSX.Element` — `<div class="lumis-shell {className}"><LumisBackground/><div class="lumis-shell-content">{children}</div></div>`.

- [ ] **Step 1: Write the failing test**

```tsx
// src/features/lumis/LumisPageShell.test.tsx
import { render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { LumisPageShell } from './LumisPageShell'

test('renders the backdrop behind a z-raised content layer', () => {
  render(<LumisPageShell><h1>Olá</h1></LumisPageShell>)
  expect(screen.getByRole('heading', { name: 'Olá' })).toBeInTheDocument()
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  expect(css).toMatch(/\.lumis-shell\s*\{[^}]*position:\s*relative/)
  expect(css).toMatch(/\.lumis-shell-content\s*\{[^}]*z-index:\s*1/)
  expect(css).not.toMatch(/\.lumis-shell(-content)?\s*\{[^}]*filter:/)
})

test('passes intensity through to the backdrop', () => {
  const { container } = render(<LumisPageShell intensity="muted">x</LumisPageShell>)
  expect(container.querySelector('.lumis-bg')?.getAttribute('data-intensity')).toBe('muted')
})
```

- [ ] **Step 2: Run it, verify it fails** — `npx vitest run src/features/lumis/LumisPageShell.test.tsx` → FAIL.

- [ ] **Step 3: Implement**

```tsx
// src/features/lumis/LumisPageShell.tsx
import type { ReactNode } from 'react'
import { LumisBackground } from './LumisBackground'

// One implementation of the LUMIS identity backdrop for a full page/shell. The content layer is
// z-raised and never carries a filter, so nested scroll/touch/carousel/camera areas keep working.
export function LumisPageShell({
  intensity, className, children,
}: { intensity?: 'default' | 'muted'; className?: string; children: ReactNode }) {
  return (
    <div className={`lumis-shell ${className ?? ''}`.trim()}>
      <LumisBackground intensity={intensity} />
      <div className="lumis-shell-content">{children}</div>
    </div>
  )
}
```

- [ ] **Step 4: Append CSS**

```css
.lumis-shell { position: relative; isolation: isolate; min-height: 100dvh; background: var(--lumis-bg); }
.lumis-shell-content { position: relative; z-index: 1; }
```

- [ ] **Step 5: Run + commit**

Run: `npx vitest run src/features/lumis && npx tsc -b`
```bash
git add src/features/lumis/LumisPageShell.tsx src/features/lumis/LumisPageShell.test.tsx src/styles.css
git commit -m "feat(ui): LumisPageShell — shared identity backdrop wrapper"
```

---

### Task 4: BlurFade compositing fix (`is-settled`)

**Files:**
- Modify: `recepcaototem/ClientApp/src/features/totem/magic/BlurFade.tsx`
- Modify: `recepcaototem/ClientApp/src/styles.css` (`.totem-magic-fade` rules region ~1230)
- Test: `recepcaototem/ClientApp/src/features/totem/magic/BlurFade.test.tsx` (new)

**Interfaces:**
- Produces: after the entry transition ends, `.totem-magic-fade` carries `is-in is-settled` and CSS zeroes `filter`/`will-change`.

- [ ] **Step 1: Write the failing test**

```tsx
// src/features/totem/magic/BlurFade.test.tsx
import { act, render } from '@testing-library/react'
import { expect, test } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { BlurFade } from './BlurFade'

test('after the entry transition ends the fade layer settles (no persistent filter/will-change)', () => {
  const { container } = render(<BlurFade>hi</BlurFade>)
  const el = container.querySelector('.totem-magic-fade') as HTMLElement
  act(() => { el.dispatchEvent(new Event('transitionend', { bubbles: true })) })
  expect(el.className).toMatch(/\bis-settled\b/)
})

test('CSS clears filter and will-change on the settled fade layer', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  expect(css).toMatch(/\.totem-magic-fade\.is-in\.is-settled\s*\{[^}]*filter:\s*none[^}]*will-change:\s*auto/)
})
```

- [ ] **Step 2: Run it, verify it fails** — FAIL (`is-settled` never added).

- [ ] **Step 3: Implement the component change**

```tsx
// src/features/totem/magic/BlurFade.tsx
import { useEffect, useState, type ReactNode } from 'react'

export function BlurFade({ delay, children }: { delay?: number; children: ReactNode }) {
  const [shown, setShown] = useState(false)
  const [settled, setSettled] = useState(false)
  useEffect(() => {
    const id = requestAnimationFrame(() => setShown(true))
    return () => cancelAnimationFrame(id)
  }, [])
  return (
    <div
      className={`totem-magic-fade ${shown ? 'is-in' : ''} ${settled ? 'is-settled' : ''}`.replace(/\s+/g, ' ').trim()}
      style={{ transitionDelay: `${delay ?? 0}ms` }}
      onTransitionEnd={() => setSettled(true)}
    >
      {children}
    </div>
  )
}
```

- [ ] **Step 4: Add the CSS rule**

After the existing `.totem-magic-fade.is-in { … }` rule in `styles.css`:

```css
.totem-magic-fade.is-in.is-settled { filter: none; will-change: auto; }
```

- [ ] **Step 5: Run tests (incl. carousel) + commit**

Run: `npx vitest run src/features/totem`
Expected: PASS incl. `TotemProfessionalCarousel.test.tsx` (unchanged).
```bash
git add src/features/totem/magic/BlurFade.tsx src/features/totem/magic/BlurFade.test.tsx src/styles.css
git commit -m "fix(totem): BlurFade clears filter/will-change after entry (compositing)"
```

---

# GROUP B — Login

### Task 5: `Login.tsx` redesign

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/Login.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/Login.test.tsx`
- Modify: `recepcaototem/ClientApp/src/styles.css` (append `.lumis-login-*` rules)

**Interfaces:**
- Consumes: `LumisPageShell` (Task 3), `useSession().login` (unchanged signature `login(email, password)`), `safeCustomerReturnUrl`.
- Produces: same `export function Login({ audience }: { audience?: 'admin' | 'customer' | 'professional' })`. Route wiring in `App.tsx` is unchanged.

- [ ] **Step 1: Rewrite `Login.test.tsx`**

Keep the existing "submits credentials / shows error / 429" assertions (adjust selectors to the new markup — `getByLabelText(/e-mail/i)`, `getByLabelText(/senha/i)`, `getByRole('button', { name: /entrar/i })`). Add:

```tsx
test('customer login: discrete card, no social / no forgot / no remember-me', () => {
  renderLogin('customer')  // helper wrapping <MemoryRouter><SessionProvider>…
  expect(screen.getByText('ÁREA DO CLIENTE')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /bem-vindo de volta/i })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: /criar conta/i })).toHaveAttribute('href', expect.stringContaining('/cliente/cadastro'))
  expect(screen.queryByText(/google|apple|icloud/i)).toBeNull()
  expect(screen.queryByText(/esqueci.*senha|recuperar senha/i)).toBeNull()
  expect(screen.queryByLabelText(/lembrar de mim|manter conectado/i)).toBeNull()
})

test('professional login points to the real registration flow', () => {
  renderLogin('professional')
  expect(screen.getByText('ÁREA DO PROFISSIONAL')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: /solicitar cadastro/i })).toHaveAttribute('href', '/profissional/cadastro')
})

test('admin login has no account-creation affordance', () => {
  renderLogin('admin')
  expect(screen.getByText('ADMINISTRAÇÃO')).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /criar conta|solicitar/i })).toBeNull()
})

test('customer returnUrl is preserved into the create-account link', () => {
  renderLogin('customer', '/cliente/login?returnUrl=%2Fcliente%2Fagendar%3Fhandoff%3DAbc-1')
  expect(screen.getByRole('link', { name: /criar conta/i }).getAttribute('href'))
    .toContain('returnUrl=')
})

test('sober look: no decorative icons, textual show/hide password control', () => {
  const { container } = renderLogin('customer')
  // the only <svg> allowed anywhere is none — no envelope / lock / shield / eye / arrow icons
  expect(container.querySelectorAll('svg')).toHaveLength(0)
  const toggle = screen.getByRole('button', { name: /mostrar/i })
  fireEvent.click(toggle)
  expect(screen.getByRole('button', { name: /ocultar/i })).toBeInTheDocument()
  expect(screen.getByLabelText(/senha/i)).toHaveAttribute('type', 'text')
})
```

- [ ] **Step 2: Run it, verify it fails** — `npx vitest run src/pages/Login.test.tsx` → FAIL.

- [ ] **Step 3: Rewrite `Login.tsx`**

Replace the two-panel `login-page` markup with a single centred card inside `LumisPageShell`. Keep: `submit`, `session.status` guards, the `session.user` "sessão ativa" branch, `returnUrl` (customer only). **No decorative icons anywhere** — no envelope/lock/shield/eye/arrow, no illustrations, no social buttons. E-mail and Senha are plain `<input>`s. The show/hide password control is a **textual** button reading `Mostrar` / `Ocultar` (not an eye icon). The submit button is plain text `Entrar`. Per-audience config drives eyebrow / title (`Bem-vindo de volta` for all) / helper text / secondary action.

```tsx
import { type FormEvent, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { ApiError } from '../api/client'
import { homeForRoles } from '../auth/roleRoutes'
import { safeCustomerReturnUrl } from '../auth/returnUrl'
import { LumisPageShell } from '../features/lumis/LumisPageShell'

type Audience = 'admin' | 'customer' | 'professional'
const COPY: Record<Audience, { eyebrow: string; text: string }> = {
  customer:     { eyebrow: 'ÁREA DO CLIENTE',      text: 'Acesse sua conta para continuar.' },
  professional: { eyebrow: 'ÁREA DO PROFISSIONAL', text: 'Acesse sua conta para continuar.' },
  admin:        { eyebrow: 'ADMINISTRAÇÃO',        text: '' },
}

export function Login({ audience = 'admin' }: { audience?: Audience }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const session = useSession()
  const returnUrl = audience === 'customer' ? safeCustomerReturnUrl(params.get('returnUrl')) : null

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setLoading(true)
    try {
      const current = await session.login(email, password)
      if (!current) throw new Error('Session unavailable')
      navigate(current.mustChangePassword ? '/change-password' : (returnUrl ?? homeForRoles(current.roles)), { replace: true })
    } catch (err) {
      setError(err instanceof ApiError && err.status === 429
        ? 'Muitas tentativas. Aguarde alguns instantes e tente novamente.'
        : 'E-mail ou senha inválidos.')
    } finally { setLoading(false) }
  }

  if (session.status === 'loading') return <div role="status">Confirmando sessão…</div>

  const { eyebrow, text } = COPY[audience]
  return (
    <LumisPageShell className="lumis-login">
      <main className="lumis-login-card">
        <img className="lumis-login-logo" src="/lumis-logo-transparent.png" alt="LUMIS" width={124} height={38} />
        <span className="lumis-login-eyebrow">{eyebrow}</span>
        <h1 className="lumis-login-title">Bem-vindo de volta</h1>
        {text && <p className="lumis-login-text">{text}</p>}

        {session.status === 'error' && (
          <div className="form-error" role="alert">Não foi possível confirmar a sessão.
            <button type="button" onClick={() => void session.refresh()}>Tentar novamente</button></div>
        )}

        {session.user ? (
          <div className="lumis-login-active">
            <p>Sessão ativa: {session.user.displayName}</p>
            <button className="primary-button" onClick={() => navigate(returnUrl ?? homeForRoles(session.user!.roles))}>Continuar</button>
            <button className="secondary-button" onClick={async () => { try { await session.logout(); setPassword('') } catch { setError('Não foi possível confirmar a saída.') } }}>Sair / Trocar conta</button>
          </div>
        ) : (
          <form onSubmit={submit} className="lumis-login-form">
            <label className="field-label">E-mail
              <input className="field-input" type="email" autoComplete="email" value={email}
                onChange={(e) => { setEmail(e.target.value); setError('') }} />
            </label>
            <label className="field-label">Senha
              <span className="lumis-password-field">
                <input className="field-input" type={showPassword ? 'text' : 'password'} autoComplete="current-password"
                  value={password} onChange={(e) => { setPassword(e.target.value); setError('') }} />
                <button type="button" className="lumis-password-toggle" onClick={() => setShowPassword((v) => !v)}>
                  {showPassword ? 'Ocultar' : 'Mostrar'}
                </button>
              </span>
            </label>
            {error && <div className="form-error" role="alert">{error}</div>}
            <button className="primary-button lumis-login-submit" type="submit" disabled={loading}>
              {loading ? <span className="spinner" /> : 'Entrar'}
            </button>
          </form>
        )}

        {audience === 'customer' && (
          <p className="lumis-login-secondary">Não tem uma conta?{' '}
            <a href={`/cliente/cadastro${returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : ''}`}>Criar conta</a></p>
        )}
        {audience === 'professional' && (
          <p className="lumis-login-secondary">Ainda não possui acesso? <a href="/profissional/cadastro">Solicitar cadastro</a></p>
        )}
        <a className="lumis-login-back" href="/">← Voltar</a>
      </main>
    </LumisPageShell>
  )
}
```

- [ ] **Step 4: Append card CSS** — `.lumis-login-card { max-width: 400px; margin: auto; padding: clamp(24px,5vw,40px); background: var(--lumis-surface-2); border: 1px solid var(--lumis-border); border-radius: 16px; display: flex; flex-direction: column; gap: 14px; }` plus `.lumis-login` flex-centering (`display:flex; align-items:center; justify-content:center; padding: clamp(16px,5vh,64px) 16px;`), `.lumis-login-eyebrow` (12px, `.13em`, `--lumis-muted`), `.lumis-login-title`, `.lumis-login-submit { min-height: 48px; width: 100%; }`, `.field-input { min-height: 44px; }` scoped, `.lumis-login-secondary a { color: var(--lumis-text-dim); }`, `.lumis-password-field { position: relative; display: block; }` with `.lumis-password-field .field-input { padding-right: 84px; }`, `.lumis-password-toggle { position: absolute; right: 10px; top: 50%; transform: translateY(-50%); min-height: 32px; padding: 0 8px; background: none; border: 0; color: var(--lumis-muted); font-size: 13px; cursor: pointer; }` `.lumis-password-toggle:hover { color: var(--lumis-text-dim); text-decoration: underline; }`. No `.password-field` eye-button styling is carried over.

- [ ] **Step 5: Run tests + tsc + commit**

Run: `npx vitest run src/pages/Login.test.tsx && npx tsc -b`
```bash
git add src/pages/Login.tsx src/pages/Login.test.tsx src/styles.css
git commit -m "feat(login): discrete LUMIS card, 3 audiences, no social/forgot/remember-me"
```

---

# GROUP C — Totem Check-In

### Task 6: `SixDigitCode` component

**Files:**
- Create: `recepcaototem/ClientApp/src/features/totem/SixDigitCode.tsx`
- Create: `recepcaototem/ClientApp/src/features/totem/SixDigitCode.test.tsx`
- Modify: `recepcaototem/ClientApp/src/styles.css` (append `.totem-code-boxes` rules)

**Interfaces:**
- Consumes: `onlyDigits6`, `isComplete6` from `src/features/totem/sixDigitCode.ts` (existing).
- Produces: `export function SixDigitCode({ value, onChange, onSubmit, disabled, id, label }: { value: string; onChange: (v: string) => void; onSubmit?: () => void; disabled?: boolean; id?: string; label?: string }): JSX.Element` — 6 visual cells backed by one hidden controlled `<input>` labelled "código de 6 dígitos".

- [ ] **Step 1: Write the failing test**

```tsx
// src/features/totem/SixDigitCode.test.tsx
import { fireEvent, render, screen } from '@testing-library/react'
import { expect, test, vi } from 'vitest'
import { SixDigitCode } from './SixDigitCode'

function Harness() {
  const [v, setV] = require('react').useState('')
  return <SixDigitCode value={v} onChange={setV} />
}

test('exposes a single control labelled "código de 6 dígitos"', () => {
  render(<SixDigitCode value="" onChange={vi.fn()} />)
  const input = screen.getByLabelText(/código de 6 dígitos/i)
  expect(input).toHaveAttribute('inputmode', 'numeric')
  expect(input).toHaveAttribute('autocomplete', 'one-time-code')
})

test('keeps only up to 6 digits, preserves leading zeros', () => {
  const onChange = vi.fn()
  render(<SixDigitCode value="" onChange={onChange} />)
  fireEvent.change(screen.getByLabelText(/código de 6 dígitos/i), { target: { value: '0a0b1c2d3e9' } })
  expect(onChange).toHaveBeenLastCalledWith('001239')
})

test('renders 6 cells reflecting the value', () => {
  const { container } = render(<SixDigitCode value="0429" onChange={vi.fn()} />)
  const cells = container.querySelectorAll('.totem-code-cell')
  expect(cells).toHaveLength(6)
  expect(cells[0].textContent).toBe('0')
  expect(cells[3].textContent).toBe('9')
  expect(cells[4].textContent).toBe('')
})

test('Enter submits only when complete', () => {
  const onSubmit = vi.fn()
  const { rerender } = render(<SixDigitCode value="12345" onChange={vi.fn()} onSubmit={onSubmit} />)
  fireEvent.keyDown(screen.getByLabelText(/código de 6 dígitos/i), { key: 'Enter' })
  expect(onSubmit).not.toHaveBeenCalled()
  rerender(<SixDigitCode value="123456" onChange={vi.fn()} onSubmit={onSubmit} />)
  fireEvent.keyDown(screen.getByLabelText(/código de 6 dígitos/i), { key: 'Enter' })
  expect(onSubmit).toHaveBeenCalledTimes(1)
})
```

- [ ] **Step 2: Run it, verify it fails** — FAIL (module missing).

- [ ] **Step 3: Implement**

```tsx
// src/features/totem/SixDigitCode.tsx
import { useRef } from 'react'
import { isComplete6, onlyDigits6 } from './sixDigitCode'

// Six visual cells backed by one hidden controlled <input> (inputMode numeric, one-time-code,
// paste-friendly, leading zeros preserved). Clicking anywhere focuses the input. Enter fires
// onSubmit only when the value is complete. Accessible name: "código de 6 dígitos".
export function SixDigitCode({
  value, onChange, onSubmit, disabled, id = 'totem-code',
  label = 'Código de 6 dígitos',
}: {
  value: string; onChange: (v: string) => void; onSubmit?: () => void
  disabled?: boolean; id?: string; label?: string
}) {
  const ref = useRef<HTMLInputElement>(null)
  const digits = value.split('')
  return (
    <div className="totem-code-boxes" onClick={() => ref.current?.focus()}>
      <label className="sr-only" htmlFor={id}>{label}</label>
      <input
        ref={ref} id={id} className="totem-code-hidden-input" value={value}
        onChange={(e) => onChange(onlyDigits6(e.target.value))}
        onKeyDown={(e) => { if (e.key === 'Enter' && isComplete6(value)) { e.preventDefault(); onSubmit?.() } }}
        inputMode="numeric" autoComplete="one-time-code" maxLength={6} spellCheck={false}
        aria-label={label} disabled={disabled}
      />
      {Array.from({ length: 6 }, (_, i) => (
        <span key={i} className={`totem-code-cell${i === value.length ? ' is-active' : ''}`} aria-hidden="true">
          {digits[i] ?? ''}
        </span>
      ))}
    </div>
  )
}
```

- [ ] **Step 4: CSS** — `.totem-code-boxes { position: relative; display: flex; gap: 10px; justify-content: center; }` · `.totem-code-hidden-input { position: absolute; inset: 0; opacity: 0; width: 100%; height: 100%; border: 0; cursor: text; }` · `.totem-code-cell { width: 48px; height: 60px; display: grid; place-items: center; font-size: 24px; color: var(--lumis-text); background: #141414; border: 1px solid var(--lumis-border-strong); border-radius: 12px; }` · `.totem-code-cell.is-active { border-color: var(--lumis-text-dim); }`

- [ ] **Step 5: Run + commit**

Run: `npx vitest run src/features/totem/SixDigitCode.test.tsx && npx tsc -b`
```bash
git add src/features/totem/SixDigitCode.tsx src/features/totem/SixDigitCode.test.tsx src/styles.css
git commit -m "feat(totem): SixDigitCode — 6-cell OTP input (one controlled model)"
```

---

### Task 7: `TotemCheckIn.tsx` layout rebuild

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/TotemCheckIn.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/TotemCheckIn.test.tsx`
- Modify: `recepcaototem/ClientApp/src/styles.css` (`.totem-kiosk` region ~1124–1230; rebuild)

**Interfaces:**
- Consumes: `SixDigitCode` (Task 6), `LumisBackground` (Task 2), unchanged `useQrScanner` / `totemApi.resolveCheckIn` / `totemApi.confirmCheckIn` / `normalizeToken` / `isComplete6`.

- [ ] **Step 1: Update the test file**

Keep every existing behavioural assertion (camera state text, `resolveCheckIn`/`confirmCheckIn` calls, preview, generic errors, 12 s auto-return, tab switching). Change structural selectors and add:

```tsx
test('kiosk chrome: discrete topbar with Voltar / LUMIS / clock, centred stage', () => {
  renderCheckIn()
  expect(screen.getByRole('button', { name: /voltar/i })).toBeInTheDocument()
  expect(screen.getByRole('img', { name: 'LUMIS' })).toBeInTheDocument()
  expect(screen.getByText('CHECK-IN')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /confirme sua chegada/i })).toBeInTheDocument()
})

test('code tab uses the 6-box control and still validates leading zeros', async () => {
  renderCheckIn()
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  const input = screen.getByLabelText(/código de 6 dígitos/i)
  fireEvent.change(input, { target: { value: '004729' } })
  fireEvent.click(screen.getByRole('button', { name: /confirmar|validar/i }))
  await waitFor(() => expect(resolveSpy).toHaveBeenCalledWith('004729'))
})
```

- [ ] **Step 2: Run it, verify it fails** — FAIL on the new selectors.

- [ ] **Step 3: Rebuild the component**

Replace the `<main className="totem-kiosk">` subtree: `<LumisBackground />` first child; a `.totem-kiosk-topbar` (`← Voltar` left / `<img alt="LUMIS">` centre / `<KioskClock/>` right); a `.totem-kiosk-stage` wrapping the existing `.totem-stage-inner` content (eyebrow `CHECK-IN`, `Confirme sua chegada`, `Escolha como deseja identificar seu agendamento`, the tablist, the scan/manual segments). In the manual segment, replace the single `<input>` with `<SixDigitCode value={token} onChange={setToken} onSubmit={() => isComplete6(token) && resolveToken(token, 'manual')} disabled={loading} />` and keep the `[Confirmar]` button (`disabled={!isComplete6(token) || loading}`). Keep `resolveToken`, `registerArrival`, `useQrScanner`, `goHome`, the `AUTO_RESET_MS` effect, the `Modal`. Delete the `<aside className="totem-aside">`.

- [ ] **Step 4: Rebuild `.totem-kiosk` CSS**

Replace the grid rules:

```css
.totem-kiosk {
  position: relative; overflow-x: hidden; min-height: 100dvh;
  display: flex; flex-direction: column;
  color: var(--lumis-text-dim); background: var(--lumis-bg);
}
.totem-kiosk-topbar {
  position: relative; z-index: 1;
  display: flex; align-items: center; justify-content: space-between;
  padding: clamp(14px, 2.5vw, 24px) clamp(16px, 4vw, 40px);
  border-bottom: 1px solid var(--lumis-border-soft);
}
.totem-kiosk-topbar img { width: clamp(104px, 12vw, 132px); height: auto; }
.totem-kiosk-stage {
  position: relative; z-index: 1; flex: 1;
  display: flex; align-items: center; justify-content: center;
  padding: clamp(24px, 5vh, 64px) clamp(16px, 5vw, 40px); overflow-y: auto;
}
.totem-kiosk-stage .totem-stage-inner {
  width: 100%; max-width: 560px; margin-inline: auto; text-align: center;
  display: flex; flex-direction: column; gap: 18px;
}
.totem-scan-frame { width: min(70vw, 420px); max-width: 100%; aspect-ratio: 1 / 1; max-height: none; margin-inline: auto; }
```

Remove the old `.totem-kiosk { display:grid; grid-template-columns: … }`, the `.totem-aside*` rules, and the `@media (max-width: 900px) { .totem-kiosk … .totem-aside … }` block. Keep `.totem-option`, `.totem-btn*`, `.totem-preview*`, `.totem-alert` (now reading `--lumis-*` via the remapped `--tk-*`).

- [ ] **Step 5: Run tests + full totem suite + build**

Run: `npx vitest run src/pages/TotemCheckIn.test.tsx && npx vitest run src/features/totem && npx tsc -b && npx vite build`
```bash
git add src/pages/TotemCheckIn.tsx src/pages/TotemCheckIn.test.tsx src/styles.css
git commit -m "fix(totem): /totem/check-in — topbar + centred stage + large camera + 6 boxes"
```

---

# GROUP D — Handoff backend

### Task 8: `TotemBookingHandoff` domain entity

**Files:**
- Create: `src/GestaoPredio.Domain/Customers/TotemBookingHandoff.cs`
- Test: `tests/GestaoPredio.UnitTests/TotemBookingHandoffTests.cs` (new)

**Interfaces:**
- Produces:
  - `enum TotemBookingHandoffStatus : short { Pending = 0, Completed = 1, Expired = 2 }`
  - `sealed class TotemBookingHandoff` with private ctor and get-only properties `Id, ProfessionalId, HandoffTokenHash (byte[]), StatusTokenHash (byte[]), Status, CreatedAt, ExpiresAt, StartedAt?, CompletedAt?, ReservationId?, Version (uint)`.
  - `static TotemBookingHandoff Create(Guid professionalId, byte[] handoffTokenHash, byte[] statusTokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)`
  - `void MarkStarted(DateTimeOffset at, TimeSpan graceWindow, DateTimeOffset hardCeiling)`
  - `void Complete(Guid reservationId, DateTimeOffset at)`
  - `void MarkExpired(DateTimeOffset at)`
  - `bool IsUsable(DateTimeOffset now)`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/GestaoPredio.UnitTests/TotemBookingHandoffTests.cs
using GestaoPredio.Domain.Customers;

namespace GestaoPredio.UnitTests;

public sealed class TotemBookingHandoffTests
{
    private static byte[] Hash(byte b) => Enumerable.Repeat(b, 32).ToArray();
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static TotemBookingHandoff New() =>
        TotemBookingHandoff.Create(Guid.NewGuid(), Hash(1), Hash(2), T0, T0.AddMinutes(5));

    [Fact]
    public void Create_rejects_bad_hashes_and_inverted_window()
    {
        Assert.Throws<ArgumentException>(() => TotemBookingHandoff.Create(Guid.NewGuid(), new byte[8], Hash(2), T0, T0.AddMinutes(5)));
        Assert.Throws<ArgumentException>(() => TotemBookingHandoff.Create(Guid.Empty, Hash(1), Hash(2), T0, T0.AddMinutes(5)));
        Assert.Throws<ArgumentException>(() => TotemBookingHandoff.Create(Guid.NewGuid(), Hash(1), Hash(2), T0, T0));
    }

    [Fact]
    public void MarkStarted_sets_StartedAt_once_and_extends_within_the_hard_ceiling()
    {
        var h = New();
        h.MarkStarted(T0.AddMinutes(4), TimeSpan.FromMinutes(10), T0.AddMinutes(20));
        Assert.Equal(T0.AddMinutes(4), h.StartedAt);
        Assert.Equal(T0.AddMinutes(14), h.ExpiresAt);          // max(5, 4+10) = 14, under the 20 ceiling

        h.MarkStarted(T0.AddMinutes(9), TimeSpan.FromMinutes(10), T0.AddMinutes(20));
        Assert.Equal(T0.AddMinutes(4), h.StartedAt);           // unchanged
        Assert.Equal(T0.AddMinutes(14), h.ExpiresAt);          // not re-extended
    }

    [Fact]
    public void MarkStarted_never_pushes_ExpiresAt_past_the_hard_ceiling()
    {
        var h = TotemBookingHandoff.Create(Guid.NewGuid(), Hash(1), Hash(2), T0, T0.AddMinutes(5));
        h.MarkStarted(T0.AddMinutes(19), TimeSpan.FromMinutes(10), T0.AddMinutes(20));
        Assert.Equal(T0.AddMinutes(20), h.ExpiresAt);
    }

    [Fact]
    public void Complete_moves_to_Completed_and_links_the_reservation()
    {
        var h = New();
        var rid = Guid.NewGuid();
        h.Complete(rid, T0.AddMinutes(3));
        Assert.Equal(TotemBookingHandoffStatus.Completed, h.Status);
        Assert.Equal(rid, h.ReservationId);
        Assert.Equal(T0.AddMinutes(3), h.CompletedAt);
        Assert.Throws<InvalidOperationException>(() => h.Complete(Guid.NewGuid(), T0.AddMinutes(4)));
    }

    [Fact]
    public void MarkExpired_only_from_pending_and_IsUsable_reflects_state_and_clock()
    {
        var h = New();
        Assert.True(h.IsUsable(T0.AddMinutes(1)));
        Assert.False(h.IsUsable(T0.AddMinutes(6)));            // past ExpiresAt
        h.MarkExpired(T0.AddMinutes(6));
        Assert.Equal(TotemBookingHandoffStatus.Expired, h.Status);
        Assert.False(h.IsUsable(T0));
        Assert.Throws<InvalidOperationException>(() => h.MarkExpired(T0.AddMinutes(7)));
    }
}
```

- [ ] **Step 2: Run, verify fail** — `dotnet test tests/GestaoPredio.UnitTests --filter TotemBookingHandoffTests` → compile error / fail.

- [ ] **Step 3: Implement the entity**

```csharp
// src/GestaoPredio.Domain/Customers/TotemBookingHandoff.cs
using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Customers;

public enum TotemBookingHandoffStatus : short { Pending = 0, Completed = 1, Expired = 2 }

/// <summary>
/// A one-shot bridge from the public Totem to a visitor's phone: the phone completes a booking for
/// <see cref="ProfessionalId"/> while the kiosk polls for the result. No PII, no plaintext token.
/// </summary>
public sealed class TotemBookingHandoff
{
    private TotemBookingHandoff() { }

    public Guid Id { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public byte[] HandoffTokenHash { get; private set; } = [];
    public byte[] StatusTokenHash { get; private set; } = [];
    public TotemBookingHandoffStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? ReservationId { get; private set; }
    public uint Version { get; private set; }

    public static TotemBookingHandoff Create(Guid professionalId, byte[] handoffTokenHash, byte[] statusTokenHash,
        DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (professionalId == Guid.Empty) throw new ArgumentException("O profissional deve ser informado.", nameof(professionalId));
        if (handoffTokenHash is not { Length: 32 }) throw new ArgumentException("O hash deve ter 32 bytes.", nameof(handoffTokenHash));
        if (statusTokenHash is not { Length: 32 }) throw new ArgumentException("O hash deve ter 32 bytes.", nameof(statusTokenHash));
        if (expiresAt <= createdAt) throw new ArgumentException("A expiração deve ser posterior à criação.", nameof(expiresAt));
        return new TotemBookingHandoff
        {
            Id = Guid.NewGuid(), ProfessionalId = professionalId,
            HandoffTokenHash = handoffTokenHash.ToArray(), StatusTokenHash = statusTokenHash.ToArray(),
            Status = TotemBookingHandoffStatus.Pending,
            CreatedAt = TimestampNormalizer.ToUtcMicroseconds(createdAt),
            ExpiresAt = TimestampNormalizer.ToUtcMicroseconds(expiresAt),
        };
    }

    public void MarkStarted(DateTimeOffset at, TimeSpan graceWindow, DateTimeOffset hardCeiling)
    {
        Require(TotemBookingHandoffStatus.Pending);
        if (StartedAt is not null) return;
        StartedAt = TimestampNormalizer.ToUtcMicroseconds(at);
        var extended = at + graceWindow;
        var target = extended > ExpiresAt ? extended : ExpiresAt;
        if (target > hardCeiling) target = hardCeiling;
        ExpiresAt = TimestampNormalizer.ToUtcMicroseconds(target);
    }

    public void Complete(Guid reservationId, DateTimeOffset at)
    {
        Require(TotemBookingHandoffStatus.Pending);
        if (reservationId == Guid.Empty) throw new ArgumentException("A reserva deve ser informada.", nameof(reservationId));
        Status = TotemBookingHandoffStatus.Completed;
        ReservationId = reservationId;
        CompletedAt = TimestampNormalizer.ToUtcMicroseconds(at);
    }

    public void MarkExpired(DateTimeOffset at)
    {
        Require(TotemBookingHandoffStatus.Pending);
        Status = TotemBookingHandoffStatus.Expired;
        _ = at;
    }

    public bool IsUsable(DateTimeOffset now) => Status == TotemBookingHandoffStatus.Pending && ExpiresAt > now;

    private void Require(TotemBookingHandoffStatus expected)
    {
        if (Status != expected) throw new InvalidOperationException($"Handoff em {Status}; esperado {expected}.");
    }
}
```

- [ ] **Step 4: Run tests** — `dotnet test tests/GestaoPredio.UnitTests --filter TotemBookingHandoffTests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/GestaoPredio.Domain/Customers/TotemBookingHandoff.cs tests/GestaoPredio.UnitTests/TotemBookingHandoffTests.cs
git commit -m "feat(domain): TotemBookingHandoff entity + state machine"
```

---

### Task 9: EF mapping + migration

**Files:**
- Create: `src/GestaoPredio.Infrastructure/Persistence/Configurations/TotemBookingHandoffConfiguration.cs`
- Modify: `src/GestaoPredio.Infrastructure/Persistence/ApplicationDbContext.cs` (add `DbSet`)
- Create: `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/<ts>_TotemBookingHandoff.cs` (+ Designer + snapshot) via `dotnet ef`
- Modify: `tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs` (`ResetDatabaseAsync`: add `DELETE FROM "TotemBookingHandoffs"` **before** the `Reservations` delete)
- Test: `tests/GestaoPredio.IntegrationTests/TotemBookingHandoffModelTests.cs` (new)

**Interfaces:**
- Consumes: `TotemBookingHandoff` (Task 8).
- Produces: table `TotemBookingHandoffs`; `ApplicationDbContext.TotemBookingHandoffs`.

- [ ] **Step 1: Write the failing model-shape test**

```csharp
// tests/GestaoPredio.IntegrationTests/TotemBookingHandoffModelTests.cs
using GestaoPredio.Domain.Customers;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.IntegrationTests;

public sealed class TotemBookingHandoffModelTests
{
    [Fact]
    public void Handoff_table_hashes_are_unique_and_reservation_fk_is_no_action()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var e = db.Model.FindEntityType(typeof(TotemBookingHandoff))!;
        Assert.Equal("TotemBookingHandoffs", e.GetTableName());
        Assert.Contains(e.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(TotemBookingHandoff.HandoffTokenHash));
        Assert.Contains(e.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(TotemBookingHandoff.StatusTokenHash));
        Assert.Contains(e.GetForeignKeys(), fk => fk.Properties.Single().Name == nameof(TotemBookingHandoff.ProfessionalId) && fk.DeleteBehavior == DeleteBehavior.NoAction);
        Assert.Equal("bytea", e.FindProperty(nameof(TotemBookingHandoff.HandoffTokenHash))!.GetColumnType());
    }
}
```

- [ ] **Step 2: Run, verify fail** — `dotnet test tests/GestaoPredio.IntegrationTests --filter TotemBookingHandoffModelTests` → FAIL (entity not in model).

- [ ] **Step 3: Add the DbSet + configuration**

`ApplicationDbContext.cs`: `public DbSet<TotemBookingHandoff> TotemBookingHandoffs => Set<TotemBookingHandoff>();`

```csharp
// src/GestaoPredio.Infrastructure/Persistence/Configurations/TotemBookingHandoffConfiguration.cs
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class TotemBookingHandoffConfiguration : IEntityTypeConfiguration<TotemBookingHandoff>
{
    public void Configure(EntityTypeBuilder<TotemBookingHandoff> entity)
    {
        entity.ToTable("TotemBookingHandoffs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.HandoffTokenHash).HasColumnType("bytea").IsRequired();
        entity.Property(x => x.StatusTokenHash).HasColumnType("bytea").IsRequired();
        entity.Property(x => x.Status).HasConversion<short>();
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.StartedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => x.HandoffTokenHash).IsUnique().HasDatabaseName("UX_TotemBookingHandoffs_HandoffTokenHash");
        entity.HasIndex(x => x.StatusTokenHash).IsUnique().HasDatabaseName("UX_TotemBookingHandoffs_StatusTokenHash");
        entity.HasIndex(x => x.ProfessionalId).HasDatabaseName("IX_TotemBookingHandoffs_ProfessionalId");
        entity.HasIndex(x => x.ReservationId).HasDatabaseName("IX_TotemBookingHandoffs_ReservationId");
        entity.HasIndex(x => new { x.Status, x.ExpiresAt }).HasDatabaseName("IX_TotemBookingHandoffs_Status_ExpiresAt");
        entity.HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.NoAction);
    }
}
```

- [ ] **Step 4: Generate the migration (offline, local only)**

Run from repo root:
```bash
dotnet ef migrations add TotemBookingHandoff \
  --project src/GestaoPredio.Infrastructure \
  --startup-project recepcaototem \
  --context ApplicationDbContext \
  --output-dir Persistence/Migrations/PostgreSql
```
Inspect the generated `Up()`: it must contain ONLY `CreateTable("TotemBookingHandoffs", …)` + the five `CreateIndex` calls (two unique). It must NOT `AlterColumn`/`DropColumn`/rename anything on `Reservations`, `Visits`, `Professionals`, `CheckInTokens`. `Down()` must be a single `DropTable`.

- [ ] **Step 5: Wire test cleanup + run**

In `ModulesApiFactory.ResetDatabaseAsync`, add `await db.Database.ExecuteSqlRawAsync("DELETE FROM \"TotemBookingHandoffs\"");` immediately before the `Reservations` line.

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter TotemBookingHandoffModelTests`
Expected: PASS (the test DB migrates the new table).

- [ ] **Step 6: Commit**

```bash
git add src/GestaoPredio.Infrastructure/Persistence src/GestaoPredio.Infrastructure/Persistence/Migrations tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs tests/GestaoPredio.IntegrationTests/TotemBookingHandoffModelTests.cs
git commit -m "feat(db): TotemBookingHandoffs table + EF mapping (migration not applied remotely)"
```

---

### Task 10: `TotemHandoffRateLimiter`

**Files:**
- Create: `recepcaototem/Features/Totem/TotemHandoffRateLimiter.cs`
- Modify: `recepcaototem/Program.cs` (`AddSingleton<TotemHandoffRateLimiter>()` next to the other limiters, ~line 66)
- Modify: `tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs` (`ConfigureWebHost`: add the six `RateLimiting:Handoff*` `UseSetting` lines → `"10000"` / window `"60"`)

**Interfaces:**
- Produces: `sealed class TotemHandoffRateLimiter(IConfiguration) : IDisposable` with `ValueTask<TotemHandoffRateLimitLease> AcquireAsync(string partitionKey, string bucket, CancellationToken)` where `bucket ∈ { "create", "status", "cancel", "claim", "resolve" }`; `TotemHandoffRateLimitLease.IsAcquired`. Defaults: create 10, status 50, cancel 15, claim 20, resolve 20; window `RateLimiting:HandoffWindowSeconds` default 60.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/GestaoPredio.IntegrationTests/TotemHandoffRateLimitTests.cs
using System.Net;
using System.Net.Http.Json;
namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class TotemHandoffRateLimitTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Status_polling_never_429s_within_its_own_generous_budget()
    {
        await factory.ResetAsync();
        using var host = factory.WithConfig(
            ("RateLimiting:HandoffWindowSeconds", "600"),
            ("RateLimiting:HandoffStatusPermitLimit", "50"),
            ("RateLimiting:HandoffCreateIpPermitLimit", "50"));
        var prof = await SeedActiveProfessionalAsync();
        var create = await host.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId = prof });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var body = await create.Content.ReadFromJsonAsync<CreateBody>();
        for (var i = 0; i < 45; i++)   // ~90 s of 2 s polling
        {
            var r = await host.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{body!.Id}/status", new { statusToken = body.StatusToken });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode);
        }
        var r51 = await host.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{body!.Id}/status", new { statusToken = body.StatusToken });
        Assert.Equal(HttpStatusCode.TooManyRequests, r51.StatusCode);   // 46th..51st cross the 50 limit
    }

    private sealed record CreateBody(Guid Id, string HandoffToken, string StatusToken, DateTimeOffset ExpiresAt, string ProfessionalName, string Profession);
    // SeedActiveProfessionalAsync: insert a Professional via a service scope, return its Id (see Task 11 helper).
}
```

- [ ] **Step 2: Run, verify fail** — endpoint 404 / limiter missing → FAIL.

- [ ] **Step 3: Implement the limiter** (copy `ProfessionalPresenceRateLimiter` shape; one `PartitionedRateLimiter<string>` per bucket, selected in `AcquireAsync`):

```csharp
// recepcaototem/Features/Totem/TotemHandoffRateLimiter.cs
using System.Threading.RateLimiting;

namespace recepcaototem.Features.Totem;

public sealed class TotemHandoffRateLimiter : IDisposable
{
    private readonly Dictionary<string, PartitionedRateLimiter<string>> _buckets;

    public TotemHandoffRateLimiter(IConfiguration cfg)
    {
        var window = cfg.GetValue("RateLimiting:HandoffWindowSeconds", 60);
        _buckets = new()
        {
            ["create"]  = Make(cfg.GetValue("RateLimiting:HandoffCreateIpPermitLimit", 10), window),
            ["status"]  = Make(cfg.GetValue("RateLimiting:HandoffStatusPermitLimit", 50), window),
            ["cancel"]  = Make(cfg.GetValue("RateLimiting:HandoffCancelIpPermitLimit", 15), window),
            ["claim"]   = Make(cfg.GetValue("RateLimiting:HandoffClaimIpPermitLimit", 20), window),
            ["resolve"] = Make(cfg.GetValue("RateLimiting:HandoffResolveIpPermitLimit", 20), window),
        };
    }

    public async ValueTask<TotemHandoffRateLimitLease> AcquireAsync(string partitionKey, string bucket, CancellationToken ct)
        => new(await _buckets[bucket].AcquireAsync(partitionKey, 1, ct));

    private static PartitionedRateLimiter<string> Make(int permits, int seconds) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key,
            _ => new FixedWindowRateLimiterOptions
            { PermitLimit = Math.Max(1, permits), Window = TimeSpan.FromSeconds(Math.Max(1, seconds)), QueueLimit = 0, AutoReplenishment = true }));

    public void Dispose() { foreach (var b in _buckets.Values) b.Dispose(); }
}

public sealed class TotemHandoffRateLimitLease(RateLimitLease lease) : IDisposable
{
    public bool IsAcquired => lease.IsAcquired;
    public void Dispose() => lease.Dispose();
}
```

- [ ] **Step 4: Register + test settings** — `Program.cs`: `builder.Services.AddSingleton<TotemHandoffRateLimiter>();`. `ModulesApiFactory.ConfigureWebHost`: `builder.UseSetting("RateLimiting:HandoffCreateIpPermitLimit", "10000");` and the same for `HandoffStatusPermitLimit`, `HandoffCancelIpPermitLimit`, `HandoffClaimIpPermitLimit`, `HandoffResolveIpPermitLimit`, plus `builder.UseSetting("RateLimiting:HandoffWindowSeconds", "60");`.

- [ ] **Step 5:** Deferred — the rate-limit test compiles but stays red until Task 11 provides `create`/`status`. Mark this task's commit now; the test file is completed in Task 11's run.

```bash
git add recepcaototem/Features/Totem/TotemHandoffRateLimiter.cs recepcaototem/Program.cs tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs tests/GestaoPredio.IntegrationTests/TotemHandoffRateLimitTests.cs
git commit -m "feat(totem): dedicated TotemHandoffRateLimiter (own budget per bucket)"
```

---

### Task 11: `create` + `status` + `cancel` endpoints

**Files:**
- Create: `recepcaototem/Features/Totem/TotemBookingHandoffEndpoints.cs`
- Modify: `recepcaototem/Program.cs` (`app.MapTotemBookingHandoffEndpoints();` after `app.MapTotemEndpoints();`)
- Test: `tests/GestaoPredio.IntegrationTests/TotemBookingHandoffApiTests.cs` (new); finish `TotemHandoffRateLimitTests` seed helper

**Interfaces:**
- Consumes: `TotemBookingHandoff`, `TotemHandoffRateLimiter`, `ApplicationDbContext`, `TimeProvider`.
- Produces (all `AllowAnonymous`, JSON bodies):
  - `POST /api/totem/booking-handoffs` `{ professionalId: Guid }` → `201 { id, handoffToken, statusToken, expiresAt, professionalName, profession }`
  - `POST /api/totem/booking-handoffs/{id:guid}/status` `{ statusToken }` → `{ status: "PENDING", expiresAt }` | `{ status: "COMPLETED", professionalName, startAt, roomName }` | `{ status: "EXPIRED" }`
  - `POST /api/totem/booking-handoffs/{id:guid}/cancel` `{ statusToken }` → `200 { status: "EXPIRED" }` (idempotent)
  - Failures: `INVALID_HANDOFF` (400), `429` `TOO_MANY_REQUESTS`.
  - `HandoffWindows` static: `Initial = 5 min`, `Grace = 10 min`, `HardCeiling = 20 min`.
  - Helpers reused by Tasks 12–14: `TryDecodeHash(string token, out byte[] hash)` (base64url → 32 bytes → SHA-256), `INVALID_HANDOFF` `IResult`.

- [ ] **Step 1: Write failing API tests**

```csharp
// tests/GestaoPredio.IntegrationTests/TotemBookingHandoffApiTests.cs  (excerpt)
[Fact]
public async Task Create_returns_two_distinct_tokens_stored_only_as_hashes()
{
    await factory.ResetAsync();
    var prof = await SeedActiveProfessionalAsync("Dra. Ana", "Fisioterapia");
    var log = factory.CaptureLogs();
    var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId = prof });
    Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    var b = await res.Content.ReadFromJsonAsync<CreateBody>();
    Assert.NotEqual(b!.HandoffToken, b.StatusToken);
    Assert.Equal("Dra. Ana", b.ProfessionalName);
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var row = await db.TotemBookingHandoffs.SingleAsync();
    Assert.Equal(32, row.HandoffTokenHash.Length);
    Assert.DoesNotContain(b.HandoffToken, log.Text);
    Assert.DoesNotContain(b.StatusToken, log.Text);
}

[Fact]
public async Task Status_is_PENDING_then_EXPIRED_after_the_window_and_wrong_token_is_generic()
{
    await factory.ResetAsync();
    var prof = await SeedActiveProfessionalAsync();
    factory.FreezeTime(DateTimeOffset.UtcNow);
    var b = await CreateHandoffAsync(prof);
    var p = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
    Assert.Equal("PENDING", (await p.Content.ReadFromJsonAsync<StatusBody>())!.Status);

    var wrong = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = "not-a-token" });
    Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
    Assert.Equal("INVALID_HANDOFF", (await wrong.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

    factory.FreezeTime(factory.UtcNow.AddMinutes(6));
    var e = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
    Assert.Equal("EXPIRED", (await e.Content.ReadFromJsonAsync<StatusBody>())!.Status);
}

[Fact]
public async Task Cancel_expires_a_pending_handoff_and_is_idempotent()
{
    await factory.ResetAsync();
    var prof = await SeedActiveProfessionalAsync();
    var b = await CreateHandoffAsync(prof);
    Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/cancel", new { statusToken = b.StatusToken })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/cancel", new { statusToken = b.StatusToken })).StatusCode);
    var s = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{b.Id}/status", new { statusToken = b.StatusToken });
    Assert.Equal("EXPIRED", (await s.Content.ReadFromJsonAsync<StatusBody>())!.Status);
}

[Fact]
public async Task Cancel_requires_the_matching_status_token_and_never_reveals_existence()
{
    await factory.ResetAsync();
    var prof = await SeedActiveProfessionalAsync();
    var a = await CreateHandoffAsync(prof);
    var other = await CreateHandoffAsync(prof);

    // handoff A's id + handoff B's statusToken → generic 400, A is NOT cancelled
    var cross = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{a.Id}/cancel", new { statusToken = other.StatusToken });
    Assert.Equal(HttpStatusCode.BadRequest, cross.StatusCode);
    Assert.Equal("INVALID_HANDOFF", (await cross.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

    // garbage token for a real id, and any token for a random id → same generic 400 (no oracle)
    var garbage = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{a.Id}/cancel", new { statusToken = "not-a-token" });
    Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
    var unknown = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{Guid.NewGuid()}/cancel", new { statusToken = a.StatusToken });
    Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

    // A is still PENDING (no mutation happened on any failed attempt)
    var still = await factory.Client.PostAsJsonAsync($"/api/totem/booking-handoffs/{a.Id}/status", new { statusToken = a.StatusToken });
    Assert.Equal("PENDING", (await still.Content.ReadFromJsonAsync<StatusBody>())!.Status);
}

[Fact]
public async Task Create_rejects_an_inactive_or_unknown_professional_generically()
{
    await factory.ResetAsync();
    var res = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs", new { professionalId = Guid.NewGuid() });
    Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
}
```

Add a shared `SeedActiveProfessionalAsync(name?, profession?)` helper (service scope → `Professional.Create` → save → return `Id`) and `CreateHandoffAsync` in this file (or a small `HandoffTestSupport` static). Wire the same helper into `TotemHandoffRateLimitTests`.

- [ ] **Step 2: Run, verify fail** — FAIL (routes 404).

- [ ] **Step 3: Implement the endpoints**

```csharp
// recepcaototem/Features/Totem/TotemBookingHandoffEndpoints.cs  (shape)
public static class TotemBookingHandoffEndpoints
{
    internal static class HandoffWindows
    {
        public static readonly TimeSpan Initial = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan HardCeiling = TimeSpan.FromMinutes(20);
    }

    public static IEndpointRouteBuilder MapTotemBookingHandoffEndpoints(this IEndpointRouteBuilder e)
    {
        e.MapPost("/api/totem/booking-handoffs", Create).AllowAnonymous();
        e.MapPost("/api/totem/booking-handoffs/{id:guid}/status", Status).AllowAnonymous();
        e.MapPost("/api/totem/booking-handoffs/{id:guid}/cancel", Cancel).AllowAnonymous();
        e.MapPost("/api/totem/booking-handoffs/claim", Claim).AllowAnonymous();   // Task 12
        return e;
    }

    internal static IResult Invalid() => Results.Json(new ApiError("INVALID_HANDOFF", "Não foi possível validar este código."), statusCode: 400);
    internal static IResult Expired() => Results.Json(new ApiError("HANDOFF_EXPIRED", "Este QR Code expirou."), statusCode: 410);
    internal static IResult TooMany() => Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);

    internal static bool TryDecodeHash(string? token, out byte[] hash)
    {
        hash = [];
        if (string.IsNullOrWhiteSpace(token)) return false;
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(token); } catch (FormatException) { return false; }
        if (bytes.Length != 32) return false;
        hash = SHA256.HashData(bytes);
        return true;
    }

    private static string NewToken(out byte[] hash)
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        hash = SHA256.HashData(raw);
        return WebEncoders.Base64UrlEncode(raw);
    }

    private static async Task<IResult> Create(CreateHandoffRequest request, HttpContext ctx,
        TotemHandoffRateLimiter limiter, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        using var lease = await limiter.AcquireAsync(ip, "create", ct);
        if (!lease.IsAcquired) return TooMany();

        var professional = await db.Professionals.AsNoTracking()
            .Where(x => x.Id == request.ProfessionalId && x.IsActive)
            .Select(x => new { x.Name, x.Profession }).SingleOrDefaultAsync(ct);
        if (professional is null) return Results.NotFound(new ApiError("INVALID_HANDOFF", "Profissional indisponível."));

        var now = time.GetUtcNow();
        var handoffToken = NewToken(out var handoffHash);
        var statusToken = NewToken(out var statusHash);
        var handoff = TotemBookingHandoff.Create(request.ProfessionalId, handoffHash, statusHash, now, now + HandoffWindows.Initial);
        db.TotemBookingHandoffs.Add(handoff);
        db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid(), Action = "TOTEM_HANDOFF_CREATED", Result = "SUCCEEDED",
            TargetEntityType = "TOTEM_HANDOFF", TargetEntityId = handoff.Id, OccurredAt = now, CorrelationId = ctx.TraceIdentifier });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/totem/booking-handoffs/{handoff.Id}", new
        {
            id = handoff.Id, handoffToken, statusToken, expiresAt = handoff.ExpiresAt,
            professionalName = professional.Name, profession = professional.Profession,
        });
    }

    private static async Task<IResult> Status(Guid id, HandoffStatusRequest request, HttpContext ctx,
        TotemHandoffRateLimiter limiter, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        using var lease = await limiter.AcquireAsync($"{ip}:{id}", "status", ct);
        if (!lease.IsAcquired) return TooMany();
        if (!TryDecodeHash(request.StatusToken, out var hash)) return Invalid();

        var handoff = await db.TotemBookingHandoffs.SingleOrDefaultAsync(x => x.Id == id && x.StatusTokenHash == hash, ct);
        if (handoff is null) return Invalid();

        var now = time.GetUtcNow();
        if (handoff.Status == TotemBookingHandoffStatus.Pending && handoff.ExpiresAt <= now)
        {
            handoff.MarkExpired(now);
            await db.SaveChangesAsync(ct);
        }

        return handoff.Status switch
        {
            TotemBookingHandoffStatus.Pending => Results.Ok(new { status = "PENDING", expiresAt = handoff.ExpiresAt }),
            TotemBookingHandoffStatus.Expired => Results.Ok(new { status = "EXPIRED" }),
            TotemBookingHandoffStatus.Completed => Results.Ok(await CompletedPayload(db, handoff, ct)),
            _ => Invalid(),
        };
    }

    private static async Task<object> CompletedPayload(ApplicationDbContext db, TotemBookingHandoff handoff, CancellationToken ct)
    {
        var row = await (from r in db.Reservations.AsNoTracking().Where(x => x.Id == handoff.ReservationId)
                         join p in db.Professionals.AsNoTracking() on r.ProfessionalId equals p.Id
                         join room in db.Rooms.AsNoTracking() on r.RoomId equals room.Id
                         select new { p.Name, room.RoomName, r.StartAt }).SingleOrDefaultAsync(ct);
        return new { status = "COMPLETED", professionalName = row?.Name, startAt = row?.StartAt, roomName = row?.RoomName };
    }

    // AUTHORIZATION: {id} alone NEVER authorizes cancellation. The row is fetched ONLY when
    // SHA-256(base64url-decode(statusToken)) == StatusTokenHash for that same id, and no
    // mutation happens before that check passes. A bad / missing / wrong-handoff token yields
    // the same generic INVALID_HANDOFF as an unknown id — no existence oracle.
    private static async Task<IResult> Cancel(Guid id, HandoffStatusRequest request, HttpContext ctx,
        TotemHandoffRateLimiter limiter, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        using var lease = await limiter.AcquireAsync(ip, "cancel", ct);
        if (!lease.IsAcquired) return TooMany();
        if (!TryDecodeHash(request.StatusToken, out var hash)) return Invalid();
        var handoff = await db.TotemBookingHandoffs.SingleOrDefaultAsync(x => x.Id == id && x.StatusTokenHash == hash, ct);
        if (handoff is null) return Invalid();   // wrong token OR unknown id — indistinguishable
        if (handoff.Status == TotemBookingHandoffStatus.Pending)
        {
            handoff.MarkExpired(time.GetUtcNow());
            db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid(), Action = "TOTEM_HANDOFF_CANCELLED", Result = "SUCCEEDED",
                TargetEntityType = "TOTEM_HANDOFF", TargetEntityId = handoff.Id, OccurredAt = time.GetUtcNow(), CorrelationId = ctx.TraceIdentifier });
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(new { status = "EXPIRED" });
    }
}

public sealed record CreateHandoffRequest(Guid ProfessionalId) : IStrictModuleRequest;
public sealed record HandoffStatusRequest(string StatusToken) : IStrictModuleRequest;
```

> Note: confirm `Room` exposes `RoomName` — if the property is `Name`, use `room.Name`. Adjust `AuditEntry` construction to the exact shape used elsewhere in `TotemEndpoints.cs` (`Result`, `TargetEntityType`, `CorrelationId`).

- [ ] **Step 4: Register + run all handoff API + rate-limit tests**

`Program.cs`: `app.MapTotemBookingHandoffEndpoints();`
Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter "TotemBookingHandoffApiTests|TotemHandoffRateLimitTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add recepcaototem/Features/Totem/TotemBookingHandoffEndpoints.cs recepcaototem/Program.cs tests/GestaoPredio.IntegrationTests/TotemBookingHandoffApiTests.cs tests/GestaoPredio.IntegrationTests/TotemHandoffRateLimitTests.cs
git commit -m "feat(totem): booking-handoff create/status/cancel endpoints (anonymous, generic errors)"
```

---

### Task 12: `claim` endpoint (public `StartedAt`)

**Files:**
- Modify: `recepcaototem/Features/Totem/TotemBookingHandoffEndpoints.cs` (add `Claim` handler + request record)
- Test: extend `tests/GestaoPredio.IntegrationTests/TotemBookingHandoffApiTests.cs`

**Interfaces:**
- Produces: `POST /api/totem/booking-handoffs/claim` `{ handoffToken }` (`AllowAnonymous`, `claim` limiter bucket) → `200 { status: "STARTED", expiresAt }` | `400 INVALID_HANDOFF` (bad token) | `410 HANDOFF_EXPIRED` (not `Pending` or already past `ExpiresAt`). No PII.

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public async Task Claim_marks_started_without_auth_extends_the_window_once_and_returns_no_pii()
{
    await factory.ResetAsync();
    var prof = await SeedActiveProfessionalAsync("Dra. Ana", "Fisioterapia");
    factory.FreezeTime(DateTimeOffset.UtcNow);
    var b = await CreateHandoffAsync(prof);

    factory.FreezeTime(factory.UtcNow.AddMinutes(4));
    var first = await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken });
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    var body = await first.Content.ReadAsStringAsync();
    Assert.Contains("STARTED", body);
    Assert.DoesNotContain("Dra. Ana", body);          // no PII / professional name
    Assert.DoesNotContain(prof.ToString(), body);     // no professionalId

    // window extended to now+10 (=14), still under the 20-min ceiling
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var row = await db.TotemBookingHandoffs.SingleAsync();
    Assert.Equal(factory.UtcNow.AddMinutes(10), row.ExpiresAt);
    var started = row.StartedAt;

    factory.FreezeTime(factory.UtcNow.AddMinutes(3));
    await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken });
    await using var scope2 = factory.Services.CreateAsyncScope();
    var row2 = await scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>().TotemBookingHandoffs.SingleAsync();
    Assert.Equal(started, row2.StartedAt);            // not re-set
    Assert.Equal(factory.UtcNow.AddMinutes(7), row2.ExpiresAt);  // unchanged from the first claim's 14
}

[Fact]
public async Task Claim_after_expiry_is_410_and_bad_token_is_400()
{
    await factory.ResetAsync();
    var prof = await SeedActiveProfessionalAsync();
    factory.FreezeTime(DateTimeOffset.UtcNow);
    var b = await CreateHandoffAsync(prof);
    Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = "xxx" })).StatusCode);
    factory.FreezeTime(factory.UtcNow.AddMinutes(6));
    Assert.Equal(HttpStatusCode.Gone, (await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken })).StatusCode);
}
```

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Implement `Claim`**

```csharp
private static async Task<IResult> Claim(HandoffClaimRequest request, HttpContext ctx,
    TotemHandoffRateLimiter limiter, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    using var lease = await limiter.AcquireAsync(ip, "claim", ct);
    if (!lease.IsAcquired) return TooMany();
    if (!TryDecodeHash(request.HandoffToken, out var hash)) return Invalid();

    var handoff = await db.TotemBookingHandoffs.SingleOrDefaultAsync(x => x.HandoffTokenHash == hash, ct);
    var now = time.GetUtcNow();
    if (handoff is null || !handoff.IsUsable(now)) return Expired();

    handoff.MarkStarted(now, HandoffWindows.Grace, handoff.CreatedAt + HandoffWindows.HardCeiling);
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { status = "STARTED", expiresAt = handoff.ExpiresAt });
}

public sealed record HandoffClaimRequest(string HandoffToken) : IStrictModuleRequest;
```

- [ ] **Step 4: Run + commit**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter TotemBookingHandoffApiTests`
```bash
git add recepcaototem/Features/Totem/TotemBookingHandoffEndpoints.cs tests/GestaoPredio.IntegrationTests/TotemBookingHandoffApiTests.cs
git commit -m "feat(totem): public claim endpoint — StartedAt + grace before authentication"
```

---

### Task 13: `resolve` endpoint (CustomerPolicy, read-only)

**Files:**
- Modify: `recepcaototem/Features/Customers/CustomerSchedulingEndpoints.cs` (add route + handler + request/response records)
- Test: `tests/GestaoPredio.IntegrationTests/CustomerHandoffApiTests.cs` (new)

**Interfaces:**
- Consumes: `TotemBookingHandoffEndpoints.TryDecodeHash`, `TotemHandoffRateLimiter` (`resolve` bucket), `GetCustomer(principal, db, ct)` (existing private helper — reuse).
- Produces: `POST /api/customer/booking-handoffs/resolve` (`CustomerPolicy` + `AntiforgeryFilter`) `{ handoffToken }` → `200 { handoffId, professionalId, professionalName, profession, expiresAt }` | `410 HANDOFF_EXPIRED` | `400 INVALID_HANDOFF`.

- [ ] **Step 1: Failing tests** — authenticated customer resolves a `Pending` handoff → gets the professional context; an expired handoff → `410`; an anonymous call → `401`.

```csharp
[Fact]
public async Task Resolve_returns_professional_context_for_an_authenticated_customer()
{
    await factory.ResetAsync();
    var seed = await SeedCustomerAsync();                       // creates CUSTOMER user + Customer row
    var prof = await SeedActiveProfessionalAsync("Dra. Ana", "Fisioterapia");
    var b = await CreateHandoffAsync(prof);
    Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(seed.Email, seed.Password)).StatusCode);
    var r = await factory.PostWithCsrfAsync("/api/customer/booking-handoffs/resolve", new { handoffToken = b.HandoffToken });
    Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    var body = await r.Content.ReadFromJsonAsync<ResolveBody>();
    Assert.Equal(prof, body!.ProfessionalId);
    Assert.Equal("Dra. Ana", body.ProfessionalName);
}
```

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Implement** — register in `MapCustomerSchedulingEndpoints` group: `group.MapPost("/booking-handoffs/resolve", ResolveHandoff).AddEndpointFilter<AntiforgeryFilter>();`. Handler acquires the `resolve` limiter bucket, `TryDecodeHash`, loads the handoff by `HandoffTokenHash`, `410` if `!IsUsable(now)`, else joins `Professionals` and returns the context. Read-only — no `SaveChanges`.

- [ ] **Step 4: Run + commit**

```bash
git add recepcaototem/Features/Customers/CustomerSchedulingEndpoints.cs tests/GestaoPredio.IntegrationTests/CustomerHandoffApiTests.cs
git commit -m "feat(customer): booking-handoffs/resolve — read-only professional context"
```

---

### Task 14: atomic completion + idempotent retry on `POST /api/customer/reservations`

**Files:**
- Modify: `recepcaototem/Features/Customers/CustomerSchedulingEndpoints.cs` (`CustomerReservationRequest` + `CreateReservation`)
- Test: extend `tests/GestaoPredio.IntegrationTests/CustomerHandoffApiTests.cs`

**Interfaces:**
- Consumes: `TotemBookingHandoffEndpoints.TryDecodeHash`, `TotemBookingHandoff.Complete`, existing `CreateReservation` transaction/lock/availability path.
- Produces: `CustomerReservationRequest(Guid ProfessionalId, DateTimeOffset StartAt, DateTimeOffset EndAt, string? HandoffToken)`. Behaviour per spec §9.6 CASE 1–4 + concurrency. `201` on real creation, `200` on idempotent replay, `409 HANDOFF_ALREADY_USED` on non-matching replay, `410 HANDOFF_EXPIRED` on expired, `400 INVALID_HANDOFF` on bad/mismatched.

- [ ] **Step 1: Failing tests** (the retry-idempotency suite — this is where the retry test lives)

```csharp
private async Task<(string Email, string Password, Guid ProfessionalId, string Token, DateTimeOffset Start)> ArrangeBookableHandoffAsync()
{
    await factory.SeedDefaultOperatingHoursAsync();
    var seed = await SeedCustomerAsync();
    var prof = await SeedActiveProfessionalWithRoomAsync();      // active professional + active room + availability window
    var b = await CreateHandoffAsync(prof.ProfessionalId);
    await factory.LoginAsync(seed.Email, seed.Password);
    // phone opens the QR: claim then resolve
    await factory.Client.PostAsJsonAsync("/api/totem/booking-handoffs/claim", new { handoffToken = b.HandoffToken });
    var start = DateTimeOffset.UtcNow.Date.AddDays(1).AddHours(10);
    return (seed.Email, seed.Password, prof.ProfessionalId, b.HandoffToken, start);
}

[Fact]
public async Task First_call_creates_one_reservation_and_completes_the_handoff()
{
    var a = await ArrangeBookableHandoffAsync();
    var res = await factory.PostWithCsrfAsync("/api/customer/reservations",
        new { professionalId = a.ProfessionalId, startAt = a.Start, endAt = a.Start.AddHours(1), handoffToken = a.Token });
    Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    var reservation = await res.Content.ReadFromJsonAsync<ReservationPayload>();
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Assert.Equal(1, await db.Reservations.CountAsync());
    var handoff = await db.TotemBookingHandoffs.SingleAsync();
    Assert.Equal(TotemBookingHandoffStatus.Completed, handoff.Status);
    Assert.Equal(reservation!.Id, handoff.ReservationId);
}

[Fact]
public async Task Retry_by_the_same_customer_returns_the_same_reservation_without_duplicating()
{
    var a = await ArrangeBookableHandoffAsync();
    var body = new { professionalId = a.ProfessionalId, startAt = a.Start, endAt = a.Start.AddHours(1), handoffToken = a.Token };
    var first = await factory.PostWithCsrfAsync("/api/customer/reservations", body);
    Assert.Equal(HttpStatusCode.Created, first.StatusCode);
    var firstId = (await first.Content.ReadFromJsonAsync<ReservationPayload>())!.Id;

    var retry = await factory.PostWithCsrfAsync("/api/customer/reservations", body);
    Assert.Equal(HttpStatusCode.OK, retry.StatusCode);                      // 200, not 201, not 409
    Assert.Equal(firstId, (await retry.Content.ReadFromJsonAsync<ReservationPayload>())!.Id);

    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Assert.Equal(1, await db.Reservations.CountAsync());                    // count stays 1
    Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "RESERVATION_CREATED"));   // no new creation audit
    Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "TOTEM_HANDOFF_COMPLETED"));
}

[Fact]
public async Task Another_customer_cannot_recover_the_reservation_through_the_handoff()
{
    var a = await ArrangeBookableHandoffAsync();
    var body = new { professionalId = a.ProfessionalId, startAt = a.Start, endAt = a.Start.AddHours(1), handoffToken = a.Token };
    Assert.Equal(HttpStatusCode.Created, (await factory.PostWithCsrfAsync("/api/customer/reservations", body)).StatusCode);

    var intruder = await SeedCustomerAsync();
    await factory.LoginAsync(intruder.Email, intruder.Password);
    var res = await factory.PostWithCsrfAsync("/api/customer/reservations", body);
    Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    Assert.Equal("HANDOFF_ALREADY_USED", (await res.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    var text = await res.Content.ReadAsStringAsync();
    Assert.DoesNotContain(a.ProfessionalId.ToString(), text);              // nothing about the reservation
}

[Fact]
public async Task Concurrent_duplicate_creates_exactly_one_reservation()
{
    var a = await ArrangeBookableHandoffAsync();
    var body = new { professionalId = a.ProfessionalId, startAt = a.Start, endAt = a.Start.AddHours(1), handoffToken = a.Token };
    var t1 = factory.PostWithCsrfAsync("/api/customer/reservations", body);
    var t2 = factory.PostWithCsrfAsync("/api/customer/reservations", body);
    var results = await Task.WhenAll(t1, t2);
    var codes = results.Select(r => (int)r.StatusCode).OrderBy(x => x).ToArray();
    Assert.Contains(201, codes);
    Assert.All(codes, c => Assert.True(c is 200 or 201));                  // loser replays idempotently, never 409
    var ids = new List<Guid>();
    foreach (var r in results) ids.Add((await r.Content.ReadFromJsonAsync<ReservationPayload>())!.Id);
    Assert.Single(ids.Distinct());                                        // both responses name the SAME reservation
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Assert.Equal(1, await db.Reservations.CountAsync());                   // exactly one row
    Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "RESERVATION_CREATED"));
    Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "TOTEM_HANDOFF_COMPLETED"));
}

[Fact]
public async Task Reservation_without_handoffToken_is_unchanged()
{
    // existing CreateReservation happy-path regression, no handoffToken → 201, no handoff rows
}
```

- [ ] **Step 2: Run, verify fail** — `dotnet test tests/GestaoPredio.IntegrationTests --filter CustomerHandoffApiTests`.

- [ ] **Step 3: Implement the branch in `CreateReservation`**

Add `string? HandoffToken` to `CustomerReservationRequest`. Near the top of `CreateReservation`, after `customer` is resolved and before the write transaction, when `request.HandoffToken is { } rawHandoff`:

```csharp
if (!TotemBookingHandoffEndpoints.TryDecodeHash(rawHandoff, out var handoffHash))
    return Results.Json(new ApiError("INVALID_HANDOFF", "Não foi possível validar este convite."), statusCode: 400);

var handoff = await db.TotemBookingHandoffs.SingleOrDefaultAsync(x => x.HandoffTokenHash == handoffHash, ct);
if (handoff is null) return Results.Json(new ApiError("INVALID_HANDOFF", "..."), statusCode: 400);
var nowHandoff = time.GetUtcNow();

// CASE 4
if (handoff.Status == TotemBookingHandoffStatus.Expired ||
    (handoff.Status == TotemBookingHandoffStatus.Pending && handoff.ExpiresAt <= nowHandoff))
    return Results.Json(new ApiError("HANDOFF_EXPIRED", "Este QR Code expirou."), statusCode: 410);

// CASE 2 / 3 — replay
if (handoff.Status == TotemBookingHandoffStatus.Completed)
    return await ReplayHandoffAsync(db, handoff, customer, request.ProfessionalId, ct);

// CASE 1 — fall through to the normal creation path, but validate the professional match first
if (handoff.ProfessionalId != request.ProfessionalId)
    return Results.Json(new ApiError("INVALID_HANDOFF", "..."), statusCode: 400);
```

`ReplayHandoffAsync`: load the linked `Reservation`; if `handoff.ReservationId is null` → `409 HANDOFF_ALREADY_USED`; if the reservation is missing, or `reservation.CustomerId != customer.Id`, or `reservation.ProfessionalId != handoff.ProfessionalId`, or `reservation.Status != ReservationStatus.Approved` → `409 HANDOFF_ALREADY_USED` (no reservation data in the body); else `Results.Ok(reservation.ToResponse(roomName, professionalName))` (**200**).

In the CASE 1 path, inside the existing transaction, right before `await db.SaveChangesAsync(ct)`:
```csharp
if (handoff is not null)   // the tracked instance loaded above
{
    handoff.Complete(reservation.Id, nowHandoff);
    db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid(), Action = "TOTEM_HANDOFF_COMPLETED", Result = "SUCCEEDED",
        TargetEntityType = "TOTEM_HANDOFF", TargetEntityId = handoff.Id, TargetUserId = customer.ApplicationUserId,
        OccurredAt = nowHandoff, CorrelationId = context.TraceIdentifier });
}
```

**Concurrency-loser flow (explicit — do NOT keep using the losing transaction/context blindly).** Wrap the `SaveChangesAsync`/`CommitAsync`:

```csharp
try
{
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return Results.Created($"/api/customer/reservations/{reservation.Id}", reservation.ToResponse(roomName, professional.Name)); // 201 — unchanged
}
catch (DbUpdateConcurrencyException)
{
    // 1. end the losing transaction
    await transaction.RollbackAsync(ct);
    // 2. drop every entity this request tracked (its would-be Reservation included) so nothing stale leaks forward
    db.ChangeTracker.Clear();
    // 3. re-read the handoff in a consistent state
    var fresh = await db.TotemBookingHandoffs.AsNoTracking().SingleOrDefaultAsync(x => x.HandoffTokenHash == handoffHash, ct);
    // 4. if the winner already completed it, replay idempotently after re-checking ALL ownership invariants
    if (fresh is { Status: TotemBookingHandoffStatus.Completed })
        return await ReplayHandoffAsync(db, fresh, customer, request.ProfessionalId, ct);
    // 5. cannot prove the invariants → appropriate generic error; 6. never create a second Reservation
    return Results.Json(new ApiError("HANDOFF_ALREADY_USED", "Este convite já foi utilizado."), statusCode: 409);
}
```

`ReplayHandoffAsync` re-runs the full CASE 2/3 check on the freshly-read handoff: `fresh.ReservationId` present → load that `Reservation`; require `reservation is not null && reservation.CustomerId == customer.Id && reservation.ProfessionalId == fresh.ProfessionalId && reservation.Status == ReservationStatus.Approved` → `200 OK` with `reservation.ToResponse(...)`; otherwise `409 HANDOFF_ALREADY_USED` with **no** reservation data. It opens no write transaction and issues no `SaveChanges` (pure read). Because both branches converge on the *winner's* row, the losing request never inserts — `Reservations.Count == 1`.

> Implementation note: `CreateReservation` already takes `TimeProvider time`, so `nowHandoff` is available before the write transaction. `handoff` stays a tracked entity so `Complete` persists in the same `SaveChanges`. `ReplayHandoffAsync` must not reuse a `handoff` instance from a cleared/aborted context — it takes the `fresh` (re-read) instance and re-queries the reservation itself.

The concurrent test `Concurrent_duplicate_creates_exactly_one_reservation` (Step 1) asserts `Reservations.CountAsync() == 1`, that the loser's status is `200` or `201` (never `409` when it *is* the same customer), and that exactly one `RESERVATION_CREATED` audit exists.

- [ ] **Step 4: Run the whole customer + handoff suite**

Run: `dotnet test tests/GestaoPredio.IntegrationTests --filter "CustomerHandoffApiTests|CustomerApiTests|TotemBookingHandoffApiTests"`
Expected: PASS (including the untouched `CustomerApiTests` regression).

- [ ] **Step 5: Commit**

```bash
git add recepcaototem/Features/Customers/CustomerSchedulingEndpoints.cs tests/GestaoPredio.IntegrationTests/CustomerHandoffApiTests.cs
git commit -m "feat(customer): atomic handoff completion + idempotent retry on reservation create"
```

---

### Task 15: exact dashboard aggregates

**Files:**
- Modify: `src/GestaoPredio.Application/Dashboard/DashboardModels.cs` (`DashboardCounts` gains `int TodayCheckIns`)
- Modify: `src/GestaoPredio.Infrastructure/Dashboard/PostgreSqlDashboardReader.cs` (one `CountAsync`)
- Modify: `recepcaototem/Features/Reservations/ProfessionalReservationEndpoints.cs` (`List` gains optional `from`/`to` on `StartAt`)
- Test: `tests/GestaoPredio.IntegrationTests/DashboardApiTests.cs` (extend); `ProfessionalAvailabilitySchedulingTests.cs` or a new `ProfessionalReservationRangeTests.cs`

**Interfaces:**
- Produces:
  - `DashboardCounts(… , int TodayCheckIns)` — every constructor call site updated (the reader is the only producer; JSON contract gains a field, backward-compatible for the frontend).
  - `GET /api/professional/reservations?from=<iso>&to=<iso>` — filters `StartAt >= from && StartAt < to`; `from`/`to` both optional; invalid (`from >= to`) → `400`.

- [ ] **Step 1: Failing tests**

```csharp
// DashboardApiTests — add
[Fact]
public async Task Dashboard_reports_todays_check_in_count_exactly()
{
    await factory.ResetAsync();
    // seed 2 visits with ArrivedAt today + 1 visit ArrivedAt yesterday (via service scope + Visit.Arrive)
    await LoginAsAdminAsync();
    var snap = await factory.Client.GetFromJsonAsync<DashboardSnapshotDto>("/api/admin/dashboard");
    Assert.Equal(2, snap!.Counts.TodayCheckIns);
}

// ProfessionalReservationRangeTests — new
[Fact]
public async Task Professional_reservations_can_be_filtered_to_a_day_window_with_exact_total()
{
    await factory.ResetAsync();
    var seed = await SeedProfessionalWithReservationsAsync(todayCount: 3, otherDayCount: 5);   // 8 total
    await factory.LoginAsync(seed.Email, seed.Password);
    var from = DateTimeOffset.UtcNow.Date; var to = from.AddDays(1);
    var page = await factory.Client.GetFromJsonAsync<PagedReservations>(
        $"/api/professional/reservations?status=all&from={from:o}&to={to:o}&page=1&pageSize=1");
    Assert.Equal(3, page!.TotalCount);
}
```

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Implement**

`DashboardModels.cs`: append `, int TodayCheckIns` to the `DashboardCounts` record (last positional param).

`PostgreSqlDashboardReader.ReadAsync`: after `inServiceVisits`, add
```csharp
var todayCheckIns = await db.Visits.AsNoTracking()
    .CountAsync(x => x.ArrivedAt >= day.StartAt && x.ArrivedAt < day.EndAt, cancellationToken);
```
and pass `todayCheckIns` into the `new DashboardCounts(...)` construction (append as the final arg).

`ProfessionalReservationEndpoints.List`: add `DateTimeOffset? from, DateTimeOffset? to` to the handler signature (minimal API binds them from query). After `if (actualStatus != "all")`, add `if (from >= to) return Results.BadRequest(new ApiError("INVALID_DATE_RANGE", "O intervalo é inválido."));` and `if (from is not null) reservations = reservations.Where(x => x.StartAt >= from); if (to is not null) reservations = reservations.Where(x => x.StartAt < to);`.

- [ ] **Step 4: Run** — `dotnet test tests/GestaoPredio.IntegrationTests --filter "DashboardApiTests|ProfessionalReservationRangeTests|DashboardModelTests"`. Fix any other `DashboardCounts(` construction site the compiler flags.

- [ ] **Step 5: Commit**

```bash
git add src/GestaoPredio.Application/Dashboard/DashboardModels.cs src/GestaoPredio.Infrastructure/Dashboard/PostgreSqlDashboardReader.cs recepcaototem/Features/Reservations/ProfessionalReservationEndpoints.cs tests/GestaoPredio.IntegrationTests
git commit -m "feat(dashboard): exact TodayCheckIns count + from/to on professional reservations"
```

---

# GROUP E — Handoff frontend / mobile

### Task 16: handoff API clients in `modules.ts`

**Files:**
- Modify: `recepcaototem/ClientApp/src/api/modules.ts`
- Test: `recepcaototem/ClientApp/src/api/modules.handoff.test.ts` (new)

**Interfaces:**
- Produces:
  - `totemApi.createHandoff(professionalId: string): Promise<{ id: string; handoffToken: string; statusToken: string; expiresAt: string; professionalName: string; profession: string }>`
  - `totemApi.pollHandoff(id: string, statusToken: string): Promise<HandoffStatusDto>` where `HandoffStatusDto = { status: 'PENDING'; expiresAt: string } | { status: 'COMPLETED'; professionalName: string; startAt: string; roomName: string | null } | { status: 'EXPIRED' }`
  - `totemApi.cancelHandoff(id: string, statusToken: string): Promise<{ status: string }>`
  - `totemApi.claimHandoff(handoffToken: string): Promise<{ status: string; expiresAt: string }>`
  - `customerApi.resolveHandoff(handoffToken: string): Promise<{ handoffId: string; professionalId: string; professionalName: string; profession: string; expiresAt: string }>`
  - `customerApi.createReservation(input: { professionalId: string; startAt: string; endAt: string; handoffToken?: string })` — add `handoffToken?`.
  - `professionalReservationsApi.list` query type gains `from?: string; to?: string`.

- [ ] **Step 1: Failing test** — mock `apiClient` (`vi.mock('./client')`), assert each method calls the right path/body.

```ts
test('pollHandoff posts the status token in the body, not the URL', async () => {
  vi.mocked(apiClient.post).mockResolvedValue({ status: 'PENDING', expiresAt: 'x' })
  await totemApi.pollHandoff('h1', 'stok')
  expect(apiClient.post).toHaveBeenCalledWith('/api/totem/booking-handoffs/h1/status', { statusToken: 'stok' })
})
```

- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** the methods on the existing `totemApi` / `customerApi` objects; add the exported `HandoffStatusDto` union; widen `professionalReservationsApi.list`'s query param type.
- [ ] **Step 4: Run + `tsc -b` + commit**

```bash
git add src/api/modules.ts src/api/modules.handoff.test.ts
git commit -m "feat(api): handoff client methods + createReservation handoffToken"
```

---

### Task 17: `TotemProfessionals` — "Continuar" creates the handoff

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/TotemProfessionals.tsx`
- Test: `recepcaototem/ClientApp/src/pages/TotemProfessionals.test.tsx` (new or extend)

**Interfaces:**
- Consumes: `totemApi.createHandoff` (Task 16), `useNavigate`.
- Behaviour: "Continuar →" (enabled only with an active selection) calls `createHandoff(active.id)`, then `navigate('/totem/handoff', { state: { handoffId, statusToken, professionalName, profession, expiresAt } })`. On failure: inline message + stay on the carousel. **No** navigation to `/cliente/*`.

- [ ] **Step 1: Failing test** — mock `totemApi.createHandoff`; select a professional; click Continuar; assert `navigate` called with `/totem/handoff` + the state; assert it is NOT called with any `/cliente` path.
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** — replace the `onClick={() => active && navigate('/cliente/agendar?professionalId=' + active.id)}` with an async handler that creates the handoff and navigates to `/totem/handoff` with `state`; add a `useState` error string rendered near the button.
- [ ] **Step 4: Run + `tsc -b` + commit**

```bash
git add src/pages/TotemProfessionals.tsx src/pages/TotemProfessionals.test.tsx
git commit -m "feat(totem): Continuar creates a booking handoff and opens /totem/handoff"
```

---

### Task 18: `TotemHandoff` page + route

**Files:**
- Create: `recepcaototem/ClientApp/src/pages/TotemHandoff.tsx`
- Create: `recepcaototem/ClientApp/src/pages/TotemHandoff.test.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx` (route)
- Modify: `recepcaototem/ClientApp/src/styles.css` (append `.totem-handoff-*`)

**Interfaces:**
- Consumes: `totemApi.pollHandoff`, `totemApi.cancelHandoff`, `totemApi.createHandoff` (regenerate), `LumisBackground`, `KioskClock`, `QRCode.toDataURL`, `useLocation().state`, `useNavigate`.
- Behaviour: reads `{ handoffId, statusToken, professionalName, profession, expiresAt }` from `location.state`. **No `state` → `<Navigate to="/totem" replace />`** (F5 safety; nothing read from storage). Renders "Continue no seu celular", professional name + profession, a QR (`<img alt="QR Code para continuar o agendamento no seu celular">`) encoding `${window.location.origin}/cliente/agendar?handoff=${handoffToken}` — **wait**: the page only has `statusToken` + `handoffId` in state, not `handoffToken`. **Fix:** Task 17 must also pass `handoffToken` in the navigation state. Update Task 17 interface: state = `{ handoffId, handoffToken, statusToken, professionalName, profession, expiresAt }`. The QR encodes the `handoffToken`; `statusToken` is used only for polling.
- Polling: `setInterval(2000)` → `pollHandoff(handoffId, statusToken)`. `PENDING` → update countdown from `expiresAt`. `COMPLETED` → stop, show ✓ screen (professional, `startAt` date+time, "Tudo certo por aqui.", "Retornando ao início..."), `setTimeout(6000)` → `navigate('/totem', { replace: true })`. `EXPIRED` → stop, show "Este QR Code expirou." + `[Gerar novo QR]` (calls `createHandoff` again for the same professional — needs `professionalId` in state too; add it) + `[Escolher outro profissional]` (`navigate('/totem/profissionais')`). Network error → count consecutive failures; after 5 show "Reconectando…" + `[Tentar novamente]`.
- Cancel: `[← Escolher outro profissional]` calls `cancelHandoff(handoffId, statusToken)` then `navigate('/totem/profissionais')`.
- Countdown in `aria-live="polite"`, text updated ~every 30 s.

- [ ] **Step 1: Failing tests**

```tsx
test('no navigation state → redirects to /totem', () => {
  render(<MemoryRouter initialEntries={['/totem/handoff']}><Routes>
    <Route path="/totem/handoff" element={<TotemHandoff />} />
    <Route path="/totem" element={<div>totem home</div>} />
  </Routes></MemoryRouter>)
  expect(screen.getByText('totem home')).toBeInTheDocument()
})

test('polls and swaps to the done screen on COMPLETED, then auto-returns', async () => {
  vi.useFakeTimers()
  vi.mocked(totemApi.pollHandoff)
    .mockResolvedValueOnce({ status: 'PENDING', expiresAt: future })
    .mockResolvedValue({ status: 'COMPLETED', professionalName: 'Dra. Ana', startAt: startIso, roomName: 'Sala 1' })
  renderWithState({ handoffId: 'h1', handoffToken: 'H', statusToken: 'S', professionalId: 'p1', professionalName: 'Dra. Ana', profession: 'Fisioterapia', expiresAt: future })
  await vi.advanceTimersByTimeAsync(2000)
  await vi.advanceTimersByTimeAsync(2000)
  expect(screen.getByText(/agendamento concluído/i)).toBeInTheDocument()
  await vi.advanceTimersByTimeAsync(6000)
  expect(navigateSpy).toHaveBeenCalledWith('/totem', { replace: true })
})

test('EXPIRED shows regenerate + choose-another; choose-another leaves via /totem/profissionais', async () => { /* … */ })
test('choose-another cancels the handoff', async () => {
  vi.mocked(totemApi.pollHandoff).mockResolvedValue({ status: 'PENDING', expiresAt: future })
  renderWithState({ /* … */ })
  fireEvent.click(await screen.findByRole('button', { name: /escolher outro profissional/i }))
  expect(totemApi.cancelHandoff).toHaveBeenCalledWith('h1', 'S')
})
test('never writes to localStorage/sessionStorage', () => { /* spy setItem, run a full cycle, assert not called */ })
```

- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** the page (state guard, `useEffect` polling with cleanup, `QRCode.toDataURL(url, { margin: 1, width: 320, color: { dark: '#181818', light: '#ffffff' } })` in an effect, countdown via `useState` + a 1 s tick that only re-renders text at 30 s boundaries or use `Math.ceil` mm:ss each tick — keep it simple: tick every 1 s, render mm:ss, wrap in `aria-live="polite"`). Add the route to `App.tsx`: `<Route path="/totem/handoff" element={<TotemHandoff />} />` next to the other `/totem/*` public routes.
- [ ] **Step 4: CSS** — `.totem-handoff` on `LumisPageShell`; centred column, QR `width: min(70vw, 320px)`, countdown below, buttons ≥44 px.
- [ ] **Step 5: Run + `tsc -b` + `vite build` + commit**

```bash
git add src/pages/TotemHandoff.tsx src/pages/TotemHandoff.test.tsx src/App.tsx src/styles.css
git commit -m "feat(totem): /totem/handoff — QR + countdown + 2s polling + auto-return"
```

---

### Task 19: `CustomerBooking` `?handoff=` + `Login` claim-on-mount

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/customer/CustomerBooking.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/Login.tsx` (add the `claim` effect)
- Test: `recepcaototem/ClientApp/src/pages/customer/CustomerBooking.handoff.test.tsx` (new); extend `Login.test.tsx`

**Interfaces:**
- Consumes: `totemApi.claimHandoff`, `customerApi.resolveHandoff`, `customerApi.createReservation({ …, handoffToken })`.
- `CustomerBooking` behaviour: on mount, if `params.get('handoff')` present → `void totemApi.claimHandoff(token).catch(() => {})` (fire-and-forget, idempotent) then `customerApi.resolveHandoff(token)` → set `professionalId` from the result and remember `handoffToken` in state; on `410`/error → show "Este convite expirou. Você pode escolher um profissional normalmente." and fall back to the normal `?professionalId=` / first-professional logic. When `handoffToken` is set, `createReservation` includes it. After a successful resolve, `navigate('/cliente/agendar', { replace: true })` to drop `?handoff=` from the address bar (keep `handoffToken` in component state).
- `Login` behaviour: `useEffect` on mount — if `audience === 'customer'` and `safeCustomerReturnUrl(params.get('returnUrl'))` contains `handoff=`, extract the token and `void apiClient.post('/api/totem/booking-handoffs/claim', { handoffToken }).catch(() => {})`.

- [ ] **Step 1: Failing tests** — mock the api; assert `claimHandoff` + `resolveHandoff` called on mount with the token; assert `professionalId` preselected from resolve; assert `createReservation` called with `handoffToken`; assert the `410` path renders the fallback message and still lets the user pick; assert `Login` calls claim when the returnUrl carries a handoff.
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** both effects.
- [ ] **Step 4: Run + `tsc -b` + commit**

```bash
git add src/pages/customer/CustomerBooking.tsx src/pages/customer/CustomerBooking.handoff.test.tsx src/pages/Login.tsx src/pages/Login.test.tsx
git commit -m "feat(customer): consume ?handoff= (claim + resolve + atomic create); Login claims on mount"
```

---

### Task 20: portals test — Totem never routes to login

**Files:**
- Modify: `recepcaototem/ClientApp/src/frontend-portals.test.ts`

- [ ] **Step 1: Add assertions** — `/totem/handoff` resolves without a `ProtectedRoute` wrapper (anonymous); the string `/cliente/login` and `/login` do not appear as a navigation target from any `/totem/*` page module; `TotemProfessionals` "Continuar" navigates to `/totem/handoff`, never `/cliente/agendar` or `/cliente/login`.
- [ ] **Step 2: Run** `npx vitest run src/frontend-portals.test.ts` — expect FAIL, then adjust source only if a real gap surfaces (it should already hold after Tasks 17–19).
- [ ] **Step 3: Commit**

```bash
git add src/frontend-portals.test.ts
git commit -m "test(portals): Totem flow never navigates to a login screen"
```

---

# GROUP F — Dashboard Customer

### Task 21: `CustomerShell` → sidebar + drawer + LumisPageShell

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/customer/CustomerHome.tsx` (`CustomerShell`)
- Modify: `recepcaototem/ClientApp/src/styles.css` (append `.customer-shell*` dark rules / drawer)
- Test: extend `recepcaototem/ClientApp/src/pages/customer/*` test (or new `CustomerShell.test.tsx`)

**Interfaces:**
- Produces: `CustomerShell` renders a left sidebar (desktop) / hamburger drawer (mobile) with LUMIS logo + nav items **Dashboard** (`/cliente`), **Agendamentos** (`/cliente/agendamentos`), **Novo agendamento** (`/cliente/agendar`) — no items for non-existent routes. Wrapped in `LumisPageShell intensity="muted"`. Keeps `customerApi.me()` + logout.

- [ ] **Step 1: Failing test** — nav has exactly those 3 links with correct `href`s; a `menu`/`dialog` toggle exists with `aria-label`; logo `alt="LUMIS"`.
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** — model the drawer on `AdminLayout`'s `open`/overlay pattern; reuse `.sidebar`/`.admin-shell` structural classes or new `.customer-shell` ones reading `--lumis-*`.
- [ ] **Step 4: Run + `tsc -b` + commit**

```bash
git add src/pages/customer/CustomerHome.tsx src/styles.css src/pages/customer/CustomerShell.test.tsx
git commit -m "feat(customer): dark sidebar+drawer shell on LumisPageShell"
```

---

### Task 22: `CustomerHome` dashboard cards

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/customer/CustomerHome.tsx` (`CustomerHome`)
- Test: `recepcaototem/ClientApp/src/pages/customer/CustomerHome.test.tsx` (new/extend)

**Interfaces:**
- Consumes: `customerApi.me`, `customerApi.reservations({ page: 1, pageSize: 50 })`, `customerApi.issueCheckInToken`, `QRCode.toDataURL`.
- Cards: **Próximo atendimento** (`reservations.items.filter(r => r.status === 'APPROVED' && Date.parse(r.endAt) >= Date.now()).sort by startAt [0]` → professional, room, date, time, status, `[Ver detalhes]` → `/cliente/agendamentos/:id`); **Meu QR Code** (button → `issueCheckInToken(nextReservation.id)`; on success render `<img>` from `QRCode.toDataURL(result.token …)` + spaced `result.manualCode` + `[copiar]`; on `CHECK_IN_NOT_ELIGIBLE` show "Disponível 1 h antes do horário"; never a hardcoded code); **Novo agendamento** (`[Novo agendamento →]` → `/cliente/agendar`); **Próximos agendamentos** (upcoming list); **Histórico recente** (past items from the same list). No notifications block.

- [ ] **Step 1: Failing test** — with mocked api data: next-appointment card shows the professional + a `Ver detalhes` link; QR card shows a `Gerar QR Code` button and, after click, an `<img alt=/QR/>` + the returned `manualCode` (assert the DOM text equals the mock's code, proving it's not hardcoded); no element with text `4 7 2 9 1 6`; history section lists a past reservation.
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run + `tsc -b` + `vite build` + commit**

```bash
git add src/pages/customer/CustomerHome.tsx src/pages/customer/CustomerHome.test.tsx
git commit -m "feat(customer): dashboard — next appointment, real QR card, upcoming/history"
```

---

# GROUP G — Dashboard Professional

### Task 23: `ProfessionalShell` restyle

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/professional/ProfessionalHome.tsx` (`ProfessionalShell`)
- Modify: `recepcaototem/ClientApp/src/styles.css`
- Test: extend/create `ProfessionalShell.test.tsx`

**Interfaces:**
- Produces: `ProfessionalShell` wrapped in `LumisPageShell intensity="muted"`, dark tokens, existing nav items unchanged (Dashboard/Agenda/Disponibilidade/Atendimentos/Reservas/Locações/Financeiro/Perfil), sidebar on desktop + drawer on mobile, topbar `PROFISSIONAL` / "Olá, {first name}" / "Seu espaço, sua agenda, mais possibilidades." / date + avatar.

- [ ] **Step 1: Failing test** — topbar eyebrow `PROFISSIONAL`; the greeting uses `profile.name`'s first token (mock it — assert it is NOT a hardcoded "Dra. Helena"); a drawer toggle with `aria-label`.
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** (reuse the drawer pattern; keep the `Promise.all([me, reservations, visits])` load).
- [ ] **Step 4: Run + `tsc -b` + commit**

```bash
git add src/pages/professional/ProfessionalHome.tsx src/styles.css src/pages/professional/ProfessionalShell.test.tsx
git commit -m "feat(professional): dark LumisPageShell sidebar+drawer shell"
```

---

### Task 24: `ProfessionalDashboard` exact KPIs

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/professional/ProfessionalHome.tsx` (`ProfessionalDashboard`)
- Modify: `recepcaototem/ClientApp/src/api/modules.ts` (already widened in Task 16 — verify `from`/`to` reach the query string)
- Test: `recepcaototem/ClientApp/src/pages/professional/ProfessionalDashboard.test.tsx` (new/extend)

**Interfaces:**
- Consumes: `professionalVisitsApi.list({ status, from, to, page: 1, pageSize: 1 })` (`.totalCount`), `professionalReservationsApi.list({ status: 'all', from, to, page: 1, pageSize: 100 })`, `professionalAvailabilityApi.get()`.
- Cards (all exact via `totalCount`): **Atendimentos hoje** = `totalCount(status='IN_SERVICE') + totalCount(status='ENDED')` for today's window; **Próximo horário** = next `APPROVED` reservation with `endAt >= now`; **Disponibilidade** = today's `effectiveDays` interval total; **Check-ins confirmados hoje** = `totalCount(status='all')` for today. **Agenda de hoje** = the `pageSize:100` list, each row's label derived from the joined `Visit` status (`ENDED`→"Concluído", `IN_SERVICE`→"Em atendimento", `WAITING`→"Aguardando", none→"Agendado", `CANCELLED`→"Cancelado"). Right rail: "Disponibilidade de hoje", "Próximas reservas", "Resumo/avisos" (count of `kind ∈ {RESCHEDULE, CANCELLATION}` `PENDING`).

- [ ] **Step 1: Failing test** — mock `professionalVisitsApi.list` to return `{ items: [], totalCount: 7 }` for the check-ins query and `{ totalCount: 3 }`+`{ totalCount: 2 }` for IN_SERVICE/ENDED; assert the KPI shows `7` and `5` respectively (proving it reads `totalCount`, not `items.length`); assert no KPI renders a value derived from a truncated `items` array; assert an agenda row with a mocked `ENDED` visit renders "Concluído".
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** — replace the current client-side `reservations.filter(...)` counting with the `totalCount` queries; build `todayWindow()` helper (`start = new Date(); start.setHours(0,0,0,0)`, `end = start + 1 day`, ISO). Derive agenda status by joining reservations to `visits` by `reservationId`.
- [ ] **Step 4: Run + `tsc -b` + `vite build` + commit**

```bash
git add src/pages/professional/ProfessionalHome.tsx src/pages/professional/ProfessionalDashboard.test.tsx
git commit -m "feat(professional): dashboard — exact totalCount KPIs + derived agenda status"
```

---

# GROUP H — Dashboard Admin

### Task 25: admin dashboard API clients

**Files:**
- Modify: `recepcaototem/ClientApp/src/api/modules.ts`
- Test: `recepcaototem/ClientApp/src/api/modules.dashboard.test.ts` (new)

**Interfaces:**
- Produces:
  - `dashboardApi.get(signal?): Promise<DashboardSnapshotDto>` → `GET /api/admin/dashboard`. Types: `DashboardCountsDto { activeProfessionals, activeRooms, occupiedRooms, reservedRooms, activeLeases, scheduledLeases, pendingReservations, todayReservations, waitingVisits, inServiceVisits, todayCheckIns }`; `DashboardSnapshotDto { operationalDate, counts, financial, alerts: { total, warning, critical, recent[] }, agenda[], currentVisits[], rooms[] }` (fields as the backend record; `currentVisits[]` = `{ visitId, visitorName, status, professionalName, roomName, arrivedAt, serviceStartedAt, durationMinutes }`; `rooms[]` = `{ roomId, roomName, status, nextCommitmentAt }`).
  - `receptionApi.professionals(signal?): Promise<ReceptionProfessionalDto[]>` → `GET /api/reception/professionals`. `ReceptionProfessionalDto { professionalId, name, profession, operationalStatus, presence, currentRoomId, waitingVisitorsCount, canReceiveVisitor }` (subset of `ReceptionProfessionalResponse`).

- [ ] **Step 1: Failing test** — mock `apiClient.get`; assert path + returned shape typing.
- [ ] **Step 2–4:** implement, run, `tsc -b`, commit.

```bash
git add src/api/modules.ts src/api/modules.dashboard.test.ts
git commit -m "feat(api): dashboardApi.get + receptionApi.professionals clients"
```

---

### Task 26: `AdminDashboard` page + wire `/admin` index + `AdminLayout` restyle

**Files:**
- Create: `recepcaototem/ClientApp/src/pages/admin/AdminDashboard.tsx`
- Create: `recepcaototem/ClientApp/src/pages/admin/AdminDashboard.test.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx` (`/admin` index → `<AdminDashboard />` instead of `<ModuleUnavailable title="Visão geral" />`)
- Modify: `recepcaototem/ClientApp/src/components/AdminLayout.tsx` (wrap content in `LumisPageShell intensity="muted"`, dark tokens; nav unchanged)
- Modify: `recepcaototem/ClientApp/src/styles.css`

**Interfaces:**
- Consumes: `dashboardApi.get()`, `receptionApi.professionals()`.
- KPI cards (all from `counts`, exact): **Atendimentos hoje** = `counts.todayReservations` (+ `counts.pendingReservations` sub-stat "aguardando aprovação"); **Check-ins realizados** = `counts.todayCheckIns`; **Salas ocupadas** = `counts.occupiedRooms` / `counts.activeRooms`; **Reservas hoje** = `counts.todayReservations`.
- Panels: **Recepção · Em atendimento** (`snapshot.currentVisits` table: ordem, cliente (`visitorName`), profissional, status, espera (`durationMinutes`)); **Ocupação das salas** (`snapshot.rooms`: `roomName`, `status` → "Em uso"/"Livre", `nextCommitmentAt`); **Profissionais presentes** (`receptionApi.professionals()` filtered to `presence` = present — use the domain's presence value; label "presentes"); **Atividades/Alertas** (`snapshot.alerts.recent` + the `total`/`warning`/`critical` counts).
- Dense screen → `intensity="muted"` background; tables in `overflow-x:auto`.

- [ ] **Step 1: Failing test** — mock both clients; assert the 4 KPI values equal the mocked `counts` fields (incl. `todayCheckIns`); assert a `currentVisits` row renders its `visitorName` + `professionalName`; assert a room shows "Em uso" for `status === 'OCCUPIED'`; assert "Profissionais presentes" (not "online"); assert **no fabricated** rows when a collection is empty (renders an empty state, not fake data).
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** the page + swap the route + restyle `AdminLayout`.
- [ ] **Step 4: Run + `tsc -b` + `vite build` + commit**

```bash
git add src/pages/admin/AdminDashboard.tsx src/pages/admin/AdminDashboard.test.tsx src/App.tsx src/components/AdminLayout.tsx src/styles.css
git commit -m "feat(admin): real /admin dashboard (KPIs, reception, rooms, presence, alerts)"
```

---

# GROUP I — Responsive / a11y

### Task 27: responsive + accessibility sweep

**Files:**
- Modify: `recepcaototem/ClientApp/src/styles.css` (media queries)
- Modify: touch-ups across `TotemHandoff.tsx`, `AdminDashboard.tsx`, `CustomerHome.tsx`, `ProfessionalHome.tsx`, `Login.tsx`, `TotemCheckIn.tsx` as the tests demand
- Test: `recepcaototem/ClientApp/src/a11y-responsive.test.tsx` (new) + assertions folded into the per-page test files

- [ ] **Step 1: Write the failing tests**
  - `styles.css` contains KPI-grid media queries stepping 4→2→1 columns (`.lumis-kpi-grid` or the per-dashboard grid class): assert `@media (max-width: 1024px)` and `(max-width: 640px)` rules exist for the grid class.
  - Every dashboard table wrapper class has `overflow-x: auto`.
  - `TotemHandoff` countdown container has `aria-live="polite"`.
  - The handoff QR `<img>` has a non-empty `alt`.
  - Each new shell's drawer toggle has an `aria-label` and is a `<button>`.
  - Reduced-motion: `styles.css` has `@media (prefers-reduced-motion: reduce)` rules for `.lumis-bg-ray` (Task 2) **and** `.totem-magic-fade` (existing) — assert both.
  - No status is conveyed by colour alone: room status / visit status / handoff status render a text label next to any colour dot (assert the label text nodes exist in the respective component tests).
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** the media queries + the missing `aria-*` / labels.
- [ ] **Step 4: Run full vitest + `tsc -b` + `vite build` + commit**

```bash
git add src/styles.css src/a11y-responsive.test.tsx src/pages src/components
git commit -m "feat(ui): responsive KPI grids, scrollable tables, a11y sweep (aria-live, alt, labels)"
```

---

# GROUP J — Final verification

### Task 28: full gate run + stragglers

- [ ] **Step 1: Backend**

Run: `dotnet test tests/GestaoPredio.UnitTests` then `dotnet test tests/GestaoPredio.IntegrationTests`
Expected: all green. Investigate any failure at its root (no skips).

- [ ] **Step 2: Frontend**

Run (from `recepcaototem/ClientApp`): `npx vitest run` then `npx tsc -b` then `npx vite build` then `npm run --silent verify:production-bundle`
Expected: all green; bundle verification passes.

- [ ] **Step 3: Tree hygiene**

Run: `git diff --check` (no whitespace errors). Confirm:
- `git status` clean.
- The new migration exists under `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/` and has **not** been applied anywhere but the local test schema.
- No real secret committed; no `appsettings` change; CSP string in `Program.cs` unchanged; `main` untouched; nothing pushed.

- [ ] **Step 4: Commit any straggler fixes**

```bash
git add -A
git commit -m "chore: final gate fixes for the LUMIS UX round"
```

- [ ] **Step 5: Hand off**

Do NOT run `superpowers:finishing-a-development-branch` and do NOT push. Report to the user: commit range, gate results, migration status (created, not applied remotely), and the endpoint/route summary. Stop.

---

## Self-Review

**Spec coverage:** §2 tokens → Task 1. §3 background → Tasks 2–3. §4 compositing → Task 4. §5 check-in layout → Tasks 6–7. §6 carousel → untouched (asserted green in Tasks 4/7). §7–§14 handoff → Tasks 8–14 (backend) + 16–20 (frontend); §9.4 claim → Task 12; §9.6 atomic + idempotent retry → Task 14; §9.7 rate limiter → Tasks 10–11; §7.5/§11 refresh → Task 18; §13 expiration → Tasks 8/11/12. §15 phone continuation → Task 19. §16 professional dashboard → Tasks 15/23/24. §17 customer dashboard → Tasks 21–22. §18 admin dashboard → Tasks 15/25/26. §19 endpoints → Tasks 11–16/25. §20 responsive → Task 27 + per-task. §21 a11y → Task 27 + per-task. §22 tests → every task is TDD. §23 migration → Task 9 (created, not applied). §24 operational (rate-limit keys) → Task 10. §25 out-of-scope → honoured (no forgot-password, no remember-me, no new customer routes, placeholders stay). §26 login → Task 5 + Task 19 (claim). §27 threat model → Tasks 11/12/14 (generic errors, no logging, distinct tokens) + Task 19 (URL cleanup).

**Placeholder scan:** every code step has real code or a precise diff instruction. The dashboard/shell tasks give representative JSX + the exact assertion rather than the full file — acceptable because the data sources and card contracts are fully specified in the spec and the interfaces block.

**Type consistency:** `HandoffStatusDto` union (Task 16) matches the backend `status`/`cancel` payloads (Task 11). `state` passed by `TotemProfessionals` (Task 17) = `{ handoffId, handoffToken, statusToken, professionalId, professionalName, profession, expiresAt }` — consumed verbatim by `TotemHandoff` (Task 18) which needs `handoffToken` (QR), `statusToken` (poll), `professionalId` (regenerate). `DashboardCounts` gains `TodayCheckIns` (Task 15) → `DashboardCountsDto.todayCheckIns` (Task 25) → KPI (Task 26). `CustomerReservationRequest.HandoffToken` (Task 14) ↔ `createReservation({ handoffToken })` (Task 16) ↔ `CustomerBooking` (Task 19).

**Ordering / dependencies:** A (1–4) is foundational and first. D (8–15) is backend, independent of frontend, and precedes E. E (16–20) needs D + A. B (5) needs A. C (6–7) needs A. F/G/H need A (+ D15/D25 for the dashboards that use new aggregates). I (27) after all screens exist. J (28) last. Within D: 8→9→10→11→12→13→14 (14 needs 11's `TryDecodeHash` + 13's patterns), 15 independent (can slot anywhere after 9). Task 10's rate-limit test is completed in Task 11's run (noted in Task 10 Step 5).

**Backend vs frontend-only:** backend-touching = Tasks 8, 9, 10, 11, 12, 13, 14, 15. Frontend-only = Tasks 1, 2, 3, 4, 5, 6, 7, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27. (Task 7 also edits CSS only; Task 16/25 are TS client + types, no server.)
