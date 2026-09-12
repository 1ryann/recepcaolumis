import { afterEach, expect, test, vi } from 'vitest'
import { apiClient } from './client'
import { professionalProfileApi } from './modules'

afterEach(() => vi.restoreAllMocks())

test('professionalProfileApi.get GETs /api/professional/me', async () => {
  const get = vi.spyOn(apiClient, 'get').mockResolvedValue({ name: 'Ana', profession: 'Fisio', description: null, whatsApp: '+5511999998888', hasPhoto: false, photoUrl: null, concurrencyToken: 'tok' } as never)
  const result = await professionalProfileApi.get()
  expect(get).toHaveBeenCalledWith('/api/professional/me', { signal: undefined })
  expect(result.whatsApp).toBe('+5511999998888')
})

test('professionalProfileApi.update PUTs the editable fields', async () => {
  const put = vi.spyOn(apiClient, 'put').mockResolvedValue({} as never)
  await professionalProfileApi.update({ whatsApp: '11999998888', description: 'Oi', concurrencyToken: 'tok' })
  expect(put).toHaveBeenCalledWith('/api/professional/me', { whatsApp: '11999998888', description: 'Oi', concurrencyToken: 'tok' })
})

test('professionalProfileApi.uploadPhoto POSTs multipart with file and concurrencyToken', async () => {
  const postMultipart = vi.spyOn(apiClient, 'postMultipart').mockResolvedValue({ hasPhoto: true, photoUrl: '/x', concurrencyToken: 'tok2' } as never)
  const file = new Blob(['x'], { type: 'image/png' })
  await professionalProfileApi.uploadPhoto(file, 'tok')
  const [path, form] = postMultipart.mock.calls[0]
  expect(path).toBe('/api/professional/me/photo')
  expect(form.get('concurrencyToken')).toBe('tok')
  // jsdom's FormData (like the real browser implementation) always wraps an appended
  // Blob into a fresh File entry, so reference equality with the original Blob is never
  // possible here — assert on content/type instead, matching the existing
  // professionalsApi.putPhoto test's pattern in api/modules.test.ts.
  const uploaded = form.get('file') as File
  expect(uploaded.type).toBe('image/png')
  expect(uploaded.size).toBe(file.size)
})

test('professionalProfileApi.deletePhoto DELETEs with concurrencyToken', async () => {
  const del = vi.spyOn(apiClient, 'delete').mockResolvedValue({ hasPhoto: false, photoUrl: null, concurrencyToken: 'tok3' } as never)
  await professionalProfileApi.deletePhoto('tok')
  expect(del).toHaveBeenCalledWith('/api/professional/me/photo', { concurrencyToken: 'tok' })
})
