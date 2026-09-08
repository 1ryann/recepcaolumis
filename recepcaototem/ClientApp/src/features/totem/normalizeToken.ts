const ZERO_WIDTH = new RegExp('[\\u200B\\u200C\\u200D\\uFEFF]', 'g')

// Accepts whatever the camera or a paste produced and returns the bare check-in
// token: trims whitespace and zero-width characters, and unwraps a URL that carries
// the token in a `token`/`code` query parameter or as its last path segment.
export function normalizeToken(raw: string): string {
  const cleaned = raw.replace(ZERO_WIDTH, '').trim()
  if (!/^[a-zA-Z][a-zA-Z0-9+.-]*:\/\//.test(cleaned)) return cleaned
  try {
    const url = new URL(cleaned)
    const param = url.searchParams.get('token') ?? url.searchParams.get('code')
    if (param) return param.trim()
    const segments = url.pathname.split('/').filter(Boolean)
    return (segments[segments.length - 1] ?? cleaned).trim()
  } catch {
    return cleaned
  }
}
