import { afterEach, expect, test, vi } from 'vitest'
import { apiClient } from './client'
import { totemApi } from './modules'

afterEach(() => vi.restoreAllMocks())

test('totemApi.professionals GETs the public carousel endpoint', async () => {
  const get = vi.spyOn(apiClient, 'get').mockResolvedValue([
    { id: 'p1', name: 'Ana', profession: 'Fisio', photoUrl: null, status: 'AVAILABLE' },
  ] as never)
  const result = await totemApi.professionals()
  expect(get).toHaveBeenCalledWith('/api/totem/professionals', { signal: undefined })
  expect(result[0].status).toBe('AVAILABLE')
})
