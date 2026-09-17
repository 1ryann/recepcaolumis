import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { expect, test } from 'vitest'

const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')

test(':root declares the full LUMIS token set with LIGHT as the default value set', () => {
  // Light is the default so it's visible with no media query and no [data-theme] attribute at
  // all — the bare, unconditional :root block (not the dark override blocks further down).
  const root = css.match(/:root\s*\{[^}]*\}/)?.[0] ?? ''
  expect(root).toMatch(/--lumis-bg:\s*#f2f2f2/)
  expect(root).toMatch(/--lumis-surface:\s*#[Ff]{6}/)
  expect(root).toMatch(/--lumis-surface-2:\s*#[Ff]{6}/)
  expect(root).toMatch(/--lumis-surface-3:\s*#f6f6f6/)
  expect(root).toMatch(/--lumis-border:\s*#dedede/)
  expect(root).toMatch(/--lumis-text:\s*#2b2b2b/)
  // Dedicated Totem screen-title heading token (Etapa 1.5d, Fix 2) — light matches
  // --lumis-text (full emphasis); dark (asserted below) is byte-identical to the dark
  // --lumis-text-dim value titles already rendered, so dark mode is unchanged.
  expect(root).toMatch(/--lumis-heading:\s*#2b2b2b/)
  expect(root).toMatch(/--lumis-text-secondary:\s*#6b6b6b/)
  expect(root).toMatch(/--lumis-action-muted:\s*#555555/)
  // Etapa 2c: #767676 fell to ~4.05:1 on the #f2f2f2 page background (Admin page-header copy,
  // profile chip); #6b6b6b clears 4.5:1 there. Dark (#888888) is unchanged.
  expect(root).toMatch(/--lumis-muted:\s*#6b6b6b/)
  expect(root).toMatch(/--lumis-success:/)
  expect(root).toMatch(/--lumis-warning:/)
  expect(root).toMatch(/--lumis-danger:/)
  // The newer tokens added alongside the original set (Finding 4, Fix round 1) — asserted here
  // too so a future edit can't silently drop one without failing this test.
  expect(root).toMatch(/--lumis-bg-secondary:\s*#e8e8e8/)
  expect(root).toMatch(/--lumis-primary:\s*#3d3d3d/)
  expect(root).toMatch(/--lumis-primary-hover:\s*#262626/)
  expect(root).toMatch(/--lumis-input-bg:\s*#[Ff]{6}/)
  // Light field border must reach 3:1 against the field fill and the panel (#d2d2d2/#dedede
  // sat at ~1.35–1.5:1). Focus must be clearly stronger than rest.
  expect(root).toMatch(/--lumis-input-border:\s*#8a8a8a/)
  expect(root).toMatch(/--lumis-input-border-focus:\s*#3d3d3d/)
  expect(root).toMatch(/--lumis-focus-ring:\s*rgba\(56,\s*111,\s*158,\s*\.45\)/)
  expect(root).toMatch(/--lumis-overlay:\s*rgba\(43,\s*43,\s*43,\s*\.4\)/)

  // Admin sidebar tokens (Etapa 1.5a) — the sidebar was the last hard-coded-dark exception left
  // over from Etapa 1 ("WHITE MODE COMPLETO, SEM EXCEÇÕES"). Light values here are a deliberate
  // distinct-surface design, not a plain white block.
  expect(root).toMatch(/--lumis-sidebar-bg:\s*#[Ff]{6}/)
  expect(root).toMatch(/--lumis-sidebar-bg-2:\s*#f7f7f7/)
  expect(root).toMatch(/--lumis-sidebar-bg-3:\s*#fbfbfb/)
  expect(root).toMatch(/--lumis-sidebar-text:\s*#2b2b2b/)
  expect(root).toMatch(/--lumis-sidebar-muted:\s*#6b6b6b/)
  expect(root).toMatch(/--lumis-sidebar-chip-bg:\s*#2f2f2f/)
  expect(root).toMatch(/--lumis-sidebar-chip-text:\s*#[Ff]{6}/)

  // Public Home/Totem tokens (Etapa 1.5b) — back the previously dark-only .home-*/.totem-*
  // rules. Light defaults here; dark values (byte-for-byte the pre-existing literals) are
  // asserted in the dark-block test below.
  expect(root).toMatch(/--lumis-ink-rgb:\s*61,\s*61,\s*61/)
  expect(root).toMatch(/--lumis-kiosk-accent:\s*var\(--lumis-primary\)/)
  expect(root).toMatch(/--lumis-kiosk-accent-hover:\s*var\(--lumis-primary-hover\)/)
  expect(root).toMatch(/--lumis-alert-text:\s*#8a3232/)
  expect(root).toMatch(/--lumis-danger-rgb:\s*193,\s*79,\s*79/)
  // Success/warning RGB channels (Etapa 2a) — back the Cliente reservation-status badge tints.
  expect(root).toMatch(/--lumis-success-rgb:\s*47,\s*143,\s*111/)
  expect(root).toMatch(/--lumis-warning-rgb:\s*163,\s*112,\s*31/)
  expect(root).toMatch(/--lumis-warn-strong:\s*#8a6015/)
  // Etapa 3: deepened to the -ink values so 12–14px Totem chip text clears 4.5:1 on white
  // (was #2f8f6f 3.98:1 / #a3701f 4.29:1). Dark values below are unchanged.
  expect(root).toMatch(/--lumis-status-ok:\s*#22674f/)
  expect(root).toMatch(/--lumis-status-busy:\s*#7d5516/)
  expect(root).toMatch(/--lumis-status-muted:\s*#6b6b6b/)
  expect(root).toMatch(/--lumis-dot-hover:\s*#6b6b6b/)
  expect(root).toMatch(/--lumis-code-cell-bg:\s*#ececec/)
  expect(root).toMatch(/--lumis-color-scheme:\s*light/)
})

test('a dark override block exists, reachable via :root[data-theme="dark"], with the original dark values', () => {
  // These are the same hex values the token set used to carry directly in the bare :root block
  // before the light/dark split — moving them here must not have lost or altered them.
  const darkBlock = css.match(/:root\[data-theme="dark"\]\s*\{[^}]*\}/)?.[0] ?? ''
  expect(darkBlock).not.toBe('')
  expect(darkBlock).toMatch(/--lumis-bg:\s*#181818/)
  expect(darkBlock).toMatch(/--lumis-surface:\s*#1[Cc]1[Cc]1[Cc]/)
  expect(darkBlock).toMatch(/--lumis-surface-2:\s*#222222/)
  expect(darkBlock).toMatch(/--lumis-surface-3:\s*#272727/)
  expect(darkBlock).toMatch(/--lumis-border:\s*#3[Dd]3[Dd]3[Dd]/)
  expect(darkBlock).toMatch(/--lumis-text:\s*#[Ff]{6}/)
  // Etapa 1.5d, Fix 2 — dark value must stay byte-identical to --lumis-text-dim's dark value.
  expect(darkBlock).toMatch(/--lumis-heading:\s*#f2f2f2/)
  expect(darkBlock).toMatch(/--lumis-text-secondary:\s*#9a9a9a/)
  expect(darkBlock).toMatch(/--lumis-action-muted:\s*#b8b8b8/)
  expect(darkBlock).toMatch(/--lumis-muted:\s*#888888/)
  expect(darkBlock).toMatch(/--lumis-success:/)
  expect(darkBlock).toMatch(/--lumis-warning:/)
  expect(darkBlock).toMatch(/--lumis-danger:/)
  // The newer tokens added alongside the original set (Finding 4, Fix round 1) — asserted here
  // too so a future edit can't silently drop one without failing this test.
  expect(darkBlock).toMatch(/--lumis-bg-secondary:\s*#1[Cc]1[Cc]1[Cc]/)
  expect(darkBlock).toMatch(/--lumis-primary:\s*#[Ff]{6}/)
  expect(darkBlock).toMatch(/--lumis-primary-hover:\s*#e0e0e0/)
  expect(darkBlock).toMatch(/--lumis-input-bg:\s*#222222/)
  expect(darkBlock).toMatch(/--lumis-input-border:\s*#3[Dd]3[Dd]3[Dd]/)
  // Dark focus border stays HEAD's rgba(255,255,255,.22) (was var(--lumis-border-strong)).
  expect(darkBlock).toMatch(/--lumis-input-border-focus:\s*rgba\(255,\s*255,\s*255,\s*\.22\)/)
  expect(darkBlock).toMatch(/--lumis-focus-ring:\s*rgba\(91,\s*155,\s*213,\s*\.5\)/)
  expect(darkBlock).toMatch(/--lumis-overlay:\s*rgba\(7,\s*27,\s*33,\s*\.48\)/)

  // Admin sidebar tokens (Etapa 1.5a) — the dark values must be byte-for-byte what the "LUMIS
  // admin refinement" .sidebar rules rendered before the token pass (styles.css, ~line 851).
  expect(darkBlock).toMatch(/--lumis-sidebar-bg:\s*rgba\(25,\s*25,\s*25,\s*\.98\)/)
  expect(darkBlock).toMatch(/--lumis-sidebar-bg-2:\s*rgba\(47,\s*47,\s*47,\s*\.97\)/)
  expect(darkBlock).toMatch(/--lumis-sidebar-bg-3:\s*rgba\(28,\s*28,\s*28,\s*\.99\)/)
  expect(darkBlock).toMatch(/--lumis-sidebar-text:\s*#f2f2f2/)
  expect(darkBlock).toMatch(/--lumis-sidebar-muted:\s*#a6a6a6/)
  expect(darkBlock).toMatch(/--lumis-sidebar-chip-bg:\s*#f2f2f2/)
  expect(darkBlock).toMatch(/--lumis-sidebar-chip-text:\s*#3[Dd]3[Dd]3[Dd]/)

  // Public Home/Totem tokens (Etapa 1.5b) — dark values must be byte-for-byte the literals
  // the .home-*/.totem-* rules rendered before this token pass, so dark mode is unchanged.
  expect(darkBlock).toMatch(/--lumis-ink-rgb:\s*255,\s*255,\s*255/)
  expect(darkBlock).toMatch(/--lumis-kiosk-accent:\s*#f2f2f2/)
  expect(darkBlock).toMatch(/--lumis-kiosk-accent-hover:\s*#[Ff]{6}/)
  expect(darkBlock).toMatch(/--lumis-alert-text:\s*#f0b8b8/)
  expect(darkBlock).toMatch(/--lumis-danger-rgb:\s*226,\s*120,\s*120/)
  expect(darkBlock).toMatch(/--lumis-success-rgb:\s*63,\s*185,\s*140/)
  expect(darkBlock).toMatch(/--lumis-warning-rgb:\s*224,\s*169,\s*59/)
  expect(darkBlock).toMatch(/--lumis-warn-strong:\s*#e6c07a/)
  expect(darkBlock).toMatch(/--lumis-status-ok:\s*#3E9B6B/)
  expect(darkBlock).toMatch(/--lumis-status-busy:\s*#C98A2B/)
  expect(darkBlock).toMatch(/--lumis-status-muted:\s*#888888/)
  expect(darkBlock).toMatch(/--lumis-dot-hover:\s*#bdbdbd/)
  expect(darkBlock).toMatch(/--lumis-code-cell-bg:\s*#141414/)
  expect(darkBlock).toMatch(/--lumis-color-scheme:\s*dark/)

  // A matching @media (prefers-color-scheme: dark) block carries the same dark values, so an OS
  // preference is honored automatically until the user makes an explicit manual choice.
  const mediaBlockMatch = css.match(/@media \(prefers-color-scheme: dark\)\s*\{\s*:root:not\(\[data-theme="light"\]\)\s*\{[^}]*\}/)
  expect(mediaBlockMatch).not.toBeNull()
  expect(mediaBlockMatch?.[0]).toMatch(/--lumis-bg:\s*#181818/)
  expect(mediaBlockMatch?.[0]).toMatch(/--lumis-sidebar-bg:\s*rgba\(25,\s*25,\s*25,\s*\.98\)/)
  expect(mediaBlockMatch?.[0]).toMatch(/--lumis-kiosk-accent:\s*#f2f2f2/)
  // Etapa 1.5d, Fix 2 — the OS-preference dark block must carry the same heading value too.
  expect(mediaBlockMatch?.[0]).toMatch(/--lumis-heading:\s*#f2f2f2/)
  expect(mediaBlockMatch?.[0]).toMatch(/--lumis-text-secondary:\s*#9a9a9a/)
  expect(mediaBlockMatch?.[0]).toMatch(/--lumis-action-muted:\s*#b8b8b8/)
})

test('the "LUMIS admin refinement" .sidebar rule consumes --lumis-sidebar-* tokens instead of hard-coded colors, so dark mode is no longer a permanent exception', () => {
  // There are several older, losing `.sidebar { ... }` layers earlier in the cascade (an "Admin
  // shell" layer and a "RYNEX"/"LUMIS official monochrome" layer, both intentionally left in
  // place) — this test targets the winning "LUMIS admin refinement" one, identified by its
  // `isolation: isolate` declaration which only that rule has.
  const sidebarRule = css.match(/\.sidebar \{\s*isolation: isolate;[^}]*\}/)?.[0] ?? ''
  expect(sidebarRule).not.toBe('')
  expect(sidebarRule).toMatch(/background:\s*\n?\s*linear-gradient\(148deg, var\(--lumis-sidebar-bg\) 0%, var\(--lumis-sidebar-bg-2\) 52%, var\(--lumis-sidebar-bg-3\) 100%\)/)
  expect(sidebarRule).toMatch(/border-right:\s*1px solid var\(--lumis-sidebar-border\)/)
  expect(css).toMatch(/\.admin-nav a\.active \{[^}]*background:\s*var\(--lumis-sidebar-chip-bg\)/)
  // filter: invert() is explicitly forbidden anywhere in the sidebar's theme handling.
  expect(css).not.toMatch(/\.sidebar[^{]*\{[^}]*invert\(/)
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

test('Admin content tokens (Etapa 2c) carry a considered LIGHT default and HEAD-exact DARK values in both dark blocks', () => {
  const root = css.match(/:root\s*\{[^}]*\}/)?.[0] ?? ''
  const darkBlock = css.match(/:root\[data-theme="dark"\]\s*\{[^}]*\}/)?.[0] ?? ''
  const mediaBlock = css.match(/@media \(prefers-color-scheme: dark\)\s*\{\s*:root:not\(\[data-theme="light"\]\)\s*\{[^}]*\}/)?.[0] ?? ''
  const light: Array<[string, RegExp]> = [
    ['panel-bg', /#ffffff/], ['success-ink', /#22674f/], ['warning-ink', /#7d5516/], ['danger-ink', /#a33d3d/], ['surface-hover', /#f6f6f6/], ['text-soft', /#4a4a4a/], ['text-subtle', /#6b6b6b/],
    ['success-muted', /#3d7a66/], ['success-text', /#24694f/],
    ['focus-halo', /rgba\(56,\s*111,\s*158,\s*\.45\)/], ['focus-halo-soft', /rgba\(56,\s*111,\s*158,\s*\.45\)/],
    ['shadow-card', /0 10px 26px rgba\(61,\s*61,\s*61,\s*\.06\)/], ['shadow-card-hover', /0 14px 32px rgba\(61,\s*61,\s*61,\s*\.10\)/],
    ['shadow-panel', /0 14px 30px rgba\(61,\s*61,\s*61,\s*\.07\)/], ['shadow-popover', /0 18px 42px rgba\(61,\s*61,\s*61,\s*\.16\)/],
    ['shadow-modal', /0 25px 70px rgba\(43,\s*43,\s*43,\s*\.22\)/],
  ]
  const dark: Array<[string, RegExp]> = [
    ['panel-bg', /#212121/], ['success-ink', /#3fb98c/], ['warning-ink', /#e0a93b/], ['danger-ink', /#e27878/], ['surface-hover', /#292929/], ['text-soft', /#cfcfcf/], ['text-subtle', /#8a8a8a/],
    ['success-muted', /#8fb3a6/], ['success-text', /#7fd4b3/],
    ['focus-halo', /rgba\(255,\s*255,\s*255,\s*\.06\)/], ['focus-halo-soft', /rgba\(255,\s*255,\s*255,\s*\.05\)/],
    ['shadow-card', /0 10px 26px rgba\(0,\s*0,\s*0,\s*\.28\)/], ['shadow-card-hover', /0 14px 32px rgba\(0,\s*0,\s*0,\s*\.34\)/],
    ['shadow-panel', /0 14px 30px rgba\(0,\s*0,\s*0,\s*\.3\)/], ['shadow-popover', /0 18px 42px rgba\(0,\s*0,\s*0,\s*\.4\)/],
    ['shadow-modal', /0 25px 70px rgba\(0,\s*0,\s*0,\s*\.5\)/],
  ]
  const decl = (name: string, value: RegExp) => new RegExp(`--lumis-${name}:\\s*${value.source};`)
  for (const [name, value] of light) expect(root).toMatch(decl(name, value))
  for (const [name, value] of dark) {
    expect(darkBlock).toMatch(decl(name, value))
    expect(mediaBlock).toMatch(decl(name, value))
  }
  // Admin content paints its own text colour instead of inheriting body's fixed #3d3d3d.
  expect(css).toMatch(/\.admin-content \{\s*position: relative;[^}]*color: var\(--lumis-text-dim\);/)
})

test('Etapa 3 form-value/glow/field-halo tokens: considered LIGHT default, HEAD-exact DARK in both dark blocks', () => {
  const root = css.match(/:root\s*\{[^}]*\}/)?.[0] ?? ''
  const darkBlock = css.match(/:root\[data-theme="dark"\]\s*\{[^}]*\}/)?.[0] ?? ''
  const mediaBlock = css.match(/@media \(prefers-color-scheme: dark\)\s*\{\s*:root:not\(\[data-theme="light"\]\)\s*\{[^}]*\}/)?.[0] ?? ''
  // Light: typed value at full emphasis; placeholder ≥4.5:1 on the field fill but ≥2:1 lighter than the value.
  expect(root).toMatch(/--lumis-input-text:\s*#2b2b2b;/)
  expect(root).toMatch(/--lumis-input-placeholder:\s*#6e6e6e;/)
  expect(root).toMatch(/--lumis-glow-rgb:\s*61,\s*61,\s*61;/)
  expect(root).toMatch(/--lumis-focus-halo-strong:\s*rgba\(56,\s*111,\s*158,\s*\.45\);/)
  for (const block of [darkBlock, mediaBlock]) {
    // Dark: byte-for-byte what HEAD rendered (typed #f2f2f2, placeholder #767676, glow rgba(242,242,242,a), halo .08).
    expect(block).toMatch(/--lumis-input-text:\s*#f2f2f2;/)
    expect(block).toMatch(/--lumis-input-placeholder:\s*#767676;/)
    expect(block).toMatch(/--lumis-glow-rgb:\s*242,\s*242,\s*242;/)
    expect(block).toMatch(/--lumis-focus-halo-strong:\s*rgba\(255,\s*255,\s*255,\s*\.08\);/)
  }
  // Field boundaries outside Admin use the input-border tokens, not the divider --lumis-border.
  expect(css).toMatch(/\.lumis-login-card \.field-input \{[^}]*border-color: var\(--lumis-input-border\);/)
  expect(css).toMatch(/\.lumis-auth-surface \.field-input:focus \{ border-color: var\(--lumis-input-border-focus\);/)
  expect(css).toMatch(/\.professional-content \.field-input \{[^}]*border-color: var\(--lumis-input-border\);/)
  expect(css).toMatch(/\.customer-content \.field-input:focus \{ border-color: var\(--lumis-input-border-focus\);/)
})

test('Etapa 3: base text colour is theme-aware and the area containers paint their own text', () => {
  const root = css.match(/:root\s*\{[^}]*\}/)?.[0] ?? ''
  expect(root).toMatch(/\n\s*color:\s*var\(--lumis-text\);/)
  expect(css).toMatch(/\nbody \{ color: var\(--lumis-text\); background: var\(--lumis-bg\); \}/)
  expect(css).not.toMatch(/\nbody \{ color: #3d3d3d/)
  expect(css).toMatch(/\.customer-content \{[^}]*color: var\(--lumis-text-dim\);/)
  expect(css).toMatch(/\.professional-content \{[^}]*color: var\(--lumis-text-dim\);/)
  expect(css).toMatch(/\.lumis-access-denied \{[^}]*color: var\(--lumis-text\);/)
})

test('Etapa 3: light-only field fixes keep HEAD dark values behind both dark guards', () => {
  // Placeholder for fields that never declared one: token in light, Tailwind preflight default in dark.
  expect(css).toMatch(/\.lumis-auth-surface \.field-input::placeholder,[\s\S]*?\{ color: var\(--lumis-input-placeholder\); \}/)
  expect(css).toMatch(/:root:not\(\[data-theme="light"\]\) \.lumis-auth-surface \.field-input::placeholder,[\s\S]*?\{ color: revert-layer; \}/)
  expect(css).toMatch(/:root\[data-theme="dark"\] \.lumis-auth-surface \.field-input::placeholder,[\s\S]*?\{ color: revert-layer; \}/)
  // Time-input focus: field focus tokens in light, HEAD's teal base focus in dark.
  expect(css).toMatch(/:root\[data-theme="dark"\] \.professional-content \.time-input:focus,[\s\S]*?\{ border-color: #4c9b85; box-shadow: 0 0 0 3px rgba\(76,155,133,\.12\); \}/)
  // Totem six-digit cells: visible light boundary, HEAD --lumis-border-strong in dark.
  // Light rest border must reach >=3:1 against the cell's own #ececec fill (#8a8a8a was 2.92:1).
  expect(css).toMatch(/\.totem-code-cell:not\(\.is-active\) \{ border-color: var\(--lumis-muted\); \}/)
  expect(css).toMatch(/\.totem-code-cell\.is-active \{ border-color: var\(--lumis-input-border-focus\); \}/)
  expect(css).toMatch(/:root\[data-theme="dark"\] \.totem-code-cell:not\(\.is-active\) \{ border-color: var\(--lumis-border-strong\); \}/)
  expect(css).toMatch(/:root\[data-theme="dark"\] \.totem-code-cell\.is-active \{ border-color: var\(--lumis-text-dim\); \}/)
  expect(css).toMatch(/:root:not\(\[data-theme="light"\]\) \.totem-code-cell\.is-active \{ border-color: var\(--lumis-text-dim\); \}/)
})
