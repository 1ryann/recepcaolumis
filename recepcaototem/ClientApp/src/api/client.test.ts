import { beforeEach, expect, test, vi } from 'vitest'

beforeEach(() => {
  vi.resetModules()
  vi.unstubAllGlobals()
})

test('serializes GET query values and forwards an AbortSignal', async () => {
  const fetchMock = vi.fn().mockResolvedValue(json({ items: [] }))
  vi.stubGlobal('fetch', fetchMock)
  const { apiClient } = await import('./client')
  const controller = new AbortController()
  await apiClient.get('/api/admin/professionals', {
    query: { search: 'Ana Silva', status: 'active', page: 1, ignored: undefined },
    signal: controller.signal,
  })
  expect(fetchMock).toHaveBeenCalledWith(
    '/api/admin/professionals?search=Ana+Silva&status=active&page=1',
    expect.objectContaining({ credentials: 'same-origin', signal: controller.signal }),
  )
})

test('sends one in-memory CSRF token on JSON POST PUT and DELETE', async () => {
  const fetchMock = vi.fn()
    .mockResolvedValueOnce(json({ token: 'csrf-token' }))
    .mockResolvedValue(new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetchMock)
  const { apiClient } = await import('./client')
  await apiClient.post('/post', { value: 1 })
  await apiClient.put('/put', { value: 2 })
  await apiClient.delete('/delete', { value: 3 })
  expect(fetchMock).toHaveBeenCalledTimes(4)
  for (const call of [2, 3, 4]) {
    expect(fetchMock).toHaveBeenNthCalledWith(call, expect.any(String), expect.objectContaining({
      credentials: 'same-origin',
      headers: expect.objectContaining({
        'Content-Type': 'application/json',
        'X-CSRF-TOKEN': 'csrf-token',
      }),
    }))
  }
  expect(localStorage.length).toBe(0)
})

test('multipart PUT lets fetch generate the boundary', async () => {
  const fetchMock = vi.fn()
    .mockResolvedValueOnce(json({ token: 'csrf-token' }))
    .mockResolvedValueOnce(json({ id: 'professional' }))
  vi.stubGlobal('fetch', fetchMock)
  const { apiClient } = await import('./client')
  const form = new FormData()
  form.append('concurrencyToken', 'token')
  form.append('file', new Blob(['photo'], { type: 'image/png' }), 'photo.png')
  await apiClient.putMultipart('/photo', form)
  const init = fetchMock.mock.calls[1][1] as RequestInit
  expect(init.body).toBe(form)
  expect(init.headers).toEqual(expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }))
  expect(init.headers).not.toHaveProperty('Content-Type')
})

test('throws a stable ApiError decoded from a safe API response', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(
    { code: 'RESOURCE_MODIFIED', message: 'Recarregue os dados.' }, 409,
  )))
  const { apiClient, ApiError } = await import('./client')
  const error = await apiClient.get('/conflict').catch(value => value)
  expect(error).toBeInstanceOf(ApiError)
  expect(error).toEqual(expect.objectContaining({
    status: 409, code: 'RESOURCE_MODIFIED', message: 'Recarregue os dados.',
  }))
})

test('refreshes CSRF and retries exactly once only for INVALID_CSRF', async () => {
  const fetchMock = vi.fn()
    .mockResolvedValueOnce(json({ token: 'first' }))
    .mockResolvedValueOnce(json({ code: 'INVALID_CSRF', message: 'Inválido.' }, 400))
    .mockResolvedValueOnce(json({ token: 'second' }))
    .mockResolvedValueOnce(new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetchMock)
  const { apiClient } = await import('./client')
  await apiClient.post('/mutation', {})
  expect(fetchMock).toHaveBeenCalledTimes(4)
  expect((fetchMock.mock.calls[1][1] as RequestInit).headers).toEqual(
    expect.objectContaining({ 'X-CSRF-TOKEN': 'first' }),
  )
  expect((fetchMock.mock.calls[3][1] as RequestInit).headers).toEqual(
    expect.objectContaining({ 'X-CSRF-TOKEN': 'second' }),
  )
})

test('does not retry a mutation for another 400 error', async () => {
  const fetchMock = vi.fn()
    .mockResolvedValueOnce(json({ token: 'csrf-token' }))
    .mockResolvedValueOnce(json({ code: 'INVALID_ROOM_RATE', message: 'Tarifa inválida.' }, 400))
  vi.stubGlobal('fetch', fetchMock)
  const { apiClient } = await import('./client')
  await expect(apiClient.put('/room', {})).rejects.toMatchObject({ code: 'INVALID_ROOM_RATE' })
  expect(fetchMock).toHaveBeenCalledTimes(2)
})

test('postMultipart sends a POST with FormData body and a CSRF header', async () => {
  const fetchMock = vi.fn()
    .mockResolvedValueOnce(json({ token: 'csrf-token' }))
    .mockResolvedValueOnce(json({ ok: true }))
  vi.stubGlobal('fetch', fetchMock)
  const { apiClient } = await import('./client')
  const form = new FormData()
  form.append('file', new Blob(['x']), 'x.png')
  const result = await apiClient.postMultipart<{ ok: boolean }>('/api/professional/me/photo', form)
  expect(result.ok).toBe(true)
  const init = fetchMock.mock.calls[1][1] as RequestInit
  expect(init.method).toBe('POST')
  expect(init.body).toBe(form)
  expect(init.headers).toEqual(expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }))
})

test('postPublic sends JSON same-origin without ever fetching a CSRF token', async () => {
  const fetchMock = vi.fn().mockResolvedValue(json({ ok: true }))
  vi.stubGlobal('fetch', fetchMock)
  const { apiClient } = await import('./client')
  const result = await apiClient.postPublic<{ ok: boolean }>('/api/totem/rooms/r1/rental-inquiries', { fullName: 'Ana' })
  expect(result.ok).toBe(true)
  expect(fetchMock).toHaveBeenCalledTimes(1)
  expect(fetchMock).toHaveBeenCalledWith('/api/totem/rooms/r1/rental-inquiries', expect.objectContaining({
    method: 'POST',
    credentials: 'same-origin',
    headers: expect.objectContaining({ 'Content-Type': 'application/json' }),
    body: JSON.stringify({ fullName: 'Ana' }),
  }))
  expect(fetchMock.mock.calls.every(([url]) => url !== '/api/auth/csrf')).toBe(true)
  expect((fetchMock.mock.calls[0][1] as RequestInit).headers).not.toHaveProperty('X-CSRF-TOKEN')
})

test('postPublic decodes a stable ApiError on failure without retrying', async () => {
  const fetchMock = vi.fn().mockResolvedValue(json(
    { code: 'INVALID_ROOM_RENTAL_INQUIRY', message: 'Os dados do interesse são inválidos.' }, 400,
  ))
  vi.stubGlobal('fetch', fetchMock)
  const { apiClient, ApiError } = await import('./client')
  const error = await apiClient.postPublic('/api/totem/rooms/r1/rental-inquiries', {}).catch((value) => value)
  expect(error).toBeInstanceOf(ApiError)
  expect(error).toEqual(expect.objectContaining({
    status: 400, code: 'INVALID_ROOM_RENTAL_INQUIRY', message: 'Os dados do interesse são inválidos.',
  }))
  expect(fetchMock).toHaveBeenCalledTimes(1)
})

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
