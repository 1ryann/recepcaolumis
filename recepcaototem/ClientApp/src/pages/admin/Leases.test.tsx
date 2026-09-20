import { noRoomFeatures } from '../../test/roomFixtures'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { ApiError } from '../../api/client'
import { leasesApi, professionalsApi, roomRentalInquiriesApi, roomsApi, tenantsApi } from '../../api/modules'
import { Leases, toInputDate } from './Leases'

vi.mock('../../api/modules', () => ({
  leasesApi: { list: vi.fn(), detail: vi.fn(), create: vi.fn(), update: vi.fn(), postpone: vi.fn(), cancel: vi.fn(), end: vi.fn() },
  tenantsApi: { list: vi.fn(), create: vi.fn() }, professionalsApi: { list: vi.fn() }, roomsApi: { list: vi.fn() },
  roomRentalInquiriesApi: { get: vi.fn() },
}))

// The page reads ?inquiryId=/?leaseId= via useSearchParams and clears inquiryId after a successful
// conversion via setSearchParams. Only these two hooks are overridden — every other export (routes,
// Link, etc.) keeps its real implementation — so no <Router> wrapper is needed in these tests.
let currentSearchParams = new URLSearchParams()
const setSearchParamsSpy = vi.fn((updater: URLSearchParams | ((current: URLSearchParams) => URLSearchParams)) => {
  currentSearchParams = typeof updater === 'function' ? updater(currentSearchParams) : updater
})
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useSearchParams: () => [currentSearchParams, setSearchParamsSpy] as const,
}))

const inquiry = {
  id: 'inquiry-1', roomId: 'room-1', roomName: 'Sala 101', fullName: 'Ana Souza', whatsApp: '+5569999999999',
  professionOrCompany: 'Clínica A', note: null, presentedAvailabilityStatus: 'AVAILABLE_NOW' as const,
  presentedAvailableFrom: null, presentedAvailabilityLabel: 'Disponível agora',
  status: 'NEW' as const, leaseId: null, convertedAt: null, createdAt: '2026-11-14T15:00:00Z',
  desiredStartDate: '2026-11-10', desiredEndDate: '2026-11-20',
}

const lease = {
  id: 'lease-1', tenantId: 'tenant-1', tenantName: 'Clínica Aurora', professionalId: 'professional-1',
  professionalName: 'Ana Lima', roomId: 'room-1', roomName: 'Sala 101', mode: 'HOURLY' as const,
  contractedRate: 150.5, billingStartAt: '2026-09-06T10:00:00Z', billingDueDay: 10,
  occupancyStartAt: '2026-09-07T10:00:00Z', occupancyEndAt: '2026-09-07T12:00:00Z',
  status: 'AGENDADA' as const, createdAt: '2026-09-06T00:00:00Z', updatedAt: '2026-09-06T00:00:00Z',
  concurrencyToken: 'lease-token-1',
}

beforeEach(() => {
  vi.clearAllMocks()
  currentSearchParams = new URLSearchParams()
  vi.mocked(leasesApi.list).mockResolvedValue({ items: [lease], page: 1, pageSize: 20, totalCount: 1 })
  vi.mocked(tenantsApi.list).mockResolvedValue({ items: [{ id: 'tenant-1', name: 'Clínica Aurora', kind: 'LEGAL_ENTITY', isActive: true, createdAt: '', updatedAt: '', concurrencyToken: 't' }], page: 1, pageSize: 100, totalCount: 1 })
  vi.mocked(professionalsApi.list).mockResolvedValue({ items: [{ id: 'professional-1', name: 'Ana Lima', profession: 'Fisio', whatsApp: '+5565999999999', isActive: true, hasPhoto: false, photoUrl: null, hasLinkedUser: false, createdAt: '', updatedAt: '', concurrencyToken: 'p' }], page: 1, pageSize: 100, totalCount: 1 })
  vi.mocked(roomsApi.list).mockResolvedValue({ items: [{ ...noRoomFeatures, id: 'room-1', name: 'Sala 101', description: null, hourlyRate: 100, dailyRate: 500, isActive: true, createdAt: '', updatedAt: '', concurrencyToken: 'r' }], page: 1, pageSize: 100, totalCount: 1 })
})

test('converts an API instant to the browser local datetime input without treating UTC as local time', () => {
  const instant = new Date('2027-01-15T12:34:00Z')
  const pad = (value: number) => String(value).padStart(2, '0')

  expect(toInputDate(instant.toISOString())).toBe(
    `${instant.getFullYear()}-${pad(instant.getMonth() + 1)}-${pad(instant.getDate())}T${pad(instant.getHours())}:${pad(instant.getMinutes())}`,
  )
})

