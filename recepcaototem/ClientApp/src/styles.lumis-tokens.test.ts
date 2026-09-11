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

test('page shells reference --lumis-* tokens directly (no redundant per-page alias layer)', () => {
  // The Home portal and every /totem/* shell used to redeclare their own --home-*/--tk-*/
  // --te-*/--tp-*/--tc-* aliases that only ever pointed back at --lumis-*. That indirection
  // layer is gone: .home-portal, .totem-kiosk, .totem-entry, .totem-professionals and
  // .totem-carousel consume var(--lumis-*) directly, so there is exactly one place — :root —
  // that defines the LUMIS palette.
  expect(css).not.toMatch(/--(?:tk|te|tp|tc|home)-[a-z-]+:\s*var\(--lumis-/)
  expect(css).toMatch(/\.totem-kiosk\s*\{[^}]*background:\s*var\(--lumis-bg\)/)
})
