import { beforeEach, expect, test, vi } from 'vitest'
import { apiClient } from './client'
import { dashboardApi, receptionApi } from './modules'
import type { DashboardSnapshotDto, ReceptionProfessionalDto } from './modules'

vi.mock('./client', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), putMultipart: vi.fn() },
}))

beforeEach(() => vi.clearAllMocks())

const snapshot: DashboardSnapshotDto = {
  operationalDate: '2026-09-11',
  counts: {
    activeProfessionals: 4, activeRooms: 3, occupiedRooms: 1, reservedRooms: 1,
    activeLeases: 2, scheduledLeases: 1, pendingReservations: 2, todayReservations: 5,
    waitingVisits: 1, inServiceVisits: 1, todayCheckIns: 3,
  },
  financial: { pendingAmount: 100, overdueAmount: 0, paidAmount: 500, pendingCount: 1, overdueCount: 0, paidCount: 4 },
  alerts: { total: 0, warning: 0, critical: 0, recent: [] },
  agenda: [],
  currentVisits: [],
  rooms: [],
}

test('dashboardApi.get reads the admin dashboard snapshot with an optional AbortSignal', async () => {
  const signal = new AbortController().signal
  vi.mocked(apiClient.get).mockResolvedValue(snapshot)
  const result = await dashboardApi.get(signal)
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/dashboard', { signal })
  expect(result).toBe(snapshot)
})

test('dashboardApi.get works without a signal', async () => {
  vi.mocked(apiClient.get).mockResolvedValue(snapshot)
  await dashboardApi.get()
  expect(apiClient.get).toHaveBeenCalledWith('/api/admin/dashboard', { signal: undefined })
})

const professional: ReceptionProfessionalDto = {
  professionalId: 'p1', name: 'Ana', profession: 'Fisioterapeuta', description: null,
  hasPhoto: false, photoUrl: null, operationalStatus: 'AVAILABLE',
  currentRoomId: null, currentVisitId: null, waitingVisitorsCount: 0,
  nextReservationAt: null, canReceiveVisitor: true, presence: 'PRESENT', absentUntil: null,
}

test('receptionApi.professionals reads the reception professionals list with an optional AbortSignal', async () => {
  const signal = new AbortController().signal
  vi.mocked(apiClient.get).mockResolvedValue([professional])
  const result = await receptionApi.professionals(signal)
  expect(apiClient.get).toHaveBeenCalledWith('/api/reception/professionals', { signal })
  expect(result).toEqual([professional])
})

test('receptionApi.professionals works without a signal', async () => {
  vi.mocked(apiClient.get).mockResolvedValue([])
  await receptionApi.professionals()
  expect(apiClient.get).toHaveBeenCalledWith('/api/reception/professionals', { signal: undefined })
})