test('loads real leases with server filters and BRL presentation', async () => {
  render(<Leases />)
  expect(screen.getByRole('status')).toHaveTextContent('Carregando locações')
  expect(await screen.findByText('Clínica Aurora')).toBeInTheDocument()
  expect(screen.getByText(/150,50/)).toBeInTheDocument()
  expect(screen.queryByText(/paga|em atraso|próximo vencimento/i)).not.toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Buscar locações'), { target: { value: ' aurora ' } })
  await waitFor(() => expect(leasesApi.list).toHaveBeenLastCalledWith(
    expect.objectContaining({ search: 'aurora', page: 1 }), expect.any(AbortSignal)), { timeout: 1000 })
  fireEvent.change(screen.getByLabelText('Filtrar por profissional'), { target: { value: 'professional-1' } })
  await waitFor(() => expect(leasesApi.list).toHaveBeenLastCalledWith(
    expect.objectContaining({ professionalId: 'professional-1', page: 1 }), expect.any(AbortSignal)))
})

test('supports tenant creation and scheduled ending without hardcoded records', async () => {
  vi.mocked(tenantsApi.create).mockResolvedValue({ id: 'tenant-2', name: 'Novo locatário', kind: 'INDIVIDUAL', isActive: true, createdAt: '', updatedAt: '', concurrencyToken: 't2' })
  vi.mocked(leasesApi.list).mockResolvedValue({ items: [{ ...lease, status: 'ATIVA' }], page: 1, pageSize: 20, totalCount: 1 })
  vi.mocked(leasesApi.end).mockResolvedValue({ ...lease, status: 'ATIVA', occupancyEndAt: '2026-09-07T13:00:00Z', concurrencyToken: 'lease-token-2' })
  render(<Leases />)
  await screen.findByText('Clínica Aurora')
  fireEvent.click(screen.getByRole('button', { name: /Nova locação/i }))
  fireEvent.click(await screen.findByRole('button', { name: /Novo locatário/i }))
  fireEvent.change(screen.getByLabelText('Nome do locatário'), { target: { value: 'Novo locatário' } })
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar locatário' }))
  await waitFor(() => expect(tenantsApi.create).toHaveBeenCalledWith({ name: 'Novo locatário', kind: 'INDIVIDUAL' }))
  fireEvent.click(screen.getByRole('button', { name: /Encerrar locação Clínica Aurora/i }))
  fireEvent.change(screen.getByLabelText('Data de encerramento'), { target: { value: '2026-09-07T13:00' } })
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar encerramento' }))
  await waitFor(() => expect(leasesApi.end).toHaveBeenCalledWith('lease-1', expect.any(String), 'lease-token-1'))
})

test('creates a lease from real resource identifiers', async () => {
  vi.mocked(leasesApi.create).mockResolvedValue({ ...lease, id: 'lease-2' })
  render(<Leases />)
  await screen.findByText('Clínica Aurora')
  fireEvent.click(screen.getByRole('button', { name: /Nova locação/i }))
  await screen.findByRole('option', { name: 'Clínica Aurora' })
  fireEvent.change(screen.getByLabelText('Locatário'), { target: { value: 'tenant-1' } })
  fireEvent.change(screen.getByLabelText('Profissional'), { target: { value: 'professional-1' } })
  fireEvent.change(screen.getByLabelText('Sala'), { target: { value: 'room-1' } })
  fireEvent.change(screen.getByLabelText('Valor contratado'), { target: { value: '150,50' } })
  fireEvent.change(screen.getByLabelText('Início da cobrança'), { target: { value: '2026-09-06T10:00' } })
  fireEvent.change(screen.getByLabelText('Início da ocupação'), { target: { value: '2026-09-07T10:00' } })
  fireEvent.change(screen.getByLabelText('Fim da ocupação'), { target: { value: '2026-09-07T12:00' } })
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar locação' }))
  await waitFor(() => expect(leasesApi.create).toHaveBeenCalledWith(expect.objectContaining({
    tenantId: 'tenant-1', professionalId: 'professional-1', roomId: 'room-1', contractedRate: 150.5,
  })))
})

test('reloads after a concurrency conflict', async () => {
  vi.mocked(leasesApi.cancel).mockRejectedValue(new ApiError(409, 'RESOURCE_MODIFIED', 'Conflito'))
  render(<Leases />)
  await screen.findByText('Clínica Aurora')
  fireEvent.click(screen.getByRole('button', { name: /Cancelar locação Clínica Aurora/i }))
  expect(await screen.findByRole('alert')).toHaveTextContent(/alterada por outra operação/i)
  expect(leasesApi.list).toHaveBeenCalledTimes(2)
})

