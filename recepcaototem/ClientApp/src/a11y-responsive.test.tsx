import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { expect, test } from 'vitest'

// Task 27 — cross-cutting responsive + accessibility sweep over everything built
// in the LUMIS UX round (design system, login, Totem check-in, handoff, and the
// three dashboards). Most per-screen behaviour (aria-live countdown, QR alt text,
// drawer aria-labels, status text labels) is already asserted in the relevant
// page/component test files; this file covers the styles.css-level checks that
// don't have a natural home elsewhere: KPI grid stepping, table-scroll
// containment, reduced-motion coverage and the touch-target fixes made here.

const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')

// Extracts a top-level `@media <condition> { ... }` block by brace counting,
// so it works regardless of how many rules live inside (unlike a plain
// non-greedy regex, which breaks as soon as the block contains a `}`).
function mediaBlock(source: string, condition: string): string {
  const start = source.indexOf(`@media ${condition}`)
  expect(start, `expected to find "@media ${condition}" in styles.css`).toBeGreaterThan(-1)
  const braceStart = source.indexOf('{', start)
  let depth = 1
  let i = braceStart + 1
  while (depth > 0 && i < source.length) {
    if (source[i] === '{') depth++
    else if (source[i] === '}') depth--
    i++
  }
  return source.slice(start, i)
}

test('KPI grids (.metrics-grid, .professional-kpi-grid) step to 2-up at <=1024px', () => {
  const block = mediaBlock(css, '(max-width: 1024px)')
  expect(block).toMatch(/\.metrics-grid[^{]*\{[^}]*grid-template-columns:\s*repeat\(2/)
  expect(block).toMatch(/\.professional-kpi-grid[^{]*\{[^}]*grid-template-columns:\s*repeat\(2/)
})

test('KPI grids (.metrics-grid, .professional-kpi-grid) step to 1-up at <=640px, never 4-up on mobile', () => {
  const block = mediaBlock(css, '(max-width: 640px)')
  expect(block).toMatch(/\.metrics-grid[^{]*\{[^}]*grid-template-columns:\s*1fr/)
  expect(block).toMatch(/\.professional-kpi-grid[^{]*\{[^}]*grid-template-columns:\s*1fr/)
})

test('dashboard table wrapper (.table-scroll) scrolls horizontally instead of squashing', () => {
  expect(css).toMatch(/\.table-scroll\s*\{[^}]*overflow-x:\s*auto/)
})

test('reduced-motion disables the continuous .lumis-bg-ray drift (Task 2)', () => {
  expect(css).toMatch(/@media \(prefers-reduced-motion: reduce\) \{\s*\.lumis-bg-ray \{[^}]*animation:\s*none/)
})

test('reduced-motion disables the continuous .totem-magic-fade transition (Task 4)', () => {
  // Walk the prefers-reduced-motion media block rule-by-rule (no unbounded [\s\S]*)
  // to isolate the .totem-magic-fade declaration body within it.
  expect(css).toMatch(
    /@media \(prefers-reduced-motion: reduce\) \{(?:[^{}]*\{[^{}]*\})*?\s*\.totem-magic-fade\s*\{[^}]*transition:\s*none/,
  )
})

test('drawer/menu toggle buttons meet the 44px minimum touch target', () => {
  expect(css).toMatch(/\.menu-trigger\s*\{[^}]*min-width:\s*44px[^}]*min-height:\s*44px/)
})

test('login password show/hide toggle meets the 44px minimum touch target', () => {
  expect(css).toMatch(/\.lumis-password-toggle\s*\{[^}]*min-height:\s*44px/)
})

test('customer "copiar" QR button meets the 44px minimum touch target', () => {
  expect(css).toMatch(/\.customer-qr-generated \.secondary-button\s*\{[^}]*min-height:\s*44px/)
})
