import { render, screen, fireEvent } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalReservationsApi, professionalVisitsApi } from '../../api/modules'
import { ProfessionalAgenda } from './ProfessionalAgenda'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalReservationsApi: { list: vi.fn() },
  professionalVisitsApi: { list: vi.fn() },
}))

const baseReservation = {
  id: 'r1', roomId: 'room1', roomName: 'Sala 1', professionalId: 'p1', professionalName: 'Maria',
  originalReservationId: null, kind: 'NEW' as const, status: 'APPROVED' as const, startAt: '2026-09-12T13:00:00Z',
  endAt: '2026-09-12T14:00:00Z', requestedAt: '2026-09-01T00:00:00Z', decidedAt: '2026-09-01T00:00:00Z',
  rejectionReason: null, createdAt: '2026-09-01T00:00:00Z', updatedAt: '2026-09-01T00:00:00Z', concurrencyToken: 'tok',
}

beforeEach(() => {
  vi.clearAllMocks()
})

test('Hoje view shows the real linked customer name when present', async () => {
  vi.mocked(professionalReservationsApi.list).mockResolvedValue({
    items: [{ ...baseReservation, customerName: 'Ana Beatriz' }], page: 1, pageSize: 50, totalCount: 1,
  })
  vi.mocked(professionalVisitsApi.list).mockResolvedValue({ items: [], page: 1, pageSize: 50, totalCount: 0 })
  render(<ProfessionalAgenda />)
  expect(await screen.findByText('Sala 1')).toBeInTheDocument()
  expect(screen.getByText('Ana Beatriz')).toBeInTheDocument()
  expect(screen.getByText('Agendado')).toBeInTheDocument()
})

test('Hoje view falls back to the Visit.visitorName when the reservation has no linked customer but a visit already exists', async () => {
  vi.mocked(professionalReservationsApi.list).mockResolvedValue({
    items: [{ ...baseReservation, customerName: null }], page: 1, pageSize: 50, totalCount: 1,
  })
  vi.mocked(professionalVisitsApi.list).mockResolvedValue({
    items: [{ id: 'v1', professionalId: 'p1', professionalName: 'Maria', roomId: 'room1', roomName: 'Sala 1',
      reservationId: 'r1', visitorName: 'João Silva', status: 'WAITING', arrivedAt: '2026-09-12T12:55:00Z',
      serviceStartedAt: null, endedAt: null, cancelledAt: null, createdAt: '2026-09-12T12:55:00Z',
      updatedAt: '2026-09-12T12:55:00Z', concurrencyToken: 'tok2', history: [] }],
    page: 1, pageSize: 50, totalCount: 1,
  })
  render(<ProfessionalAgenda />)
  expect(await screen.findByText('João Silva')).toBeInTheDocument()
})

test('Hoje view shows an explicit absence marker, never a fabricated name, when neither a customer nor a visit is linked yet', async () => {
  vi.mocked(professionalReservationsApi.list).mockResolvedValue({
    items: [{ ...baseReservation, customerName: null }], page: 1, pageSize: 50, totalCount: 1,
  })
  vi.mocked(professionalVisitsApi.list).mockResolvedValue({ items: [], page: 1, pageSize: 50, totalCount: 0 })
  render(<ProfessionalAgenda />)
  expect(await screen.findByLabelText('Cliente não identificado')).toBeInTheDocument()
  expect(screen.queryByText('Cliente')).not.toBeInTheDocument() // the old generic-label workaround must never reappear
})

test('switching to Semana view re-fetches with a week-wide range', async () => {
  vi.mocked(professionalReservationsApi.list).mockResolvedValue({
    items: [{ ...baseReservation, customerName: 'Ana Beatriz' }], page: 1, pageSize: 50, totalCount: 1,
  })
  vi.mocked(professionalVisitsApi.list).mockResolvedValue({ items: [], page: 1, pageSize: 50, totalCount: 0 })
  render(<ProfessionalAgenda />)
  await screen.findByText('Sala 1')
  fireEvent.click(screen.getByRole('button', { name: /semana/i }))
  await vi.waitFor(() => expect(professionalReservationsApi.list).toHaveBeenCalledTimes(2))
})
