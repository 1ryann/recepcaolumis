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
