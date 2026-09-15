import { beforeEach, expect, test, vi } from 'vitest'
import { apiClient } from './client'
import { leasesApi, professionalReservationsApi, professionalsApi, professionalLeasesApi,
  professionalVisitsApi, reservationsApi, roomsApi, roomPhotosApi, tenantsApi, visitsApi, customerApi,
  professionalAvailabilityApi, adminProfessionalAvailabilityApi, operatingHoursApi, roomBlocksApi,
  totemRoomApi, roomRentalInquiriesApi } from './modules'

vi.mock('./client', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), putMultipart: vi.fn(), postMultipart: vi.fn(), postPublic: vi.fn() },
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

test('room photo operations use dedicated endpoints, encode ids and send exact bodies', async () => {
  const signal = new AbortController().signal
  await roomPhotosApi.list('room 1', signal)
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/rooms/room%201/photos', { signal })

  await roomPhotosApi.list('room-1')
  expect(apiClient.get).toHaveBeenLastCalledWith('/api/admin/rooms/room-1/photos', { signal: undefined })

  const photo = new File(['image'], 'sala.png', { type: 'image/png' })
  await roomPhotosApi.upload('room-1', photo)
  expect(vi.mocked(apiClient.postMultipart).mock.calls[0][0]).toBe('/api/admin/rooms/room-1/photos')
  const form = vi.mocked(apiClient.postMultipart).mock.calls[0][1]
  expect(Array.from(form.keys())).toEqual(['file'])
  const uploaded = form.get('file') as File
  expect(uploaded.name).toBe('sala.png')
  expect(uploaded.type).toBe('image/png')

  await roomPhotosApi.remove('room 1', 'photo 1')
  expect(apiClient.delete).toHaveBeenCalledWith('/api/admin/rooms/room%201/photos/photo%201', {})

  await roomPhotosApi.reorder('room-1', ['photo-2', 'photo-1'])
  expect(apiClient.put).toHaveBeenCalledWith('/api/admin/rooms/room-1/photos/reorder', { orderedPhotoIds: ['photo-2', 'photo-1'] })

  await roomPhotosApi.setCover('room 1', 'photo 2')
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/rooms/room%201/photos/photo%202/cover', {})
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

test('reservation clients keep administrative and professional contracts separate', async () => {
  const signal = new AbortController().signal
  const period = { startAt: '2026-09-07T10:00:00Z', endAt: '2026-09-07T11:00:00Z' }
  await reservationsApi.list({ status: 'PENDING', roomId: 'r-1', page: 1, pageSize: 20 }, signal)
  await reservationsApi.create({ roomId: 'r-1', professionalId: 'p-1', ...period })
  await reservationsApi.approve('x-1', 'rv1')
  await reservationsApi.reject('x-2', 'Indisponível', 'rv2')
  await reservationsApi.reschedule('x-3', { ...period, concurrencyToken: 'rv3' })
  await reservationsApi.cancel('x-4', 'rv4')
  await professionalReservationsApi.list({ status: 'APPROVED', page: 1, pageSize: 20 }, signal)
  await professionalReservationsApi.create({ roomId: 'r-1', ...period })
  await professionalReservationsApi.requestReschedule('x-5', { ...period, concurrencyToken: 'rv5' })
  await professionalReservationsApi.requestCancellation('x-6', 'rv6')

  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/reservations', {
    query: { status: 'PENDING', roomId: 'r-1', page: 1, pageSize: 20 }, signal,
  })
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/reservations/x-1/approve', { concurrencyToken: 'rv1' })
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/reservations/x-2/reject', {
    reason: 'Indisponível', concurrencyToken: 'rv2',
  })
  expect(apiClient.post).toHaveBeenCalledWith('/api/professional/reservations', { roomId: 'r-1', ...period })
  expect(apiClient.get).toHaveBeenCalledWith('/api/professional/reservations', {
    query: { status: 'APPROVED', page: 1, pageSize: 20 }, signal,
  })
  expect(apiClient.post).toHaveBeenCalledWith('/api/professional/reservations/x-6/cancel-request', {
    concurrencyToken: 'rv6',
  })
})

