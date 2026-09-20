import { noRoomFeatures } from '../../test/roomFixtures'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { RoomPhotoManager } from './RoomPhotoManager'
import { roomPhotosApi, type RoomDto } from '../../api/modules'

vi.mock('../../api/modules', () => ({
  roomPhotosApi: { list: vi.fn(), upload: vi.fn(), remove: vi.fn(), reorder: vi.fn(), setCover: vi.fn() },
}))

const room: RoomDto = {
  id: 'room-1', name: 'Sala 101', description: null, hourlyRate: 100, dailyRate: 800,
  isActive: true, createdAt: '2026-09-05T00:00:00Z', updatedAt: '2026-09-05T00:00:00Z', concurrencyToken: 'rv', ...noRoomFeatures,
}

const photo = (id: string, sortOrder: number, isCover = false) =>
  ({ id, photoUrl: `/api/admin/rooms/room-1/photos/${id}`, sortOrder, isCover, createdAt: '2026-09-05T00:00:00Z' })

beforeEach(() => {
  vi.clearAllMocks()
})

test('shows a loading state, then an error with retry when the initial load fails', async () => {
  vi.mocked(roomPhotosApi.list).mockRejectedValueOnce(new Error('Falhou'))
  vi.mocked(roomPhotosApi.list).mockResolvedValueOnce([])
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  expect(screen.getByRole('status')).toHaveTextContent(/carregando/i)
  expect(await screen.findByText('Falhou')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }))
  await waitFor(() => expect(roomPhotosApi.list).toHaveBeenCalledTimes(2))
  expect(await screen.findByText(/nenhuma foto/i)).toBeInTheDocument()
})

test('shows an empty state when the room has no photos', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([])
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  expect(await screen.findByText(/nenhuma foto/i)).toBeInTheDocument()
  expect(screen.getByText('0/8 fotos')).toBeInTheDocument()
})

test('renders photos ordered by sortOrder with a cover badge and counter', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([photo('photo-2', 1), photo('photo-1', 0, true)])
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  const images = await screen.findAllByRole('img')
  expect(images).toHaveLength(2)
  expect(images[0]).toHaveAttribute('src', '/api/admin/rooms/room-1/photos/photo-1')
  expect(images[1]).toHaveAttribute('src', '/api/admin/rooms/room-1/photos/photo-2')
  expect(screen.getByText('Capa')).toBeInTheDocument()
  expect(screen.getByText('2/8 fotos')).toBeInTheDocument()
})

test('selecting a file uploads it and reloads the list', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([])
  vi.mocked(roomPhotosApi.upload).mockResolvedValue(photo('photo-1', 0))
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  await screen.findByText(/nenhuma foto/i)
  const input = screen.getByLabelText(/adicionar foto/i) as HTMLInputElement
  const file = new File(['x'], 'foto.png', { type: 'image/png' })
  fireEvent.change(input, { target: { files: [file] } })
  await waitFor(() => expect(roomPhotosApi.upload).toHaveBeenCalledWith('room-1', file))
  await waitFor(() => expect(roomPhotosApi.list).toHaveBeenCalledTimes(2))
})

test('disables the upload input and explains once the 8-photo limit is reached', async () => {
  const photos = Array.from({ length: 8 }, (_, index) => photo(`photo-${index}`, index))
  vi.mocked(roomPhotosApi.list).mockResolvedValue(photos)
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  expect(await screen.findByText('8/8 fotos')).toBeInTheDocument()
  expect(screen.getByLabelText(/adicionar foto/i)).toBeDisabled()
  expect(screen.getByText(/remova uma foto/i)).toBeInTheDocument()
})

test('removing a photo requires an explicit confirmation and then reloads', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([photo('photo-1', 0)])
  vi.mocked(roomPhotosApi.remove).mockResolvedValue(undefined)
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  await screen.findAllByRole('img')
  fireEvent.click(screen.getByRole('button', { name: 'Remover foto 1' }))
  expect(roomPhotosApi.remove).not.toHaveBeenCalled()
  expect(screen.getByRole('button', { name: /cancelar remoção da foto 1/i })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /confirmar remoção da foto 1/i }))
  await waitFor(() => expect(roomPhotosApi.remove).toHaveBeenCalledWith('room-1', 'photo-1'))
  await waitFor(() => expect(roomPhotosApi.list).toHaveBeenCalledTimes(2))
})

