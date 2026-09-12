import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalReservationsApi } from '../../api/modules'
import { ProfessionalReservations } from './ProfessionalReservations'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalReservationsApi: { list: vi.fn(), detail: vi.fn(), requestReschedule: vi.fn(), requestCancellation: vi.fn() },
}))

const reservation = {
  id: 'r1', roomId: 'room1', roomName: 'Sala 1', professionalId: 'p1', professionalName: 'Maria',
  originalReservationId: null, kind: 'NEW' as const, status: 'APPROVED' as const,
  startAt: '2026-09-15T13:00:00Z', endAt: '2026-09-15T14:00:00Z', requestedAt: '2026-09-10T00:00:00Z',
  decidedAt: '2026-09-10T00:00:00Z', rejectionReason: null, createdAt: '2026-09-10T00:00:00Z',
  updatedAt: '2026-09-10T00:00:00Z', concurrencyToken: 'tok-1',
}

beforeEach(() => {
  vi.mocked(professionalReservationsApi.list).mockResolvedValue({ items: [reservation], page: 1, pageSize: 20, totalCount: 1 })
})

test('lists reservations and shows a cancel action but no approve/reject controls', async () => {
  render(<ProfessionalReservations />)
  expect(await screen.findByText('Sala 1')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /solicitar cancelamento/i })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /aprovar/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /recusar/i })).not.toBeInTheDocument()
})

test('cancel action calls requestCancellation with the concurrency token', async () => {
  vi.mocked(professionalReservationsApi.requestCancellation).mockResolvedValue({ ...reservation, status: 'CANCELLED' })
  render(<ProfessionalReservations />)
  fireEvent.click(await screen.findByRole('button', { name: /solicitar cancelamento/i }))
  fireEvent.click(screen.getByRole('button', { name: /confirmar/i }))
  await waitFor(() => expect(professionalReservationsApi.requestCancellation).toHaveBeenCalledWith('r1', 'tok-1'))
})