test('?leaseId= opens the existing detail view via leasesApi.detail, not a second detail UI', async () => {
  currentSearchParams = new URLSearchParams('leaseId=lease-1')
  vi.mocked(leasesApi.detail).mockResolvedValue(lease)
  render(<Leases />)
  await waitFor(() => expect(leasesApi.detail).toHaveBeenCalledWith('lease-1'))
  expect(await screen.findByText('Detalhes da locação')).toBeInTheDocument()
  expect(screen.getAllByText('Clínica Aurora').length).toBeGreaterThan(0)
})

test('?inquiryId= opens the same Nova locação modal with the room preselected but still editable', async () => {
  currentSearchParams = new URLSearchParams('inquiryId=inquiry-1')
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue(inquiry)
  render(<Leases />)
  await waitFor(() => expect(roomRentalInquiriesApi.get).toHaveBeenCalledWith('inquiry-1'))
  expect(await screen.findByRole('heading', { name: 'Nova locação' })).toBeInTheDocument()
  const roomSelect = await screen.findByLabelText('Sala') as HTMLSelectElement
  expect(roomSelect.value).toBe('room-1')
  expect(roomSelect).not.toBeDisabled()
  // Nothing else is inferred from the inquiry: professional, mode, value, due day and dates keep
  // their normal empty/default state, exactly as when opening a plain "Nova locação".
  expect((screen.getByLabelText('Profissional') as HTMLSelectElement).value).toBe('')
  expect((screen.getByLabelText('Modalidade') as HTMLSelectElement).value).toBe('HOURLY')
  expect((screen.getByLabelText('Valor contratado') as HTMLInputElement).value).toBe('')
  expect((screen.getByLabelText('Dia de vencimento') as HTMLInputElement).value).toBe('')
  expect((screen.getByLabelText('Início da cobrança') as HTMLInputElement).value).toBe('')
  expect((screen.getByLabelText('Início da ocupação') as HTMLInputElement).value).toBe('')
  expect((screen.getByLabelText('Fim da ocupação') as HTMLInputElement).value).toBe('')
})

test('the inquiry availability is shown as read-only context text, never bound to a field', async () => {
  currentSearchParams = new URLSearchParams('inquiryId=inquiry-1')
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue(inquiry)
  render(<Leases />)
  expect(await screen.findByText(/Disponível agora/)).toBeInTheDocument()
  expect(screen.queryByDisplayValue('Disponível agora')).not.toBeInTheDocument()
})

test('the desired period from the inquiry is shown as context only, never prefilling a contract field', async () => {
  currentSearchParams = new URLSearchParams('inquiryId=inquiry-1')
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue(inquiry)
  render(<Leases />)
  expect(await screen.findByText('Período desejado: 10/11/2026 até 20/11/2026')).toBeInTheDocument()
  expect((screen.getByLabelText('Início da ocupação') as HTMLInputElement).value).toBe('')
  expect((screen.getByLabelText('Fim da ocupação') as HTMLInputElement).value).toBe('')
})

test('an inquiry without a desired period (pre-existing row) shows no period line', async () => {
  currentSearchParams = new URLSearchParams('inquiryId=inquiry-1')
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue({ ...inquiry, desiredStartDate: null, desiredEndDate: null })
  render(<Leases />)
  await screen.findByRole('heading', { name: 'Nova locação' })
  expect(screen.queryByText(/Período desejado/)).not.toBeInTheDocument()
})

test('closing the Nova locação modal without saving removes inquiryId from the URL', async () => {
  currentSearchParams = new URLSearchParams('inquiryId=inquiry-1&page=2')
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue(inquiry)
  render(<Leases />)
  await screen.findByRole('heading', { name: 'Nova locação' })
  fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))
  await waitFor(() => expect(screen.queryByRole('heading', { name: 'Nova locação' })).not.toBeInTheDocument())
  expect(currentSearchParams.get('inquiryId')).toBeNull()
  // Unrelated params survive.
  expect(currentSearchParams.get('page')).toBe('2')
  expect(leasesApi.create).not.toHaveBeenCalled()
})

test('a CONVERTED inquiry never reopens the conversion form: it clears inquiryId, explains why and opens the existing lease', async () => {
  currentSearchParams = new URLSearchParams('inquiryId=inquiry-1')
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue({
    ...inquiry, status: 'CONVERTED' as const, leaseId: 'lease-1', convertedAt: '2026-11-15T10:00:00Z',
  })
  vi.mocked(leasesApi.detail).mockResolvedValue(lease)
  render(<Leases />)
  expect(await screen.findByRole('heading', { name: 'Detalhes da locação' })).toBeInTheDocument()
  expect(leasesApi.detail).toHaveBeenCalledWith('lease-1')
  expect(screen.getByText('Este interesse já foi convertido em uma locação.')).toBeInTheDocument()
  expect(screen.queryByRole('heading', { name: 'Nova locação' })).not.toBeInTheDocument()
  expect(currentSearchParams.get('inquiryId')).toBeNull()
  expect(leasesApi.create).not.toHaveBeenCalled()
})

