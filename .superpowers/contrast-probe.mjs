// Static light/dark contrast probe for specific selectors. Resolves tokens per theme and
// composites translucent colours over a given backdrop. Usage (from recepcaototem/ClientApp):
//   node ../../.superpowers/contrast-probe.mjs
import { readFileSync } from 'node:fs'
const t = readFileSync('src/styles.css', 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

const tokens = (re) => { const o = {}; for (const m of t.match(re)[1].matchAll(/--([a-z0-9-]+)\s*:\s*([^;]+);/g)) o[m[1]] = m[2].trim(); return o }
const THEMES = { light: tokens(/:root\s*\{([^}]*)\}/), dark: tokens(/:root\[data-theme="dark"\]\s*\{([^}]*)\}/) }
const resolve = (v, T) => { let g = 0; while (v && /var\(--/.test(v) && g++ < 6) v = v.replace(/var\(--([a-z0-9-]+)(?:,\s*([^)]*))?\)/g, (s, n, fb) => T[n] ?? fb ?? s); return v }

// Last declaration per selector, skipping rules guarded for the other theme.
const rules = []
for (const r of t.matchAll(/([^{}]*?)\{([^{}]*)\}/g)) {
  const sel = r[1].replace(/^[\s\S]*;/, '').replace(/@media[^{]*$/, '').trim().replace(/\s+/g, ' ')
  if (sel) rules.push({ sels: sel.split(',').map((s) => s.trim()), body: r[2] })
}
const prop = (selector, p, theme) => {
  let v = null
  for (const r of rules) for (let s of r.sels) {
    const darkOnly = /^:root(\[data-theme="dark"\]|:not\(\[data-theme="light"\]\))\s+/.test(s)
    const lightOnly = /^:root\[data-theme="light"\]\s+/.test(s)
    if ((darkOnly && theme !== 'dark') || (lightOnly && theme !== 'light')) continue
    s = s.replace(/^:root(\[data-theme="(dark|light)"\]|:not\(\[data-theme="light"\]\))\s+/, '')
    if (s !== selector) continue
    for (const d of r.body.split(';')) { const i = d.indexOf(':'); if (i > 0 && d.slice(0, i).trim() === p) v = d.slice(i + 1).replace(/!important/, '').trim() }
  }
  return v === null ? null : resolve(v, THEMES[theme])
}
const rgb = (c, over) => {
  if (!c) return null
  let m = c.match(/#([0-9a-f]{6})\b/i); if (m) return [0, 2, 4].map((i) => parseInt(m[1].slice(i, i + 2), 16))
  m = c.match(/#([0-9a-f]{3})\b/i); if (m) return [...m[1]].map((x) => parseInt(x + x, 16))
  m = c.match(/rgba?\(([^)]+)\)/); if (m) { const p = m[1].split(',').map(Number); const a = p[3] ?? 1; return [0, 1, 2].map((i) => Math.round(p[i] * a + over[i] * (1 - a))) }
  if (/white/.test(c)) return [255, 255, 255]
  return null
}
const lum = (r) => { const [a, b, c] = r.map((v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4 }); return 0.2126 * a + 0.7152 * b + 0.0722 * c }
const cr = (a, b) => { const x = lum(a), y = lum(b); return ((Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05)).toFixed(2) }

export { prop, rgb, cr, resolve, THEMES }

if (import.meta.url.endsWith(process.argv[1].replace(/\\/g, '/').split('/').pop())) {
  const cases = [
    ['input', '.admin-content .field-input', '.admin-content .field-input'],
    ['input:focus', '.admin-content .field-input:focus', '.admin-content .field-input'],
    ['search', '.admin-content .search-field', '.admin-content .search-field'],
    ['search:focus', '.admin-content .search-field:focus-within', '.admin-content .search-field:focus-within'],
    ['time-input', '.admin-content .time-input', '.admin-content .time-input'],
    ['timefield-open', '.admin-content .timefield-open', '.admin-content .timefield-open'],
    ['timefield-open:hover', '.admin-content .timefield-open:hover:not(:disabled)', '.admin-content .timefield-open'],
  ]
  for (const theme of ['light', 'dark']) {
    const panel = rgb(resolve('var(--lumis-panel-bg)', THEMES[theme]), [255, 255, 255])
    console.log(`\n== ${theme} (painel ${panel}) ==`)
    for (const [label, bsel, bgsel] of cases) {
      const bc = prop(bsel, 'border-color', theme) ?? prop(bsel, 'border', theme)
      const bgv = prop(bgsel, 'background', theme)
      const bg = rgb(bgv, panel) ?? panel
      const b = rgb(bc, bg)
      console.log(label.padEnd(22), 'borda', String(bc).padEnd(26), '| campo', String(bgv).padEnd(28), '| vs campo', b ? cr(b, bg) : '?', '| vs painel', b ? cr(b, panel) : '?')
    }
  }
}
