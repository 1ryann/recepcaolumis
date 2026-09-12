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
  customerName: null,
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

// Agenda de hoje: reservation stays APPROVED but its linked Visit was separately cancelled (e.g. a no-show cancelled at the totem).
const agendaCancelledVisit = reservation({ id: 'r-cancelled-visit', roomName: 'Sala 4', startAt: '2026-09-11T17:00:00Z', endAt: '2026-09-11T18:00:00Z' })
const cancelledVisit = visit({ id: 'v-cancelled', reservationId: 'r-cancelled-visit', status: 'CANCELLED', visitorName: 'Beltrano' })

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
    if (query.status === 'APPROVED') return { items: [], page: 1, pageSize: 10, totalCount: 0 }
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

test('Agenda de hoje renders Cancelado when the matched Visit was cancelled, even though the reservation itself stays APPROVED', async () => {
  vi.mocked(professionalReservationsApi.list).mockImplementation(async (query: ReservationListQuery) => {
    if (query.status === 'all') return { items: [agendaCancelledVisit], page: 1, pageSize: 100, totalCount: 1 }
    if (query.status === 'PENDING') return { items: [], page: 1, pageSize: 100, totalCount: 0 }
    if (query.status === 'APPROVED') return { items: [], page: 1, pageSize: 10, totalCount: 0 }
    throw new Error(`unexpected reservations query ${JSON.stringify(query)}`)
  })
  vi.mocked(professionalVisitsApi.list).mockImplementation(async (query: VisitListQuery) => {
    if (query.status === 'IN_SERVICE') return { items: [], page: 1, pageSize: 1, totalCount: 0 }
    if (query.status === 'ENDED') return { items: [], page: 1, pageSize: 1, totalCount: 0 }
    if (query.status === 'all' && query.pageSize === 1) return { items: [], page: 1, pageSize: 1, totalCount: 0 }
    if (query.status === 'all' && query.pageSize === 100) return { items: [cancelledVisit], page: 1, pageSize: 100, totalCount: 1 }
    throw new Error(`unexpected visits query ${JSON.stringify(query)}`)
  })
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  const agenda = await screen.findByTestId('professional-agenda-today')
  expect(within(agenda).getByText('Cancelado')).toBeInTheDocument()
})

test('Próximo horário uses the dedicated ascending, from-now reservations fetch, not the (possibly truncated) context list', async () => {
  vi.mocked(professionalReservationsApi.list).mockImplementation(async (query: ReservationListQuery) => {
    if (query.status === 'all') return { items: [agendaScheduled, agendaEnded], page: 1, pageSize: 100, totalCount: 2 }
    if (query.status === 'PENDING') return { items: [pendingReschedule, pendingCancellation, pendingNew], page: 1, pageSize: 100, totalCount: 3 }
    if (query.status === 'APPROVED') return { items: [upcomingApproved], page: 1, pageSize: 10, totalCount: 1 }
    throw new Error(`unexpected reservations query ${JSON.stringify(query)}`)
  })
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  const kpi = await screen.findByTestId('professional-kpi-next')
  expect(within(kpi).getByText('Sala 1')).toBeInTheDocument()
})

test('Próximo horário and Próximas reservas show the earliest appointment even when the professional has more future reservations than the page size (no silent truncation to a far-future page)', async () => {
  const earliest = reservation({ id: 'r-earliest', roomName: 'Sala Cedo', startAt: '2026-09-12T09:00:00Z', endAt: '2026-09-12T10:00:00Z' })
  const later = reservation({ id: 'r-later', roomName: 'Sala Tarde', startAt: '2026-09-20T09:00:00Z', endAt: '2026-09-20T10:00:00Z' })
  let capturedQuery: ReservationListQuery | undefined
  vi.mocked(professionalReservationsApi.list).mockImplementation(async (query: ReservationListQuery) => {
    if (query.status === 'all') return { items: [agendaScheduled, agendaEnded], page: 1, pageSize: 100, totalCount: 2 }
    if (query.status === 'PENDING') return { items: [pendingReschedule, pendingCancellation, pendingNew], page: 1, pageSize: 100, totalCount: 3 }
    if (query.status === 'APPROVED') {
      capturedQuery = query
      // Simulate the backend honoring orderBy=asc: the earliest reservation comes first,
      // proving the dashboard no longer relies on a 50-row furthest-future-first page.
      return { items: [earliest, later], page: 1, pageSize: 10, totalCount: 200 }
    }
    throw new Error(`unexpected reservations query ${JSON.stringify(query)}`)
  })
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  const kpi = await screen.findByTestId('professional-kpi-next')
  expect(within(kpi).getByText('Sala Cedo')).toBeInTheDocument()
  expect(within(kpi).queryByText('Sala Tarde')).not.toBeInTheDocument()
  expect(await screen.findAllByText('Sala Cedo')).not.toHaveLength(0)
  expect((capturedQuery as unknown as { orderBy?: string })?.orderBy).toBe('asc')
})

test('Disponibilidade reflects today\'s effective interval total from the availability API', async () => {
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  expect(await screen.findByText(/8h/)).toBeInTheDocument()
})

test('Resumo/avisos counts only PENDING reservations with kind RESCHEDULE or CANCELLATION', async () => {
  renderDashboard({ reservations: [], visits: [], loading: false, error: '' })
  expect(await screen.findByText('2')).toBeInTheDocument()
})