test('cancelling the remove confirmation does not call the API', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([photo('photo-1', 0)])
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  await screen.findAllByRole('img')
  fireEvent.click(screen.getByRole('button', { name: 'Remover foto 1' }))
  fireEvent.click(screen.getByRole('button', { name: /cancelar remoção da foto 1/i }))
  expect(roomPhotosApi.remove).not.toHaveBeenCalled()
  expect(screen.getByRole('button', { name: 'Remover foto 1' })).toBeInTheDocument()
})

test('setting a photo as cover calls the endpoint and reloads', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([photo('photo-1', 0), photo('photo-2', 1, true)])
  vi.mocked(roomPhotosApi.setCover).mockResolvedValue(undefined)
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  await screen.findAllByRole('img')
  fireEvent.click(screen.getByRole('button', { name: 'Definir foto 1 como capa' }))
  await waitFor(() => expect(roomPhotosApi.setCover).toHaveBeenCalledWith('room-1', 'photo-1'))
  await waitFor(() => expect(roomPhotosApi.list).toHaveBeenCalledTimes(2))
})

test('moving a photo before its neighbor sends the complete id order once', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([photo('photo-1', 0), photo('photo-2', 1)])
  vi.mocked(roomPhotosApi.reorder).mockResolvedValue(undefined)
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  fireEvent.click(await screen.findByRole('button', { name: 'Mover foto 2 para antes' }))
  await waitFor(() => expect(roomPhotosApi.reorder)
    .toHaveBeenCalledWith('room-1', ['photo-2', 'photo-1']))
  expect(roomPhotosApi.reorder).toHaveBeenCalledTimes(1)
  await waitFor(() => expect(roomPhotosApi.list).toHaveBeenCalledTimes(2))
})

test('moving a photo after its neighbor sends the complete id order once', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([photo('photo-1', 0), photo('photo-2', 1)])
  vi.mocked(roomPhotosApi.reorder).mockResolvedValue(undefined)
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  fireEvent.click(await screen.findByRole('button', { name: 'Mover foto 1 para depois' }))
  await waitFor(() => expect(roomPhotosApi.reorder)
    .toHaveBeenCalledWith('room-1', ['photo-2', 'photo-1']))
  expect(roomPhotosApi.reorder).toHaveBeenCalledTimes(1)
})

test('disables the move buttons at the extremes of the list', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([photo('photo-1', 0), photo('photo-2', 1)])
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  await screen.findAllByRole('img')
  expect(screen.getByRole('button', { name: 'Mover foto 1 para antes' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Mover foto 2 para depois' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Mover foto 1 para depois' })).not.toBeDisabled()
  expect(screen.getByRole('button', { name: 'Mover foto 2 para antes' })).not.toBeDisabled()
})

test.each([
  ['INVALID_ROOM_PHOTO', 'Arquivo inválido.'],
  ['ROOM_PHOTO_LIMIT_REACHED', 'Limite de 8 fotos atingido.'],
  ['PHOTO_UNAVAILABLE', 'Foto indisponível.'],
])('surfaces %s inline instead of swallowing it', async (code, message) => {
  const { ApiError } = await import('../../api/client')
  vi.mocked(roomPhotosApi.list).mockResolvedValue([photo('photo-1', 0)])
  vi.mocked(roomPhotosApi.upload).mockRejectedValue(new ApiError(400, code, message))
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  await screen.findAllByRole('img')
  const input = screen.getByLabelText(/adicionar foto/i)
  fireEvent.change(input, { target: { files: [new File(['x'], 'a.png', { type: 'image/png' })] } })
  expect(await screen.findByText(message)).toBeInTheDocument()
})

test('disables mutation buttons while a mutation is in flight', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([photo('photo-1', 0), photo('photo-2', 1)])
  let resolveSetCover: (() => void) | undefined
  vi.mocked(roomPhotosApi.setCover).mockImplementation(() => new Promise(resolve => { resolveSetCover = () => resolve(undefined) }))
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  await screen.findAllByRole('img')
  fireEvent.click(screen.getByRole('button', { name: 'Definir foto 1 como capa' }))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Mover foto 2 para antes' })).toBeDisabled())
  expect(screen.getByRole('button', { name: 'Remover foto 2' })).toBeDisabled()
  resolveSetCover?.()
  await waitFor(() => expect(roomPhotosApi.list).toHaveBeenCalledTimes(2))
})

test('never displays the storage key or file id, only the photo url and metadata', async () => {
  vi.mocked(roomPhotosApi.list).mockResolvedValue([photo('photo-1', 0)])
  render(<RoomPhotoManager room={room} onClose={vi.fn()} />)
  await screen.findAllByRole('img')
  expect(screen.queryByText(/storage/i)).not.toBeInTheDocument()
  expect(screen.queryByText('photo-1')).not.toBeInTheDocument()
})