test('Novo locatário prefills the name from the inquiry but leaves Kind for the Admin to choose', async () => {
  currentSearchParams = new URLSearchParams('inquiryId=inquiry-1')
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue(inquiry)
  render(<Leases />)
  await screen.findByRole('heading', { name: 'Nova locação' })
  fireEvent.click(screen.getByRole('button', { name: /Novo locatário/i }))
  expect(await screen.findByLabelText('Nome do locatário')).toHaveValue('Ana Souza')
  expect(screen.getByLabelText('Tipo')).toHaveValue('INDIVIDUAL')
})

test('submitting a conversion includes roomRentalInquiryId, clears the param and opens the new lease detail', async () => {
  currentSearchParams = new URLSearchParams('inquiryId=inquiry-1')
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue(inquiry)
  const created = { ...lease, id: 'lease-2', tenantId: 'tenant-1', tenantName: 'Clínica Aurora' }
  vi.mocked(leasesApi.create).mockResolvedValue(created)
  render(<Leases />)
  await screen.findByRole('heading', { name: 'Nova locação' })
  await screen.findByRole('option', { name: 'Clínica Aurora' })
  fireEvent.change(screen.getByLabelText('Locatário'), { target: { value: 'tenant-1' } })
  fireEvent.change(screen.getByLabelText('Profissional'), { target: { value: 'professional-1' } })
  fireEvent.change(screen.getByLabelText('Valor contratado'), { target: { value: '150,50' } })
  fireEvent.change(screen.getByLabelText('Início da cobrança'), { target: { value: '2026-09-06T10:00' } })
  fireEvent.change(screen.getByLabelText('Início da ocupação'), { target: { value: '2026-09-07T10:00' } })
  fireEvent.change(screen.getByLabelText('Fim da ocupação'), { target: { value: '2026-09-07T12:00' } })
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar locação' }))
  await waitFor(() => expect(leasesApi.create).toHaveBeenCalledWith(
    expect.objectContaining({ roomId: 'room-1', roomRentalInquiryId: 'inquiry-1' })))
  await waitFor(() => expect(setSearchParamsSpy).toHaveBeenCalled())
  expect(await screen.findByText('Detalhes da locação')).toBeInTheDocument()
})

test('a 404 while loading the inquiry surfaces through the existing error infrastructure', async () => {
  currentSearchParams = new URLSearchParams('inquiryId=inquiry-missing')
  vi.mocked(roomRentalInquiriesApi.get).mockRejectedValue(new ApiError(404, 'NOT_FOUND', 'Interesse não encontrado.'))
  render(<Leases />)
  expect(await screen.findByText('Interesse não encontrado.')).toBeInTheDocument()
  expect(screen.queryByRole('heading', { name: 'Nova locação' })).not.toBeInTheDocument()
})

test('a 409 ROOM_RENTAL_INQUIRY_ALREADY_CONVERTED on submit surfaces the error without silently closing the form', async () => {
  currentSearchParams = new URLSearchParams('inquiryId=inquiry-1')
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue(inquiry)
  vi.mocked(leasesApi.create).mockRejectedValue(
    new ApiError(409, 'ROOM_RENTAL_INQUIRY_ALREADY_CONVERTED', 'O interesse já foi convertido.'))
  render(<Leases />)
  await screen.findByRole('heading', { name: 'Nova locação' })
  await screen.findByRole('option', { name: 'Clínica Aurora' })
  fireEvent.change(screen.getByLabelText('Locatário'), { target: { value: 'tenant-1' } })
  fireEvent.change(screen.getByLabelText('Profissional'), { target: { value: 'professional-1' } })
  fireEvent.change(screen.getByLabelText('Valor contratado'), { target: { value: '150,50' } })
  fireEvent.change(screen.getByLabelText('Início da cobrança'), { target: { value: '2026-09-06T10:00' } })
  fireEvent.change(screen.getByLabelText('Início da ocupação'), { target: { value: '2026-09-07T10:00' } })
  fireEvent.change(screen.getByLabelText('Fim da ocupação'), { target: { value: '2026-09-07T12:00' } })
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar locação' }))
  expect(await screen.findByText('O interesse já foi convertido.')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Nova locação' })).toBeInTheDocument()
})
