import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { professionalVisitsApi, professionalsApi, reservationsApi, roomsApi, visitsApi } from '../../api/modules'
import { useSession } from '../../auth/SessionProvider'
import { Visits } from './Visits'

vi.mock('../../api/modules', () => ({
  visitsApi: { list: vi.fn(), detail: vi.fn(), create: vi.fn(), start: vi.fn(), end: vi.fn(), cancel: vi.fn(), correct: vi.fn() },
  professionalVisitsApi: { list: vi.fn(), detail: vi.fn(), start: vi.fn(), end: vi.fn(), cancel: vi.fn() },
  professionalsApi: { list: vi.fn() }, roomsApi: { list: vi.fn() }, reservationsApi: { list: vi.fn() },
}))
vi.mock('../../auth/SessionProvider', () => ({ useSession: vi.fn() }))

const visit = {
  id: 'v-1', professionalId: 'p-1', professionalName: 'Ana Lima', roomId: 'r-1', roomName: 'Sala Norte',
  reservationId: null, visitorName: 'Maria Silva', status: 'WAITING' as const,
  arrivedAt: '2026-09-06T14:00:00Z', serviceStartedAt: null, endedAt: null, cancelledAt: null,
  createdAt: '2026-09-06T14:00:00Z', updatedAt: '2026-09-06T14:00:00Z', concurrencyToken: 'vv1', history: [],
}
const page = { items: [visit], page: 1, pageSize: 20, totalCount: 1 }

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(useSession).mockReturnValue({ user: { roles: ['GERENTE'] } } as ReturnType<typeof useSession>)
  vi.mocked(visitsApi.list).mockResolvedValue(page)
  vi.mocked(visitsApi.start).mockResolvedValue({ ...visit, status: 'IN_SERVICE', concurrencyToken: 'vv2' })
  vi.mocked(professionalVisitsApi.list).mockResolvedValue(page)
  vi.mocked(professionalVisitsApi.start).mockResolvedValue({ ...visit, status: 'IN_SERVICE', concurrencyToken: 'vv2' })
  vi.mocked(professionalsApi.list).mockResolvedValue({ items: [{ id: 'p-1', name: 'Ana Lima', isActive: true }], page: 1, pageSize: 100, totalCount: 1 } as never)
  vi.mocked(roomsApi.list).mockResolvedValue({ items: [{ id: 'r-1', name: 'Sala Norte', isActive: true }], page: 1, pageSize: 100, totalCount: 1 } as never)
  vi.mocked(reservationsApi.list).mockResolvedValue({ items: [], page: 1, pageSize: 100, totalCount: 0 })
})

test('operations use real visit API filters and state actions', async () => {
  render(<Visits />)
  expect(await screen.findByText('Maria Silva')).toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Status das visitas'), { target: { value: 'WAITING' } })
  await waitFor(() => expect(visitsApi.list).toHaveBeenLastCalledWith(
    expect.objectContaining({ status: 'WAITING', page: 1, pageSize: 20 }), expect.any(AbortSignal)))
  fireEvent.click(screen.getByRole('button', { name: /Iniciar atendimento de Maria Silva/i }))
  await waitFor(() => expect(visitsApi.start).toHaveBeenCalledWith('v-1', 'vv1'))
})

test('operations can register an arrival with real resources', async () => {
  render(<Visits />)
  fireEvent.click(await screen.findByRole('button', { name: /Registrar chegada/i }))
  expect(screen.getByLabelText('Nome do visitante')).toBeInTheDocument()
  expect(screen.getByLabelText('Profissional')).toBeInTheDocument()
  expect(screen.getByLabelText('Sala')).toBeInTheDocument()
})

test('professional mode uses only owned endpoints and has no arrival registration', async () => {
  vi.mocked(useSession).mockReturnValue({ user: { roles: ['PROFISSIONAL'] } } as ReturnType<typeof useSession>)
  render(<Visits />)
  expect(await screen.findByText('Maria Silva')).toBeInTheDocument()
  expect(visitsApi.list).not.toHaveBeenCalled()
  expect(screen.queryByRole('button', { name: /Registrar chegada/i })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /Iniciar atendimento de Maria Silva/i }))
  await waitFor(() => expect(professionalVisitsApi.start).toHaveBeenCalledWith('v-1', 'vv1'))
})
