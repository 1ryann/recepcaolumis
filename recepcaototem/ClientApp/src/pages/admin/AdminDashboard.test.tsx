import { render, screen, within } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { dashboardApi, receptionApi } from '../../api/modules'
import type { DashboardSnapshotDto, ReceptionProfessionalDto } from '../../api/modules'
import { AdminDashboard } from './AdminDashboard'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  dashboardApi: { get: vi.fn() },
  receptionApi: { professionals: vi.fn() },
}))

const baseSnapshot: DashboardSnapshotDto = {
  operationalDate: '2026-09-11',
  counts: {
    activeProfessionals: 6, activeRooms: 5, occupiedRooms: 3, reservedRooms: 1,
    activeLeases: 4, scheduledLeases: 2, pendingReservations: 7, todayReservations: 12,
    waitingVisits: 2, inServiceVisits: 3, todayCheckIns: 9,
  },
  financial: { pendingAmount: 0, overdueAmount: 0, paidAmount: 0, pendingCount: 0, overdueCount: 0, paidCount: 0 },
  alerts: { total: 3, warning: 2, critical: 1, recent: [
    { id: 'a1', type: 'NEXT_RESERVATION_SOON', severity: 'WARNING', title: 'Reserva próxima', message: 'Sala 2 em 10 minutos',
      conditionAt: '2026-09-11T13:00:00Z', roomId: null, professionalId: null, reservationId: null, visitId: null, leaseId: null },
  ] },
  agenda: [],
  currentVisits: [
    { visitId: 'v1', visitorName: 'Fulano da Silva', status: 'IN_SERVICE', professionalId: 'p1', professionalName: 'Dra. Helena',
      roomId: 'r1', roomName: 'Sala 1', arrivedAt: '2026-09-11T12:00:00Z', serviceStartedAt: '2026-09-11T12:05:00Z', durationMinutes: 15 },
  ],
  rooms: [
    { roomId: 'r1', roomName: 'Sala 1', status: 'OCCUPIED', nextCommitmentAt: null },
    { roomId: 'r2', roomName: 'Sala 2', status: 'AVAILABLE', nextCommitmentAt: null },
  ],
}

const professional = (overrides: Partial<ReceptionProfessionalDto>): ReceptionProfessionalDto => ({
  professionalId: 'p1', name: 'Dra. Helena', profession: 'Fisioterapeuta', description: null,
  hasPhoto: false, photoUrl: null, operationalStatus: 'IN_SERVICE', currentRoomId: 'r1', currentVisitId: 'v1',
  waitingVisitorsCount: 0, nextReservationAt: null, canReceiveVisitor: false, presence: 'PRESENT', absentUntil: null,
  ...overrides,
})

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(dashboardApi.get).mockResolvedValue(baseSnapshot)
  vi.mocked(receptionApi.professionals).mockResolvedValue([professional({})])
})

test('renders the 4 KPI cards from counts verbatim, not a client-side computation', async () => {
  render(<AdminDashboard />)
  // todayReservations backs both "Atendimentos hoje" and "Reservas hoje" per the brief, so it appears twice.
  expect(await screen.findAllByText('12')).toHaveLength(2)
  expect(screen.getByText('9')).toBeInTheDocument() // todayCheckIns
  expect(screen.getByText('3/5')).toBeInTheDocument() // occupiedRooms/activeRooms
  expect(screen.getByText('7 aguardando aprovação')).toBeInTheDocument() // pendingReservations substat
})

test('Recepção · Em atendimento renders visitorName and professionalName from currentVisits', async () => {
  render(<AdminDashboard />)
  const panel = await screen.findByTestId('admin-current-visits')
  expect(within(panel).getByText('Fulano da Silva')).toBeInTheDocument()
  expect(within(panel).getByText('Dra. Helena')).toBeInTheDocument()
})

test('a room with status OCCUPIED renders the Em uso label', async () => {
  render(<AdminDashboard />)
  const panel = await screen.findByTestId('admin-room-status')
  expect(within(panel).getByText('Em uso')).toBeInTheDocument()
  expect(within(panel).getByText('Livre')).toBeInTheDocument()
})

test('the presence panel says presentes, never online', async () => {
  render(<AdminDashboard />)
  expect(await screen.findByText(/Profissionais presentes/i)).toBeInTheDocument()
  expect(screen.queryByText(/online/i)).not.toBeInTheDocument()
})

test('Profissionais presentes only lists professionals whose presence is PRESENT, never an absent one', async () => {
  vi.mocked(receptionApi.professionals).mockResolvedValue([
    professional({ professionalId: 'p1', name: 'Dra. Helena', presence: 'PRESENT' }),
    professional({ professionalId: 'p2', name: 'Dr. Ausente', presence: 'ABSENT' }),
  ])
  render(<AdminDashboard />)
  const panel = await screen.findByTestId('admin-professionals-present')
  expect(within(panel).getByText('Dra. Helena')).toBeInTheDocument()
  expect(within(panel).queryByText('Dr. Ausente')).not.toBeInTheDocument()
})

test('renders no fabricated rows when currentVisits, rooms, professionals and alerts are all empty', async () => {
  vi.mocked(dashboardApi.get).mockResolvedValue({
    ...baseSnapshot, currentVisits: [], rooms: [], alerts: { total: 0, warning: 0, critical: 0, recent: [] },
  })
  vi.mocked(receptionApi.professionals).mockResolvedValue([])
  render(<AdminDashboard />)
  const visits = await screen.findByTestId('admin-current-visits')
  expect(within(visits).getByText(/ninguém em atendimento/i)).toBeInTheDocument()
  const rooms = screen.getByTestId('admin-room-status')
  expect(within(rooms).getByText(/nenhuma sala/i)).toBeInTheDocument()
  const professionals = screen.getByTestId('admin-professionals-present')
  expect(within(professionals).getByText(/nenhum profissional presente/i)).toBeInTheDocument()
  const alerts = screen.getByTestId('admin-alerts')
  expect(within(alerts).getByText(/nenhum alerta/i)).toBeInTheDocument()
})
