// Strict allowlist for post-login redirects into the CUSTOMER portal.
// Only paths under /cliente are ever accepted. Everything else -> null.

const hasControlChar = (s: string) =>
  [...s].some((c) => { const n = c.charCodeAt(0); return n < 0x20 || (n >= 0x7f && n <= 0x9f) })
const CUSTOMER_PATH = /^\/cliente(?:\/[A-Za-z0-9_-]+)*\/?$/
const CUSTOMER_QUERY = /^[A-Za-z0-9_-]+=[A-Za-z0-9_-]*(?:&[A-Za-z0-9_-]+=[A-Za-z0-9_-]*)*$/

export function safeCustomerReturnUrl(raw: string | null | undefined): string | null {
  if (!raw || typeof raw !== 'string') return null
  if (raw.length > 512) return null
  if (hasControlChar(raw)) return null

  let value = raw
  for (let i = 0; i < 4; i++) {
    let next: string
    try { next = decodeURIComponent(value) } catch { return null }
    if (next === value) break
    value = next
  }

  if (hasControlChar(value)) return null
  if (value.includes('\\')) return null
  if (value.includes('%')) return null
  if (value.includes('://')) return null
  if (value.includes('..')) return null
  if (value.startsWith('//')) return null
  if (!value.startsWith('/cliente')) return null

  const q = value.indexOf('?')
  const path = q === -1 ? value : value.slice(0, q)
  const query = q === -1 ? '' : value.slice(q + 1)
  if (query.includes('?')) return null
  if (path.includes('//')) return null
  if (!CUSTOMER_PATH.test(path)) return null
  if (query && !CUSTOMER_QUERY.test(query)) return null

  return query ? `${path}?${query}` : path
}
