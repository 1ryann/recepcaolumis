// Final audit: lists light-mode colour declarations that still resolve to the fixed legacy --muted
// (#888888) or grey literals, whether a later rule with the same trailing compound overrides them,
// and which production .tsx files reference the key class. Read-only. Run from recepcaototem/ClientApp.
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'

const css = readFileSync('src/styles.css', 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')
const rules = []
for (const r of css.matchAll(/([^{}]*?)\{([^{}]*)\}/g)) {
  const sel = r[1].replace(/^[\s\S]*;/, '').replace(/@media[^{]*$/, '').trim().replace(/\s+/g, ' ')
  if (!sel || sel.startsWith('@') || /^(from|to|\d+%)$/.test(sel)) continue
  const decl = {}
  for (const d of r[2].split(';')) { const i = d.indexOf(':'); if (i > 0) decl[d.slice(0, i).trim()] = d.slice(i + 1).trim() }
  rules.push({ sels: sel.split(',').map((s) => s.trim()), decl })
}

const TARGET = /var\(--muted\)|#888888\b|#888\b|#767676|#999\b|#999999/i
const DARK = /data-theme="dark"|:not\(\[data-theme="light"\]\)/
const files = []
const walk = (d) => {
  for (const f of readdirSync(d)) {
    const p = join(d, f)
    if (statSync(p).isDirectory()) { if (f !== 'dev') walk(p) }
    else if (f.endsWith('.tsx') && !f.includes('.test.')) files.push(p)
  }
}
walk('src')
const tsx = files.map((f) => [f.split(String.fromCharCode(92)).join('/'), readFileSync(f, 'utf8')])

for (const [ri, r] of rules.entries()) {
  const v = r.decl.color
  if (!v || !TARGET.test(v)) continue
  for (const s of r.sels) {
    if (DARK.test(s)) continue
    const last = s.split(' ').pop()
    const cls = [...s.matchAll(/\.([a-zA-Z0-9_-]+)/g)].map((m) => m[1])
    const key = cls[cls.length - 1]
    if (!key) continue
    const overrides = rules.flatMap((o) => o.decl.color
      ? o.sels.filter((os) => os !== s && (os.endsWith(' ' + s) || os.endsWith(' ' + s.replace(/:[a-z-]+(([^)]*))?$/, ''))) && !DARK.test(os)).map((os) => `${os} {${o.decl.color}}`)
      : [])
    const re = new RegExp(`(^|[^a-zA-Z0-9_-])${key}([^a-zA-Z0-9_-]|$)`)
    const users = tsx.filter(([, c]) => re.test(c)).map(([f]) => f.replace(/^src\//, ''))
    console.log(`${users.length ? 'USED' : 'DEAD'} | ${s} {color:${v}} | overrides: ${overrides.length ? overrides.join(' ; ') : '-'} | ${users.slice(0, 4).join(', ')}`)
  }
}