test('visit clients keep operations and owned professional contracts separate', async () => {
  const signal = new AbortController().signal
  await visitsApi.list({ status: 'WAITING', professionalId: 'p-1', roomId: 'r-1', page: 1, pageSize: 20 }, signal)
  await visitsApi.create({ professionalId: 'p-1', roomId: 'r-1', reservationId: null, visitorName: 'Maria' })
  await visitsApi.start('v-1', 'vv1')
  await visitsApi.end('v-2', 'vv2')
  await visitsApi.cancel('v-3', 'vv3')
  await visitsApi.correct('v-4', 'IN_SERVICE', 'Correção operacional', 'vv4')
  await professionalVisitsApi.list({ status: 'IN_SERVICE', page: 1, pageSize: 20 }, signal)
  await professionalVisitsApi.start('v-5', 'vv5')

  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/visits', {
    query: { status: 'WAITING', professionalId: 'p-1', roomId: 'r-1', page: 1, pageSize: 20 }, signal,
  })
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/visits/v-4/correct', {
    status: 'IN_SERVICE', reason: 'Correção operacional', concurrencyToken: 'vv4',
  })
  expect(apiClient.get).toHaveBeenCalledWith('/api/professional/visits', {
    query: { status: 'IN_SERVICE', page: 1, pageSize: 20 }, signal,
  })
})

test('availability clients use the professional and operations contracts', async () => {
  const weekly = { mode: 'CUSTOM' as const, days: [], concurrencyToken: 'pv1' }
  const exception = { date: '2026-09-15', allDay: true, startTime: null, endTime: null, reason: 'Folga' }
  await professionalAvailabilityApi.get()
  await professionalAvailabilityApi.update(weekly)
  await professionalAvailabilityApi.listExceptions()
  await professionalAvailabilityApi.createException(exception)
  await professionalAvailabilityApi.updateException('ex-1', { ...exception, concurrencyToken: 'exv1' })
  await professionalAvailabilityApi.deleteException('ex-1', 'exv2')
  await adminProfessionalAvailabilityApi.get('p-1')
  await adminProfessionalAvailabilityApi.update('p-1', weekly)
  await adminProfessionalAvailabilityApi.listExceptions('p-1')
  await adminProfessionalAvailabilityApi.createException('p-1', exception)
  await adminProfessionalAvailabilityApi.updateException('p-1', 'ex-1', { ...exception, concurrencyToken: 'exv3' })
  await adminProfessionalAvailabilityApi.deleteException('p-1', 'ex-1', 'exv4')
  expect(apiClient.get).toHaveBeenCalledWith('/api/professional/availability', { signal: undefined })
  expect(apiClient.put).toHaveBeenCalledWith('/api/professional/availability', weekly)
  expect(apiClient.post).toHaveBeenCalledWith('/api/professional/availability/exceptions', exception)
  expect(apiClient.delete).toHaveBeenCalledWith('/api/professional/availability/exceptions/ex-1', { concurrencyToken: 'exv2' })
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/professionals/p-1/availability', { signal: undefined })
  expect(apiClient.put).toHaveBeenCalledWith('/api/admin/professionals/p-1/availability', weekly)
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/professionals/p-1/availability/exceptions', exception)
  expect(apiClient.delete).toHaveBeenCalledWith('/api/admin/professionals/p-1/availability/exceptions/ex-1', { concurrencyToken: 'exv4' })
})

test('exception listing always sends the mandatory from/to range the API requires', async () => {
  const signal = new AbortController().signal
  await professionalAvailabilityApi.listExceptions(signal)
  await adminProfessionalAvailabilityApi.listExceptions('p-1', signal)
  const isoDate = expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/)
  expect(apiClient.get).toHaveBeenCalledWith('/api/professional/availability/exceptions', {
    query: expect.objectContaining({ from: isoDate, to: isoDate }), signal,
  })
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/professionals/p-1/availability/exceptions', {
    query: expect.objectContaining({ from: isoDate, to: isoDate }), signal,
  })
  const range = vi.mocked(apiClient.get).mock.calls
    .find(([path]) => path === '/api/professional/availability/exceptions')![1] as { query: { from: string, to: string } }
  const spanDays = (Date.parse(range.query.to) - Date.parse(range.query.from)) / 86_400_000
  expect(spanDays).toBeGreaterThan(0)
  expect(spanDays).toBeLessThanOrEqual(365)
})

