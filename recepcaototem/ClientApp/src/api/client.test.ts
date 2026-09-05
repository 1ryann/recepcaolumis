import { vi } from 'vitest'
import { apiClient } from './client'

test('keeps csrf in memory and sends it on same-origin mutations', async () => {
  const fetchMock = vi.fn()
    .mockResolvedValueOnce(new Response(JSON.stringify({ token: 'csrf-token' }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    .mockResolvedValueOnce(new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetchMock)
  await apiClient.post('/api/auth/logout', {})
  expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/auth/logout', expect.objectContaining({
    credentials: 'same-origin',
    headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
  }))
  expect(localStorage.length).toBe(0)
})
