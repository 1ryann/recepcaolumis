import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { expect, test } from 'vitest'

// Regression guard: the `.totem-back` ("← Voltar") control is used on both
// /totem/profissionais (inside .totem-professionals) and /totem/check-in
// (inside .totem-kiosk). It must resolve its colours from a token set that
// exists in *both* contexts — i.e. `--tk-*` with a literal fallback — so it
// never renders unstyled on /totem/check-in. Read the stylesheet as text
// (vitest does not transform CSS, so `?raw` yields nothing here).
const styles = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
const backRule = styles.match(/\.totem-back\s*\{[^}]*\}/)?.[0] ?? ''
const backHover = styles.match(/\.totem-back:hover\s*\{[^}]*\}/)?.[0] ?? ''

test('.totem-back resting colour has a literal fallback and is not a bare --tp-* var', () => {
  expect(backRule).not.toBe('')
  expect(backRule).toMatch(/color:\s*var\(--tk-[a-z-]+,\s*#[0-9a-fA-F]{3,8}\)/)
  expect(backRule).not.toMatch(/color:\s*var\(--tp-[a-z-]+\)\s*;/)
})

test('.totem-back:hover colour also has a literal fallback (hover as enhancement)', () => {
  expect(backHover).not.toBe('')
  expect(backHover).toMatch(/color:\s*var\(--tk-[a-z-]+,\s*#[0-9a-fA-F]{3,8}\)/)
})

test('.totem-back keeps an adequate touch target and a visible focus ring', () => {
  expect(backRule).toMatch(/min-height:\s*44px/)
  expect(styles).toMatch(/\.totem-back:focus-visible\s*\{[^}]*outline:[^}]*\}/)
})
