import { beforeEach, expect, test, vi } from 'vitest'
import { apiClient } from './client'
import { customerApi, professionalReservationsApi, totemApi } from './modules'
import type { HandoffStatusDto } from './modules'

vi.mock('./client', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), putMultipart: vi.fn() },
}))

beforeEach(() => vi.clearAllMocks())

test('createHandoff posts the professional id to the totem handoff endpoint', async () => {
  vi.mocked(apiClient.post).mockResolvedValue({
    id: 'h1', handoffToken: 'H', statusToken: 'S', expiresAt: 'x',
    professionalName: 'Ana', profession: 'Fisio',
  })
  await totemApi.createHandoff('p1')
  expect(apiClient.post).toHaveBeenCalledWith('/api/totem/booking-handoffs', { professionalId: 'p1' })
})

test('pollHandoff posts the status token in the body, not the URL', async () => {
  vi.mocked(apiClient.post).mockResolvedValue({ status: 'PENDING', expiresAt: 'x' })
  await totemApi.pollHandoff('h1', 'stok')
  expect(apiClient.post).toHaveBeenCalledWith('/api/totem/booking-handoffs/h1/status', { statusToken: 'stok' })
})

test('cancelHandoff posts the status token in the body', async () => {
  vi.mocked(apiClient.post).mockResolvedValue({ status: 'EXPIRED' })
  await totemApi.cancelHandoff('h1', 's')
  expect(apiClient.post).toHaveBeenCalledWith('/api/totem/booking-handoffs/h1/cancel', { statusToken: 's' })
})

test('claimHandoff posts the handoff token to the claim endpoint', async () => {
  vi.mocked(apiClient.post).mockResolvedValue({ status: 'STARTED', expiresAt: 'x' })
  await totemApi.claimHandoff('H')
  expect(apiClient.post).toHaveBeenCalledWith('/api/totem/booking-handoffs/claim', { handoffToken: 'H' })
})

test('resolveHandoff posts the handoff token to the customer resolve endpoint', async () => {
  vi.mocked(apiClient.post).mockResolvedValue({
    handoffId: 'h1', professionalId: 'p1', professionalName: 'Ana', profession: 'Fisio', expiresAt: 'x',
  })
  await customerApi.resolveHandoff('H')
  expect(apiClient.post).toHaveBeenCalledWith('/api/customer/booking-handoffs/resolve', { handoffToken: 'H' })
})

test('createReservation forwards an optional handoffToken in the body', async () => {
  await customerApi.createReservation({
    professionalId: 'p1', startAt: '2026-09-10T10:00:00Z', endAt: '2026-09-10T11:00:00Z', handoffToken: 'H',
  })
  expect(apiClient.post).toHaveBeenCalledWith('/api/customer/reservations', expect.objectContaining({ handoffToken: 'H' }))
})

test('createReservation still works without a handoffToken', async () => {
  await customerApi.createReservation({
    professionalId: 'p1', startAt: '2026-09-10T10:00:00Z', endAt: '2026-09-10T11:00:00Z',
  })
  expect(apiClient.post).toHaveBeenCalledWith('/api/customer/reservations', {
    professionalId: 'p1', startAt: '2026-09-10T10:00:00Z', endAt: '2026-09-10T11:00:00Z',
  })
})

test('professionalReservationsApi.list threads optional from/to into the query string', async () => {
  const signal = new AbortController().signal
  await professionalReservationsApi.list(
    { status: 'APPROVED', page: 1, pageSize: 20, from: '2026-09-01', to: '2026-09-30' },
    signal,
  )
  expect(apiClient.get).toHaveBeenCalledWith('/api/professional/reservations', {
    query: { status: 'APPROVED', page: 1, pageSize: 20, from: '2026-09-01', to: '2026-09-30' }, signal,
  })
})

test('HandoffStatusDto narrows on its status discriminant', () => {
  // Type-level check: roomName is only reachable inside a COMPLETED guard.
  const roomOf = (dto: HandoffStatusDto): string | null => {
    if (dto.status === 'COMPLETED') return dto.roomName
    return null
  }
  expect(roomOf({ status: 'COMPLETED', professionalName: 'Ana', startAt: 'x', roomName: 'Sala 1' })).toBe('Sala 1')
  expect(roomOf({ status: 'PENDING', expiresAt: 'x' })).toBeNull()
  expect(roomOf({ status: 'EXPIRED' })).toBeNull()
})
