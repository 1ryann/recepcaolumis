// Dark-mode parity checker: compares, per selector, the last-declared value of colour-bearing
// properties in HEAD's styles.css vs the working tree, resolving var(--lumis-*) against the
// current :root[data-theme="dark"] block. Usage (from recepcaototem/ClientApp):
//   node ../../.superpowers/dark-parity.mjs [selectorRegex]
// Default regex covers the Admin scope. Exit code 1 if any divergence is found.
import { execSync } from 'node:child_process'
import { readFileSync } from 'node:fs'

// Default: selectors whose dark look was consolidated in HEAD (Admin shell + scoped overrides).
// Unscoped base rules (.panel, .data-table…) held LIGHT values in HEAD, so they are not dark references.
const scope = new RegExp(process.argv[2] ?? '^\\.(admin-content|admin-dashboard|admin-shell|admin-main|admin-topbar|admin-nav|sidebar|profile-chip|nav-label|topbar-title|menu-trigger|mobile-close)\\b')
const PROPS = ['color', 'background', 'background-color', 'border-color', 'border', 'border-top-color', 'border-bottom-color', 'box-shadow', 'fill', 'stroke', 'outline-color']

const head = execSync('git show HEAD:recepcaototem/ClientApp/src/styles.css', { encoding: 'utf8', maxBuffer: 64e6 })
const cur = readFileSync('src/styles.css', 'utf8')
const strip = (t) => t.replace(/\/\*[\s\S]*?\*\//g, '')

const darkTokens = {}
const db = strip(cur).match(/:root\[data-theme="dark"\]\s*\{([^}]*)\}/)
for (const m of db[1].matchAll(/--([a-z0-9-]+)\s*:\s*([^;]+);/g)) darkTokens[m[1]] = m[2].trim()
const headTokens = {}
for (const m of strip(head).match(/:root\s*\{([^}]*)\}/)[1].matchAll(/--([a-z0-9-]+)\s*:\s*([^;]+);/g)) headTokens[m[1]] = m[2].trim()
const resolveWith = (T, v) => { let g = 0; while (/var\(--/.test(v) && g++ < 6) v = v.replace(/var\(--([a-z0-9-]+)(?:,\s*([^)]*))?\)/g, (s, n, fb) => T[n] ?? fb ?? s); return v }
const resolve = (v) => { let g = 0; while (/var\(--/.test(v) && g++ < 6) v = v.replace(/var\(--([a-z0-9-]+)(?:,\s*([^)]*))?\)/g, (s, n, fb) => darkTokens[n] ?? fb ?? s); return v }
const norm = (v) => v.toLowerCase().replace(/\s+/g, ' ').replace(/,\s*/g, ',').replace(/\(\s*/g, '(').replace(/\s*\)/g, ')')
  .replace(/#([0-9a-f])([0-9a-f])([0-9a-f])\b/g, '#$1$1$2$2$3$3').replace(/\bwhite\b/g, '#ffffff').replace(/\bblack\b/g, '#000000')
  // Etapa 3: color-mix(in srgb, #rrggbb P%, transparent) is exactly rgba(r,g,b,P/100) (transparent adds
  // no hue under premultiplied interpolation), so normalise that one form before the rgba() step.
  .replace(/color-mix\(in srgb,#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2}) (\d+(?:\.\d+)?)%,transparent\)/g, (s, r, g, b, p) => `rgba(${parseInt(r, 16)},${parseInt(g, 16)},${parseInt(b, 16)},${+p / 100})`)
  .replace(/rgba?\((\d+),(\d+),(\d+)(?:,(0?\.\d+|1|0))?\)/g, (s, r, g, b, a) => a === undefined || a === '1' ? '#' + [r, g, b].map((x) => (+x).toString(16).padStart(2, '0')).join('') : `rgba(${r},${g},${b},${String(+a)})`)

// Only rules that apply in dark: unconditional, or guarded for dark. Skip light-only guards.
const parse = (t) => {
  t = strip(t)
  const map = new Map()
  // flatten @media (prefers-color-scheme: dark) wrappers, drop other @media (responsive) blocks' guards but keep their rules
  for (const r of t.matchAll(/([^{}]*?)\{([^{}]*)\}/g)) {
    let sel = r[1].replace(/^[\s\S]*;/, '').replace(/@media[^{]*$/, '').trim()
    if (!sel) continue
    for (let s of sel.split(',')) {
      s = s.trim().replace(/\s+/g, ' ')
      if (/data-theme="light"\]\s*(?!\))/.test(s) && !/:not\(\[data-theme="light"\]\)/.test(s)) continue // light-only
      s = s.replace(/^:root(\[data-theme="dark"\]|:not\(\[data-theme="light"\]\))\s+/, '')
      if (!scope.test(s)) continue
      const decl = map.get(s) ?? {}
      for (const d of r[2].split(';')) {
        const i = d.indexOf(':'); if (i < 0) continue
        const p = d.slice(0, i).trim().toLowerCase(); if (!PROPS.includes(p)) continue
        // A later shorthand resets the longhands it covers (e.g. border after border-color), so drop them.
        if (p === 'border') for (const k of ['border-color', 'border-top-color', 'border-bottom-color']) delete decl[k]
        if (p === 'background') delete decl['background-color']
        decl[p] = d.slice(i + 1).replace(/!important/, '').trim()
      }
      map.set(s, decl)
    }
  }
  return map
}
const H = parse(head), C = parse(cur)
const diffs = []
// Etapa 3: an UNSCOPED base selector (e.g. ".customer-slot") that HEAD also overrides through an
// area-scoped rule (".customer-content .customer-slot") held LIGHT values in HEAD, like the
// .panel/.data-table base rules the default scope already excludes. The scoped override is still
// compared normally, and only (selector, property) pairs whose colour that HEAD scoped rule itself
// redeclares are skipped. Every skip is printed — nothing is hidden silently.
const SCOPES = ['admin-content', 'customer-content', 'professional-content']
const covers = (d, p) => p === 'border' ? ('border' in d || 'border-color' in d) : p === 'background' ? ('background' in d || 'background-color' in d) : p in d
const skipped = []
for (const [sel, hd] of H) {
  const cd = C.get(sel) ?? {}
  for (const [p, hv] of Object.entries(hd)) {
    if (!/#|rgb|white|black/i.test(hv)) continue
    if (/^\.[a-z0-9_-]+$/i.test(sel)) {
      const scoped = SCOPES.map((sc) => H.get(`.${sc} ${sel}`)).find((d) => d && covers(d, p))
      if (scoped) { skipped.push(`${sel} | ${p} (HEAD scoped override redeclares it)`); continue }
    }
    const cv = cd[p]
    if (cv === undefined) { diffs.push({ sel, p, head: hv, now: '(removed)' }); continue }
    if (norm(resolve(cv)) !== norm(resolveWith(headTokens, hv))) diffs.push({ sel, p, head: resolveWith(headTokens, hv), now: `${cv} => ${resolve(cv)}` })
  }
}
for (const k of skipped) console.log(`skipped base: ${k}`)
for (const d of diffs) console.log(`${d.sel} | ${d.p} | HEAD: ${d.head} | NOW: ${d.now}`)
console.log(`\n${diffs.length} divergence(s) in dark mode vs HEAD`)
process.exit(diffs.length ? 1 : 0)
