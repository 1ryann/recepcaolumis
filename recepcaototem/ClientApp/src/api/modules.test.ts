import { beforeEach, expect, test, vi } from 'vitest'
import { apiClient } from './client'
import { leasesApi, professionalsApi, professionalLeasesApi, roomsApi, tenantsApi } from './modules'

vi.mock('./client', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), putMultipart: vi.fn() },
}))

beforeEach(() => vi.clearAllMocks())

test('professional reads use server query and AbortSignal', async () => {
  const signal = new AbortController().signal
  await professionalsApi.list({ search: 'ana', status: 'active', page: 2, pageSize: 20 }, signal)
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/professionals', {
    query: { search: 'ana', status: 'active', page: 2, pageSize: 20 }, signal,
  })
  await professionalsApi.detail('p-1', signal)
  expect(apiClient.get).toHaveBeenLastCalledWith('/api/admin/professionals/p-1', { signal })
})

test('professional mutations send only approved contracts', async () => {
  await professionalsApi.create({ name: 'Ana', profession: 'Fisio', whatsApp: '+5565999999999' })
  await professionalsApi.update('p-1', {
    name: 'Ana', profession: 'Fisio', whatsApp: '+5565999999999', concurrencyToken: 'rv',
  })
  await professionalsApi.changeStatus('p-1', false, 'rv2')
  expect(apiClient.post).toHaveBeenNthCalledWith(1, '/api/admin/professionals', expect.any(Object))
  expect(apiClient.put).toHaveBeenCalledWith('/api/admin/professionals/p-1', expect.objectContaining({ concurrencyToken: 'rv' }))
  expect(apiClient.post).toHaveBeenNthCalledWith(2, '/api/admin/professionals/p-1/deactivate', { concurrencyToken: 'rv2' })
})

test('photo and user-link operations use dedicated endpoints', async () => {
  const photo = new File(['image'], 'photo.png', { type: 'image/png' })
  await professionalsApi.putPhoto('p-1', photo, 'rv')
  const form = vi.mocked(apiClient.putMultipart).mock.calls[0][1]
  const uploaded = form.get('file') as File
  expect(uploaded.name).toBe('photo.png')
  expect(uploaded.type).toBe('image/png')
  expect(form.get('concurrencyToken')).toBe('rv')
  await professionalsApi.removePhoto('p-1', 'rv2')
  await professionalsApi.eligibleUsers({ search: 'ana', page: 1, pageSize: 20 })
  await professionalsApi.userLink('p-1')
  await professionalsApi.putUserLink('p-1', 'user-1', 'rv3')
  await professionalsApi.removeUserLink('p-1', 'rv4')
  expect(apiClient.delete).toHaveBeenNthCalledWith(1, '/api/admin/professionals/p-1/photo', { concurrencyToken: 'rv2' })
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/professionals/eligible-users', expect.any(Object))
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/professionals/p-1/user-link')
  expect(apiClient.put).toHaveBeenCalledWith('/api/admin/professionals/p-1/user-link', {
    applicationUserId: 'user-1', concurrencyToken: 'rv3',
  })
  expect(apiClient.delete).toHaveBeenNthCalledWith(2, '/api/admin/professionals/p-1/user-link', { concurrencyToken: 'rv4' })
})

test('room operations use server paging and opaque concurrency tokens', async () => {
  const signal = new AbortController().signal
  await roomsApi.list({ search: 'sala', status: 'all', page: 1, pageSize: 20 }, signal)
  await roomsApi.create({ name: 'Sala 1', description: null, hourlyRate: 0.1, dailyRate: 100.99 })
  await roomsApi.update('r-1', {
    name: 'Sala 1', description: null, hourlyRate: 0.1, dailyRate: 100.99, concurrencyToken: 'rv',
  })
  await roomsApi.changeStatus('r-1', true, 'rv2')
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/rooms', {
    query: { search: 'sala', status: 'all', page: 1, pageSize: 20 }, signal,
  })
  expect(apiClient.put).toHaveBeenCalledWith('/api/admin/rooms/r-1', expect.objectContaining({ concurrencyToken: 'rv' }))
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/rooms/r-1/activate', { concurrencyToken: 'rv2' })
})

test('tenant and lease clients use relative contracts and opaque concurrency tokens', async () => {
  const signal = new AbortController().signal
  await tenantsApi.list({ search: 'ana', status: 'active', page: 1, pageSize: 20 }, signal)
  await tenantsApi.create({ name: 'Ana', kind: 'INDIVIDUAL' })
  await tenantsApi.update('t-1', { name: 'Ana', kind: 'INDIVIDUAL', concurrencyToken: 'tv1' })
  await leasesApi.list({ status: 'AGENDADA', page: 2, pageSize: 20, roomId: 'r-1' }, signal)
  const lease = {
    tenantId: 't-1', professionalId: 'p-1', roomId: 'r-1', mode: 'HOURLY' as const,
    contractedRate: 150.5, billingStartAt: '2026-09-06T10:00:00Z', billingDueDay: 10,
    occupancyStartAt: '2026-09-07T10:00:00Z', occupancyEndAt: '2026-09-07T12:00:00Z',
  }
  await leasesApi.create(lease)
  await leasesApi.update('l-1', { ...lease, concurrencyToken: 'lv1' })
  await leasesApi.postpone('l-1', '2026-09-07T11:00:00Z', 'lv2')
  await leasesApi.cancel('l-1', 'lv3')
  await leasesApi.end('l-1', null, 'lv4')
  await professionalLeasesApi.list({ page: 1, pageSize: 20 }, signal)
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/leases', {
    query: { status: 'AGENDADA', page: 2, pageSize: 20, roomId: 'r-1' }, signal,
  })
  expect(apiClient.put).toHaveBeenCalledWith('/api/admin/leases/l-1', expect.objectContaining({ concurrencyToken: 'lv1' }))
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/leases/l-1/postpone-occupancy', {
    occupancyStartAt: '2026-09-07T11:00:00Z', concurrencyToken: 'lv2',
  })
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/leases/l-1/cancel', { concurrencyToken: 'lv3' })
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/leases/l-1/end', { endAt: null, concurrencyToken: 'lv4' })
  expect(apiClient.get).toHaveBeenCalledWith('/api/professional/leases', { query: { page: 1, pageSize: 20 }, signal })
})
