import { noRoomFeatures } from '../../test/roomFixtures'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { operatingHoursApi, roomBlocksApi, roomsApi } from '../../api/modules'
import { Settings } from './Settings'

vi.mock('../../api/modules', async (original) => ({ ...await original<typeof import('../../api/modules')>(), operatingHoursApi: { get: vi.fn(), update: vi.fn() }, roomBlocksApi: { list: vi.fn(), create: vi.fn(), cancel: vi.fn() }, roomsApi: { list: vi.fn() } }))

const days = ['MONDAY', 'TUESDAY', 'WEDNESDAY', 'THURSDAY', 'FRIDAY', 'SATURDAY', 'SUNDAY'].map((dayOfWeek) => ({ dayOfWeek, intervals: dayOfWeek === 'MONDAY' ? [{ opensAt: '08:00', closesAt: '18:00' }] : [] }))

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(operatingHoursApi.get).mockResolvedValue({ configured: true, days, concurrencyToken: 'oh-1' })
  vi.mocked(roomsApi.list).mockResolvedValue({ items: [{ ...noRoomFeatures, id: 'room-1', name: 'Sala 1', description: null, hourlyRate: 50, dailyRate: 200, isActive: true, createdAt: '', updatedAt: '', concurrencyToken: 'room-1' }], page: 1, pageSize: 100, totalCount: 1 })
  vi.mocked(roomBlocksApi.list).mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 })
})

test('loads real operating hours and saves the edited seven-day schedule', async () => {
  vi.mocked(operatingHoursApi.update).mockResolvedValue({ configured: true, days, concurrencyToken: 'oh-2' })
  render(<Settings />)
  expect(await screen.findByText('Horário do estabelecimento')).toBeInTheDocument()
  const save = screen.getByRole('button', { name: /salvar horário/i })
  expect(save).toBeDisabled()
  const monday = screen.getByTestId('wpe-day-MONDAY')
  fireEvent.change(within(monday).getAllByLabelText('Fechamento')[0], { target: { value: '19:00' } })
  expect(save).toBeEnabled()
  fireEvent.click(save)
  await waitFor(() => expect(operatingHoursApi.update).toHaveBeenCalledWith(expect.objectContaining({
    concurrencyToken: 'oh-1',
    days: expect.arrayContaining([{ dayOfWeek: 'MONDAY', intervals: [{ opensAt: '08:00', closesAt: '19:00' }] }]),
  })))
})

test('shows the explicit unconfigured state and can create a room block', async () => {
  vi.mocked(operatingHoursApi.get).mockResolvedValueOnce({ configured: false, days: [], concurrencyToken: null })
  vi.mocked(roomBlocksApi.create).mockResolvedValue({ id: 'block-1', roomId: 'room-1', roomName: 'Sala 1', startAt: '2026-09-15T10:00:00Z', endAt: '2026-09-15T11:00:00Z', reason: 'Manutenção', status: 'ACTIVE', createdAt: '', concurrencyToken: 'b1' })
  render(<Settings />)
  expect(await screen.findByText(/ainda não foi configurado/i)).toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Sala'), { target: { value: 'room-1' } })
  fireEvent.change(screen.getByLabelText('Início'), { target: { value: '2026-09-15T10:00' } })
  fireEvent.change(screen.getByLabelText('Fim'), { target: { value: '2026-09-15T11:00' } })
  fireEvent.change(screen.getByLabelText('Motivo'), { target: { value: 'Manutenção' } })
  fireEvent.click(screen.getByRole('button', { name: /criar bloqueio/i }))
  await waitFor(() => expect(roomBlocksApi.create).toHaveBeenCalledWith(expect.objectContaining({ roomId: 'room-1', reason: 'Manutenção' })))
})
