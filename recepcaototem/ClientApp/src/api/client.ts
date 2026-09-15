let csrfToken: string | null = null
export function resetCsrfToken() { csrfToken = null }

type QueryValue = string | number | boolean | null | undefined

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

export interface GetOptions {
  query?: Record<string, QueryValue>
  signal?: AbortSignal
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { ...init, credentials: 'same-origin', cache: 'no-store' })
  if (!response.ok) {
    // A 401 on any call means the browser cookie is no longer authenticated (session
    // expired, signed out elsewhere). Signal it once so the SessionProvider can
    // revalidate and route protected areas back to the login screen instead of
    // leaving a stale, half-broken shell on screen.
    if (response.status === 401 && typeof window !== 'undefined')
      window.dispatchEvent(new Event('lumis:unauthorized'))
    throw await decodeError(response)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

async function decodeError(response: Response) {
  let payload: unknown
  try { payload = await response.json() } catch { payload = null }
  const value = payload && typeof payload === 'object' ? payload as Record<string, unknown> : {}
  const code = typeof value.code === 'string' && value.code ? value.code : 'REQUEST_FAILED'
  const message = typeof value.message === 'string' && value.message
    ? value.message
    : 'Não foi possível concluir a solicitação.'
  return new ApiError(response.status, code, message)
}

function withQuery(path: string, query?: Record<string, QueryValue>) {
  if (!query) return path
  const values = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined) values.append(key, String(value))
  }
  const serialized = values.toString()
  return serialized ? `${path}?${serialized}` : path
}

async function getCsrfToken() {
  if (csrfToken) return csrfToken
  const response = await request<{ token: string }>('/api/auth/csrf')
  csrfToken = response.token
  return csrfToken
}

async function mutate<T>(method: string, path: string, body: BodyInit, contentType?: string): Promise<T> {
  const send = async () => {
    const token = await getCsrfToken()
    const headers: Record<string, string> = { 'X-CSRF-TOKEN': token }
    if (contentType) headers['Content-Type'] = contentType
    return request<T>(path, { method, headers, body })
  }

  try {
    return await send()
  } catch (error) {
    if (!(error instanceof ApiError) || error.code !== 'INVALID_CSRF') throw error
    csrfToken = null
    return send()
  }
}

export const apiClient = {
  get<T>(path: string, options: GetOptions = {}) {
    return request<T>(withQuery(path, options.query), { signal: options.signal })
  },
  // Bypasses `mutate`/CSRF entirely: for a public, AllowAnonymous, no-antiforgery endpoint
  // (e.g. the totem room rental inquiry) there is no session cookie to protect, and fetching
  // a CSRF token first would be a wasted round trip. Calls `request` directly, same as `get`.
  postPublic<T>(path: string, body: unknown) {
    return request<T>(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
  },
  post<T>(path: string, body: unknown) {
    return mutate<T>('POST', path, JSON.stringify(body), 'application/json')
  },
  put<T>(path: string, body: unknown) {
    return mutate<T>('PUT', path, JSON.stringify(body), 'application/json')
  },
  delete<T>(path: string, body: unknown) {
    return mutate<T>('DELETE', path, JSON.stringify(body), 'application/json')
  },
  putMultipart<T>(path: string, body: FormData) {
    return mutate<T>('PUT', path, body)
  },
  postMultipart<T>(path: string, body: FormData) {
    return mutate<T>('POST', path, body)
  },
}
