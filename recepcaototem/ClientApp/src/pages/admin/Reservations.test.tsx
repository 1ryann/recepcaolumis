import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { professionalReservationsApi, professionalsApi, reservationsApi, roomsApi } from '../../api/modules'
import { useSession } from '../../auth/SessionProvider'
import { Reservations } from './Reservations'

vi.mock('../../api/modules', () => ({
  reservationsApi: {
    list: vi.fn(), detail: vi.fn(), create: vi.fn(), approve: vi.fn(), reject: vi.fn(),
    reschedule: vi.fn(), cancel: vi.fn(),
  },
  professionalReservationsApi: {
    list: vi.fn(), detail: vi.fn(), create: vi.fn(), requestReschedule: vi.fn(), requestCancellation: vi.fn(),
  },
  professionalsApi: { list: vi.fn() }, roomsApi: { list: vi.fn() },
}))
vi.mock('../../auth/SessionProvider', () => ({ useSession: vi.fn() }))

const reservation = {
  id: 'res-1', roomId: 'room-1', roomName: 'Sala Norte', professionalId: 'pro-1',
  professionalName: 'Ana Lima', originalReservationId: null, kind: 'NEW' as const,
  status: 'PENDING' as const, startAt: '2026-09-07T10:00:00Z', endAt: '2026-09-07T11:00:00Z',
  requestedAt: '2026-09-06T10:00:00Z', decidedAt: null, rejectionReason: null,
  createdAt: '2026-09-06T10:00:00Z', updatedAt: '2026-09-06T10:00:00Z', concurrencyToken: 'rv1',
}
const page = { items: [reservation], page: 1, pageSize: 20, totalCount: 1 }
const rooms = { items: [{ id: 'room-1', name: 'Sala Norte', isActive: true }], page: 1, pageSize: 100, totalCount: 1 }
const professionals = { items: [{ id: 'pro-1', name: 'Ana Lima', isActive: true }], page: 1, pageSize: 100, totalCount: 1 }

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(useSession).mockReturnValue({ user: { roles: ['GERENTE'] } } as ReturnType<typeof useSession>)
  vi.mocked(reservationsApi.list).mockResolvedValue(page)
  vi.mocked(reservationsApi.approve).mockResolvedValue({ ...reservation, status: 'APPROVED', concurrencyToken: 'rv2' })
  vi.mocked(reservationsApi.reject).mockResolvedValue({ ...reservation, status: 'REJECTED', rejectionReason: 'Indisponível' })
  vi.mocked(roomsApi.list).mockResolvedValue(rooms as Awaited<ReturnType<typeof roomsApi.list>>)
  vi.mocked(professionalsApi.list).mockResolvedValue(professionals as Awaited<ReturnType<typeof professionalsApi.list>>)
  vi.mocked(professionalReservationsApi.list).mockResolvedValue(page)
  vi.mocked(professionalReservationsApi.requestCancellation).mockResolvedValue({
    ...reservation, id: 'request-2', kind: 'CANCELLATION', status: 'PENDING', originalReservationId: 'res-1',
  })
})

test('operations list, filter and approve pending reservations through the API', async () => {
  render(<Reservations />)
  expect(screen.getByRole('status')).toHaveTextContent('Carregando reservas')
  expect(await screen.findByRole('button', { name: /Aprovar reserva de Ana Lima/i })).toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Status das reservas'), { target: { value: 'PENDING' } })
  await waitFor(() => expect(reservationsApi.list).toHaveBeenLastCalledWith(
    expect.objectContaining({ status: 'PENDING', page: 1, pageSize: 20 }), expect.any(AbortSignal)))

  fireEvent.click(screen.getByRole('button', { name: /Aprovar reserva de Ana Lima/i }))
  await waitFor(() => expect(reservationsApi.approve).toHaveBeenCalledWith('res-1', 'rv1'))
})

test('operations reject pending reservations with a required reason', async () => {
  render(<Reservations />)
  await screen.findByRole('button', { name: /Recusar reserva de Ana Lima/i })
  fireEvent.click(screen.getByRole('button', { name: /Recusar reserva de Ana Lima/i }))
  fireEvent.change(screen.getByLabelText('Motivo da recusa'), { target: { value: 'Indisponível' } })
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar recusa' }))
  await waitFor(() => expect(reservationsApi.reject).toHaveBeenCalledWith('res-1', 'Indisponível', 'rv1'))
})

test('operations can open the real creation form with room and professional resources', async () => {
  render(<Reservations />)
  fireEvent.click(await screen.findByRole('button', { name: /Nova reserva/i }))
  expect(await screen.findByLabelText('Profissional')).toBeInTheDocument()
  expect(screen.getByLabelText('Sala')).toBeInTheDocument()
  expect(professionalsApi.list).toHaveBeenCalled()
  expect(roomsApi.list).toHaveBeenCalled()
})

test('professional mode uses only owned reservation endpoints and exposes request actions', async () => {
  vi.mocked(useSession).mockReturnValue({ user: { roles: ['PROFISSIONAL'] } } as ReturnType<typeof useSession>)
  vi.mocked(professionalReservationsApi.list).mockResolvedValue({
    ...page, items: [{ ...reservation, status: 'APPROVED' }],
  })
  render(<Reservations />)

  expect(await screen.findByText('Sala Norte')).toBeInTheDocument()
  expect(reservationsApi.list).not.toHaveBeenCalled()
  expect(screen.getByRole('button', { name: /Solicitar reserva/i })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /Solicitar remarcação de Sala Norte/i })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /Solicitar cancelamento de Sala Norte/i }))
  await waitFor(() => expect(professionalReservationsApi.requestCancellation).toHaveBeenCalledWith('res-1', 'rv1'))
})
