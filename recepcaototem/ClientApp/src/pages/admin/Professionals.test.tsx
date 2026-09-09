import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { Professionals } from './Professionals'
import { professionalsApi } from '../../api/modules'
import { adminProfessionalAvailabilityApi } from '../../api/modules'

vi.mock('../../api/modules', () => ({
  professionalsApi: {
    list: vi.fn(), create: vi.fn(), update: vi.fn(), changeStatus: vi.fn(),
    putPhoto: vi.fn(), removePhoto: vi.fn(), eligibleUsers: vi.fn(), userLink: vi.fn(),
    putUserLink: vi.fn(), removeUserLink: vi.fn(),
  },
  adminProfessionalAvailabilityApi: { get: vi.fn(), update: vi.fn(), listExceptions: vi.fn(), createException: vi.fn(), updateException: vi.fn(), deleteException: vi.fn() },
}))
vi.mock('../../auth/SessionProvider', () => ({
  useSession: vi.fn(() => ({ user: { roles: ['ADMINISTRADOR'] } })),
}))

const professional = {
  id: 'professional-1', name: 'Ana Souza', profession: 'Fisioterapeuta', whatsApp: '+5565999999999',
  isActive: true, hasPhoto: false, photoUrl: null, hasLinkedUser: true,
  createdAt: '2026-09-05T00:00:00Z', updatedAt: '2026-09-05T00:00:00Z', concurrencyToken: 'token-1',
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(professionalsApi.list).mockResolvedValue({ items: [professional], page: 1, pageSize: 20, totalCount: 1 })
})

test('loads professionals from the server and never renders mock room data', async () => {
  render(<Professionals />)
  expect(screen.getByRole('status')).toHaveTextContent('Carregando profissionais')
  expect(await screen.findByText('Ana Souza')).toBeInTheDocument()
  expect(professionalsApi.list).toHaveBeenCalledWith(
    { search: undefined, status: 'all', page: 1, pageSize: 20 }, expect.any(AbortSignal),
  )
  expect(screen.queryByText(/Sala/i)).not.toBeInTheDocument()
})

test('uses a debounced server search, resets page, and shows server zero results', async () => {
  vi.mocked(professionalsApi.list)
    .mockResolvedValueOnce({ items: [professional], page: 2, pageSize: 20, totalCount: 21 })
    .mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  render(<Professionals />)
  fireEvent.change(await screen.findByLabelText('Buscar profissionais'), { target: { value: '  ninguém  ' } })
  await waitFor(() => expect(professionalsApi.list).toHaveBeenLastCalledWith(
    { search: 'ninguém', status: 'all', page: 1, pageSize: 20 }, expect.any(AbortSignal),
  ), { timeout: 1000 })
  expect(await screen.findByText('Nenhum profissional encontrado.')).toBeInTheDocument()
})

test('creates, edits and changes status using the current opaque token', async () => {
  vi.mocked(professionalsApi.create).mockResolvedValue({ ...professional, id: 'professional-2', name: 'Nova Pessoa' })
  vi.mocked(professionalsApi.update).mockResolvedValue({ ...professional, name: 'Ana Atualizada', concurrencyToken: 'token-2' })
  vi.mocked(professionalsApi.changeStatus).mockResolvedValue({ ...professional, isActive: false, concurrencyToken: 'token-3' })
  render(<Professionals />)
  await screen.findByText('Ana Souza')
  fireEvent.click(screen.getByRole('button', { name: /Novo profissional/i }))
  fireEvent.change(screen.getByLabelText('Nome completo'), { target: { value: 'Nova Pessoa' } })
  fireEvent.change(screen.getByLabelText('Profissão'), { target: { value: 'Psicóloga' } })
  fireEvent.change(screen.getByLabelText('WhatsApp'), { target: { value: '(65) 99999-9999' } })
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar profissional' }))
  await waitFor(() => expect(professionalsApi.create).toHaveBeenCalledWith({
    name: 'Nova Pessoa', profession: 'Psicóloga', whatsApp: '(65) 99999-9999',
  }))
  fireEvent.click(screen.getByRole('button', { name: 'Editar Ana Souza' }))
  fireEvent.change(screen.getByLabelText('Nome completo'), { target: { value: 'Ana Atualizada' } })
  fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
  await waitFor(() => expect(professionalsApi.update).toHaveBeenCalledWith('professional-1', expect.objectContaining({ concurrencyToken: 'token-1' })))
  fireEvent.click(screen.getByRole('button', { name: /Desativar Ana Atualizada/i }))
  await waitFor(() => expect(professionalsApi.changeStatus).toHaveBeenCalledWith('professional-1', false, 'token-2'))
})

test('only administrators can open the Identity link action', async () => {
  const { useSession } = await import('../../auth/SessionProvider')
  vi.mocked(useSession).mockReturnValue({ user: { roles: ['GERENTE'] } } as never)
  render(<Professionals />)
  await screen.findByText('Ana Souza')
  expect(screen.queryByRole('button', { name: /Vincular conta/i })).not.toBeInTheDocument()
})

test('shows a conflict message and reloads after RESOURCE_MODIFIED', async () => {
  const { ApiError } = await import('../../api/client')
  vi.mocked(professionalsApi.changeStatus).mockRejectedValue(new ApiError(409, 'RESOURCE_MODIFIED', 'Conflito'))
  render(<Professionals />)
  await screen.findByText('Ana Souza')
  fireEvent.click(screen.getByRole('button', { name: /Desativar Ana Souza/i }))
  expect(await screen.findByText(/alterado por outra operação/i)).toBeInTheDocument()
  expect(professionalsApi.list).toHaveBeenCalledTimes(2)
})

test('opens the shared availability editor for the selected professional', async () => {
  vi.mocked(adminProfessionalAvailabilityApi.get).mockResolvedValue({ mode: 'INHERIT_GLOBAL', days: [], effectiveDays: [], globalDays: [], concurrencyToken: 'v1', existingReservationsOutsideAvailabilityCount: 0 })
  vi.mocked(adminProfessionalAvailabilityApi.listExceptions).mockResolvedValue([])
  render(<Professionals />)
  await screen.findByText('Ana Souza')
  fireEvent.click(screen.getByRole('button', { name: 'Disponibilidade de Ana Souza' }))
  expect(await screen.findByText('Disponibilidade de Ana Souza')).toBeInTheDocument()
  expect(adminProfessionalAvailabilityApi.get).toHaveBeenCalledWith('professional-1')
})
