import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import {
  professionalAvailabilityApi, professionalReservationsApi, professionalVisitsApi,
  type ProfessionalAvailabilityDto, type ReservationDto, type ReservationListQuery, type VisitDto, type VisitListQuery,
} from '../../api/modules'
import { ProfessionalDashboard } from './ProfessionalHome'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalReservationsApi: { list: vi.fn() },
  professionalVisitsApi: { list: vi.fn() },
  professionalAvailabilityApi: { get: vi.fn() },
}))

const reservation = (overrides: Partial<ReservationDto>): ReservationDto => ({
  id: 'r0', roomId: 'room-1', roomName: 'Sala 1', professionalId: 'p1', professionalName: 'Helena',
  originalReservationId: null, kind: 'NEW', status: 'APPROVED',
  startAt: '2026-09-11T13:00:00Z', endAt: '2026-09-11T14:00:00Z',
  requestedAt: '2026-09-01T00:00:00Z', decidedAt: null, rejectionReason: null,
  createdAt: '2026-09-01T00:00:00Z', updatedAt: '2026-09-01T00:00:00Z', concurrencyToken: 'v1',
  ...overrides,
})

const visit = (overrides: Partial<VisitDto>): VisitDto => ({
  id: 'v0', professionalId: 'p1', professionalName: 'Helena', roomId: 'room-1', roomName: 'Sala 1',
  reservationId: null, visitorName: 'Fulano', status: 'WAITING', arrivedAt: '2026-09-11T13:00:00Z',
  serviceStartedAt: null, endedAt: null, cancelledAt: null,
  createdAt: '2026-09-11T13:00:00Z', updatedAt: '2026-09-11T13:00:00Z', concurrencyToken: 'v1', history: [],
  ...overrides,
})

const availability: ProfessionalAvailabilityDto = {
  mode: 'CUSTOM',
  days: [],
  effectiveDays: [
    { dayOfWeek: 'FRIDAY', intervals: [{ startTime: '08:00', endTime: '12:00' }, { startTime: '13:00', endTime: '17:00' }] },
  ],
  globalDays: [],
  concurrencyToken: 'v1',
  existingReservationsOutsideAvailabilityCount: 0,
}

// Agenda de hoje: one reservation with no matching visit ("Agendado"), one whose visit ENDED ("Concluído").
const agendaScheduled = reservation({ id: 'r-scheduled', roomName: 'Sala 2', startAt: '2026-09-11T13:00:00Z', endAt: '2026-09-11T14:00:00Z' })
const agendaEnded = reservation({ id: 'r-ended', roomName: 'Sala 3', startAt: '2026-09-11T15:00:00Z', endAt: '2026-09-11T16:00:00Z' })
const endedVisit = visit({ id: 'v-ended', reservationId: 'r-ended', status: 'ENDED', visitorName: 'Ciclano' })

const pendingReschedule = reservation({ id: 'r-pending-1', kind: 'RESCHEDULE', status: 'PENDING' })
const pendingCancellation = reservation({ id: 'r-pending-2', kind: 'CANCELLATION', status: 'PENDING' })
const pendingNew = reservation({ id: 'r-pending-3', kind: 'NEW', status: 'PENDING' })

const upcomingApproved = reservation({ id: 'r-upcoming', startAt: '2026-09-12T13:00:00Z', endAt: '2026-09-12T14:00:00Z', status: 'APPROVED' })

function renderDashboard(context: { reservations: ReservationDto[], visits: VisitDto[], loading: boolean, error: string }) {
  return render(
    <MemoryRouter initialEntries={['/profissional']}>
      <Routes>
        <Route path="/profissional" element={<Outlet context={context} />}>
          <Route index element={<ProfessionalDashboard />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.setSystemTime(new Date('2026-09-11T18:00:00Z'))
  vi.mocked(professionalVisitsApi.list).mockImplementation(async (query: VisitListQuery) => {
    if (query.status === 'IN_SERVICE') return { items: [], page: 1, pageSize: 1, totalCount: 3 }
    if (query.status === 'ENDED') return { items: [], page: 1, pageSize: 1, totalCount: 2 }
    if (query.status === 'all' && query.pageSize === 1) return { items: [], page: 1, pageSize: 1, totalCount: 7 }
    if (query.status === 'all' && query.pageSize === 100) return { items: [endedVisit], page: 1, pageSize: 100, totalCount: 1 }
    throw new Error(`unexpected visits query ${JSON.stringify(query)}`)
  })
  vi.mocked(professionalReservationsApi.list).mockImplementation(async (query: ReservationListQuery) => {
    if (query.status === 'all') return { items: [agendaScheduled, agendaEnded], page: 1, pageSize: 100, totalCount: 2 }
    if (query.status === 'PENDING') return { items: [pendingReschedule, pendingCancellation, pendingNew], page: 1, pageSize: 100, totalCount: 3 }
    throw new Error(`unexpected reservations query ${JSON.stringify(query)}`)
  })
  vi.mocked(professionalAvailabilityApi.get).mockResolvedValue(availability)
})

afterEach(() => { vi.useRealTimers() })

test('Atendimentos hoje reads totalCount, not the (empty) items array', async () => {
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  expect(await screen.findByText('5')).toBeInTheDocument()
})

test('Check-ins confirmados hoje reads totalCount, not the (empty) items array', async () => {
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  expect(await screen.findByText('7')).toBeInTheDocument()
})

test('no KPI ever renders 0 or 1 derived from a truncated items array when totalCount says otherwise', async () => {
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  await screen.findByText('7')
  expect(screen.queryByText('0')).not.toBeInTheDocument()
})

test('Agenda de hoje derives status by joining reservations to visits: matched ENDED visit renders Concluído, unmatched renders Agendado', async () => {
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  const agenda = await screen.findByTestId('professional-agenda-today')
  expect(within(agenda).getByText('Concluído')).toBeInTheDocument()
  expect(within(agenda).getByText('Agendado')).toBeInTheDocument()
})

test('Próximo horário uses the next APPROVED reservation from context with endAt in the future', async () => {
  renderDashboard({ reservations: [upcomingApproved], visits: [], loading: false, error: '' })
  const kpi = await screen.findByTestId('professional-kpi-next')
  expect(within(kpi).getByText('Sala 1')).toBeInTheDocument()
})

test('Disponibilidade reflects today\'s effective interval total from the availability API', async () => {
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  expect(await screen.findByText(/8h/)).toBeInTheDocument()
})

test('Resumo/avisos counts only PENDING reservations with kind RESCHEDULE or CANCELLATION', async () => {
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  expect(await screen.findByText('2')).toBeInTheDocument()
})
