import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { RoomRentalInquiries } from './RoomRentalInquiries'
import { type RoomRentalInquiryAdminDto, roomRentalInquiriesApi } from '../../api/modules'

const navigateSpy = vi.fn()
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => navigateSpy,
}))

vi.mock('../../api/modules', () => ({
  roomRentalInquiriesApi: { list: vi.fn(), get: vi.fn() },
}))

const newInquiry: RoomRentalInquiryAdminDto = {
  id: 'inquiry-1', roomId: 'room-1', roomName: 'Sala 202', fullName: 'Ana Souza', whatsApp: '+5569999999999',
  professionOrCompany: 'Clínica A', note: 'Prefere manhãs', presentedAvailabilityStatus: 'AVAILABLE_SOON',
  presentedAvailableFrom: '2026-11-16', presentedAvailabilityLabel: 'Disponível em breve — a partir de 16/11/2026',
  status: 'NEW', leaseId: null, convertedAt: null, createdAt: '2026-11-14T15:00:00Z',
  desiredStartDate: '2026-11-10', desiredEndDate: '2026-11-20',
}
const convertedInquiry: RoomRentalInquiryAdminDto = {
  id: 'inquiry-2', roomId: 'room-2', roomName: 'Sala 101', fullName: 'Bruna Lima', whatsApp: '+5569999999998',
  professionOrCompany: 'Clínica B', note: null, presentedAvailabilityStatus: 'AVAILABLE_NOW',
  presentedAvailableFrom: null, presentedAvailabilityLabel: 'Disponível agora',
  status: 'CONVERTED', leaseId: 'lease-9', convertedAt: '2026-11-14T16:00:00Z', createdAt: '2026-11-14T14:00:00Z',
  desiredStartDate: null, desiredEndDate: null,
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(roomRentalInquiriesApi.list).mockResolvedValue({
    items: [newInquiry, convertedInquiry], page: 1, pageSize: 20, totalCount: 2,
  })
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue(newInquiry)
})

test('loads inquiries from the API and shows the historical availability label from the backend', async () => {
  render(<RoomRentalInquiries />)
  expect(screen.getByRole('status')).toHaveTextContent(/carregando/i)
  expect(await screen.findByText('Ana Souza')).toBeInTheDocument()
  expect(screen.getByText('Disponível em breve — a partir de 16/11/2026')).toBeInTheDocument()
  expect(screen.getByText('Disponível agora')).toBeInTheDocument()
  expect(roomRentalInquiriesApi.list).toHaveBeenCalledWith({ page: 1, pageSize: 20 }, expect.any(AbortSignal))
})

test('a New inquiry shows Ver detalhes and Criar locação, never a status-editing control', async () => {
  render(<RoomRentalInquiries />)
  await screen.findByText('Ana Souza')
  const row = screen.getByText('Ana Souza').closest('tr')!
  expect(within(row).getByRole('button', { name: /ver detalhes/i })).toBeInTheDocument()
  expect(within(row).getByRole('button', { name: /criar locação/i })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /alterar status/i })).not.toBeInTheDocument()
})

test('a Converted inquiry shows Ver detalhes, Ver locação and a converted indicator', async () => {
  render(<RoomRentalInquiries />)
  await screen.findByText('Bruna Lima')
  const row = screen.getByText('Bruna Lima').closest('tr')!
  expect(within(row).getByRole('button', { name: /ver detalhes/i })).toBeInTheDocument()
  expect(within(row).getByRole('button', { name: /ver locação/i })).toBeInTheDocument()
  expect(within(row).getByText(/convertido em locação/i)).toBeInTheDocument()
  expect(within(row).queryByRole('button', { name: /criar locação/i })).not.toBeInTheDocument()
})

test('Criar locação navigates with the inquiryId query param', async () => {
  render(<RoomRentalInquiries />)
  await screen.findByText('Ana Souza')
  const row = screen.getByText('Ana Souza').closest('tr')!
  fireEvent.click(within(row).getByRole('button', { name: /criar locação/i }))
  expect(navigateSpy).toHaveBeenCalledWith('/admin/locacoes?inquiryId=inquiry-1')
})

test('Ver locação navigates with the leaseId query param', async () => {
  render(<RoomRentalInquiries />)
  await screen.findByText('Bruna Lima')
  const row = screen.getByText('Bruna Lima').closest('tr')!
  fireEvent.click(within(row).getByRole('button', { name: /ver locação/i }))
  expect(navigateSpy).toHaveBeenCalledWith('/admin/locacoes?leaseId=lease-9')
})

test('Ver detalhes opens the detail modal fetched from the API', async () => {
  render(<RoomRentalInquiries />)
  await screen.findByText('Ana Souza')
  const row = screen.getByText('Ana Souza').closest('tr')!
  fireEvent.click(within(row).getByRole('button', { name: /ver detalhes/i }))
  await waitFor(() => expect(roomRentalInquiriesApi.get).toHaveBeenCalledWith('inquiry-1'))
  expect(await screen.findByText('Prefere manhãs')).toBeInTheDocument()
})

test('the detail modal shows the customer-requested period', async () => {
  render(<RoomRentalInquiries />)
  await screen.findByText('Ana Souza')
  const row = screen.getByText('Ana Souza').closest('tr')!
  fireEvent.click(within(row).getByRole('button', { name: /ver detalhes/i }))
  expect(await screen.findByText('Período desejado')).toBeInTheDocument()
  expect(screen.getByText('10/11/2026 até 20/11/2026')).toBeInTheDocument()
})

test('the detail modal falls back to "Não informado" for a legacy inquiry with no desired dates', async () => {
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue(convertedInquiry)
  render(<RoomRentalInquiries />)
  await screen.findByText('Bruna Lima')
  const row = screen.getByText('Bruna Lima').closest('tr')!
  fireEvent.click(within(row).getByRole('button', { name: /ver detalhes/i }))
  await waitFor(() => expect(roomRentalInquiriesApi.get).toHaveBeenCalledWith('inquiry-2'))
  expect(await screen.findByText('Período desejado')).toBeInTheDocument()
  expect(screen.getByText('Não informado')).toBeInTheDocument()
})

test('shows an error with retry when loading fails, and an empty state with no results', async () => {
  vi.mocked(roomRentalInquiriesApi.list).mockRejectedValueOnce(new Error('offline'))
  render(<RoomRentalInquiries />)
  expect(await screen.findByText('offline')).toBeInTheDocument()
  vi.mocked(roomRentalInquiriesApi.list).mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  fireEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))
  expect(await screen.findByText(/nenhum interesse encontrado/i)).toBeInTheDocument()
})

test('paginates using Anterior/Próxima with the server page size', async () => {
  vi.mocked(roomRentalInquiriesApi.list).mockResolvedValue({
    items: [newInquiry], page: 1, pageSize: 20, totalCount: 21,
  })
  render(<RoomRentalInquiries />)
  await screen.findByText('Ana Souza')
  fireEvent.click(screen.getByRole('button', { name: 'Próxima' }))
  await waitFor(() => expect(roomRentalInquiriesApi.list).toHaveBeenLastCalledWith({ page: 2, pageSize: 20 }, expect.any(AbortSignal)))
})