test('operating hours and room block clients preserve admin routes and concurrency', async () => {
  const days = [{ dayOfWeek: 'MONDAY', intervals: [{ opensAt: '08:00', closesAt: '18:00' }] }]
  await operatingHoursApi.get()
  await operatingHoursApi.update({ days, concurrencyToken: 'oh1' })
  await roomBlocksApi.list({ status: 'ACTIVE', roomId: 'r-1', page: 1, pageSize: 20 })
  await roomBlocksApi.detail('b-1')
  await roomBlocksApi.create({ roomId: 'r-1', startAt: '2026-09-15T10:00:00Z', endAt: '2026-09-15T11:00:00Z', reason: 'Manutenção' })
  await roomBlocksApi.update('b-1', { startAt: '2026-09-15T10:00:00Z', endAt: '2026-09-15T11:00:00Z', reason: 'Limpeza', concurrencyToken: 'b1' })
  await roomBlocksApi.cancel('b-1', 'b2')
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/operating-hours', { signal: undefined })
  expect(apiClient.put).toHaveBeenCalledWith('/api/admin/operating-hours', { days, concurrencyToken: 'oh1' })
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/room-blocks', { query: { status: 'ACTIVE', roomId: 'r-1', page: 1, pageSize: 20 }, signal: undefined })
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/room-blocks/b-1', { signal: undefined })
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/room-blocks', expect.any(Object))
  expect(apiClient.put).toHaveBeenCalledWith('/api/admin/room-blocks/b-1', expect.objectContaining({ concurrencyToken: 'b1' }))
  expect(apiClient.post).toHaveBeenCalledWith('/api/admin/room-blocks/b-1/cancel', { concurrencyToken: 'b2' })
})

test('customer registration and profile clients keep identity server-owned', async () => {
  await customerApi.register({
    name: 'Carlos Oliveira', phone: '(69) 99999-9999', email: 'carlos@example.com',
    password: 'Senha123!', confirmation: 'Senha123!',
  })
  await customerApi.me()
  await customerApi.professionals()
  await customerApi.reservations({ page: 1, pageSize: 20 })
  expect(apiClient.post).toHaveBeenCalledWith('/api/customer/register', expect.not.objectContaining({
    customerId: expect.anything(), role: expect.anything(), roomId: expect.anything(),
  }))
  expect(apiClient.get).toHaveBeenCalledWith('/api/customer/me', { signal: undefined })
  expect(apiClient.get).toHaveBeenCalledWith('/api/customer/professionals', { signal: undefined })
  expect(apiClient.get).toHaveBeenCalledWith('/api/customer/reservations', { query: { page: 1, pageSize: 20 }, signal: undefined })
})

test('admin room rental inquiry client uses server paging and a dedicated detail route', async () => {
  const signal = new AbortController().signal
  await roomRentalInquiriesApi.list({ page: 2, pageSize: 20 }, signal)
  await roomRentalInquiriesApi.get('inquiry 1', signal)
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/room-rental-inquiries', {
    query: { page: 2, pageSize: 20 }, signal,
  })
  expect(apiClient.get).toHaveBeenCalledWith(`/api/admin/room-rental-inquiries/${encodeURIComponent('inquiry 1')}`, { signal })
})

test('public totem room catalog reads use GET with escaped paths and the inquiry client posts without CSRF', async () => {
  const signal = new AbortController().signal
  await totemRoomApi.list(signal)
  await totemRoomApi.detail('room 1', signal)
  await totemRoomApi.createInquiry('room 1', {
    fullName: 'Ana Souza', whatsApp: '+5565999999999', professionOrCompany: 'Fisioterapeuta', note: 'Manhãs',
  })
  expect(apiClient.get).toHaveBeenCalledWith('/api/totem/rooms', { signal })
  expect(apiClient.get).toHaveBeenCalledWith(`/api/totem/rooms/${encodeURIComponent('room 1')}`, { signal })
  // The rental inquiry endpoint is AllowAnonymous with no antiforgery, so this must go
  // through `postPublic` (no CSRF round trip) rather than the regular `post`.
  expect(apiClient.postPublic).toHaveBeenCalledWith(
    `/api/totem/rooms/${encodeURIComponent('room 1')}/rental-inquiries`,
    { fullName: 'Ana Souza', whatsApp: '+5565999999999', professionOrCompany: 'Fisioterapeuta', note: 'Manhãs' },
  )
  expect(apiClient.post).not.toHaveBeenCalled()
})
