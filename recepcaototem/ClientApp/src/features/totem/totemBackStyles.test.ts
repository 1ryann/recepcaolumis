import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { expect, test } from 'vitest'

// Regression guard: the `.totem-back` ("← Voltar") control is used on /totem/check-in
// (inside .totem-kiosk) and by TotemHandoff. /totem/profissionais dropped its own back
// button in favour of the LUMIS logo doubling as the way home, but the class itself is
// still shared, so it must keep resolving its colours from tokens that exist wherever it
// is used — the global --lumis-* tokens declared once on :root — so it never renders
// unstyled. Read the stylesheet as text (vitest does not transform CSS, so `?raw` yields
// nothing here).
const styles = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
const backRule = styles.match(/\.totem-back\s*\{[^}]*\}/)?.[0] ?? ''
const backHover = styles.match(/\.totem-back:hover\s*\{[^}]*\}/)?.[0] ?? ''

test('.totem-back resting colour comes from a global --lumis-* token, not a page-local alias', () => {
  expect(backRule).not.toBe('')
  expect(backRule).toMatch(/color:\s*var\(--lumis-muted\)/)
  expect(backRule).not.toMatch(/color:\s*var\(--t[kpce]-[a-z-]+\)/)
})

test('.totem-back:hover colour also comes from a global --lumis-* token', () => {
  expect(backHover).not.toBe('')
  expect(backHover).toMatch(/color:\s*var\(--lumis-text-dim\)/)
})

test('.totem-back keeps an adequate touch target and a visible focus ring', () => {
  expect(backRule).toMatch(/min-height:\s*44px/)
  expect(styles).toMatch(/\.totem-back:focus-visible\s*\{[^}]*outline:[^}]*\}/)
})
