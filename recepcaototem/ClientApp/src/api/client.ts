let csrfToken: string | null = null

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { ...init, credentials: 'same-origin' })
  if (!response.ok) throw response
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

async function getCsrfToken() {
  if (csrfToken) return csrfToken
  const response = await request<{ token: string }>('/api/auth/csrf')
  csrfToken = response.token
  return csrfToken
}

export const apiClient = {
  get<T>(path: string) { return request<T>(path) },
  async post<T>(path: string, body: unknown) {
    const token = await getCsrfToken()
    return request<T>(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
      body: JSON.stringify(body),
    })
  },
}
