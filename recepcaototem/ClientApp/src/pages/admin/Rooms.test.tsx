import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { Rooms } from './Rooms'
import { roomsApi, roomPhotosApi } from '../../api/modules'
import { aRoomDto, noRoomFeatures } from '../../test/roomFixtures'

vi.mock('../../api/modules', () => ({
  roomsApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), changeStatus: vi.fn() },
  roomPhotosApi: { list: vi.fn(), upload: vi.fn(), remove: vi.fn(), reorder: vi.fn(), setCover: vi.fn() },
}))

const room = aRoomDto({
  name: 'Sala 101', description: 'Ambiente silencioso', hourlyRate: 100.99, dailyRate: 800,
  createdAt: '2026-09-05T00:00:00Z', updatedAt: '2026-09-05T00:00:00Z', concurrencyToken: 'room-token-1',
})

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(roomsApi.list).mockResolvedValue({ items: [room], page: 1, pageSize: 20, totalCount: 1 })
  vi.mocked(roomPhotosApi.list).mockResolvedValue([])
})

test('loads room cards from the API and does not render operational mock concepts', async () => {
  render(<Rooms />)
  expect(screen.getByRole('status')).toHaveTextContent('Carregando salas')
  expect(await screen.findByText('Sala 101')).toBeInTheDocument()
  expect(screen.getByText(/100,99/)).toBeInTheDocument()
  expect(roomsApi.list).toHaveBeenCalledWith({ search: undefined, status: 'all', page: 1, pageSize: 20 }, expect.any(AbortSignal))
  expect(screen.queryByText(/ocupada|disponível para locação|profissional responsável/i)).not.toBeInTheDocument()
})

test('sends strictly parsed monetary JSON numbers for create and update', async () => {
  vi.mocked(roomsApi.create).mockResolvedValue({ ...room, id: 'room-2', name: 'Sala 102' })
  vi.mocked(roomsApi.update).mockResolvedValue({ ...room, name: 'Sala 101 editada', concurrencyToken: 'room-token-2' })
  render(<Rooms />)
  await screen.findByText('Sala 101')
  fireEvent.click(screen.getByRole('button', { name: /Nova sala/i }))
  fireEvent.change(screen.getByLabelText('Nome da sala'), { target: { value: 'Sala 102' } })
  fireEvent.change(screen.getByLabelText('Tarifa por hora'), { target: { value: '0,10' } })
  fireEvent.change(screen.getByLabelText('Tarifa diária'), { target: { value: '100,99' } })
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar sala' }))
  await waitFor(() => expect(roomsApi.create).toHaveBeenCalledWith({
    name: 'Sala 102', description: null, hourlyRate: 0.1, dailyRate: 100.99, ...noRoomFeatures,
  }))
  fireEvent.click(screen.getByRole('button', { name: 'Editar Sala 101' }))
  fireEvent.change(screen.getByLabelText('Nome da sala'), { target: { value: 'Sala 101 editada' } })
  fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
  await waitFor(() => expect(roomsApi.update).toHaveBeenCalledWith('room-1', expect.objectContaining({ concurrencyToken: 'room-token-1' })))
})

test('keeps name conflict distinct from concurrency conflict and reloads the latter', async () => {
  const { ApiError } = await import('../../api/client')
  vi.mocked(roomsApi.changeStatus).mockRejectedValueOnce(new ApiError(409, 'ROOM_NAME_ALREADY_EXISTS', 'Nome duplicado'))
    .mockRejectedValueOnce(new ApiError(409, 'RESOURCE_MODIFIED', 'Conflito'))
  render(<Rooms />)
  await screen.findByText('Sala 101')
  fireEvent.click(screen.getByRole('button', { name: /Desativar Sala 101/i }))
  expect(await screen.findByText('Nome duplicado')).toBeInTheDocument()
  expect(roomsApi.list).toHaveBeenCalledTimes(1)
  fireEvent.click(screen.getByRole('button', { name: /Desativar Sala 101/i }))
  expect(await screen.findByText(/alterado por outra operação/i)).toBeInTheDocument()
  expect(roomsApi.list).toHaveBeenCalledTimes(2)
})

test('resets the server page on status and debounced search changes', async () => {
  vi.mocked(roomsApi.list)
    .mockResolvedValueOnce({ items: [room], page: 2, pageSize: 20, totalCount: 21 })
    .mockResolvedValueOnce({ items: [room], page: 1, pageSize: 20, totalCount: 1 })
    .mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  render(<Rooms />)
  await screen.findByText('Sala 101')
  fireEvent.change(screen.getByLabelText('Status das salas'), { target: { value: 'inactive' } })
  await waitFor(() => expect(roomsApi.list).toHaveBeenLastCalledWith({ search: undefined, status: 'inactive', page: 1, pageSize: 20 }, expect.any(AbortSignal)))
  fireEvent.change(screen.getByLabelText('Buscar salas'), { target: { value: '  sala  ' } })
  await waitFor(() => expect(roomsApi.list).toHaveBeenLastCalledWith({ search: 'sala', status: 'inactive', page: 1, pageSize: 20 }, expect.any(AbortSignal)), { timeout: 1000 })
})

test('opens the photo manager for a room and returns focus to the trigger on close', async () => {
  render(<Rooms />)
  await screen.findByText('Sala 101')
  const trigger = screen.getByRole('button', { name: 'Gerenciar fotos de Sala 101' })
  fireEvent.click(trigger)
  expect(await screen.findByText('Fotos — Sala 101')).toBeInTheDocument()
  await waitFor(() => expect(roomPhotosApi.list).toHaveBeenCalledWith('room-1'))
  fireEvent.click(screen.getByRole('button', { name: 'Fechar' }))
  await waitFor(() => expect(screen.queryByText('Fotos — Sala 101')).not.toBeInTheDocument())
  expect(trigger).toHaveFocus()
})
