import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { ApiError } from '../../api/client'
import { leasesApi, professionalsApi, roomsApi, tenantsApi } from '../../api/modules'
import { Leases, toInputDate } from './Leases'

vi.mock('../../api/modules', () => ({
  leasesApi: { list: vi.fn(), detail: vi.fn(), create: vi.fn(), update: vi.fn(), postpone: vi.fn(), cancel: vi.fn(), end: vi.fn() },
  tenantsApi: { list: vi.fn(), create: vi.fn() }, professionalsApi: { list: vi.fn() }, roomsApi: { list: vi.fn() },
}))

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
  vi.mocked(leasesApi.list).mockResolvedValue({ items: [lease], page: 1, pageSize: 20, totalCount: 1 })
  vi.mocked(tenantsApi.list).mockResolvedValue({ items: [{ id: 'tenant-1', name: 'Clínica Aurora', kind: 'LEGAL_ENTITY', isActive: true, createdAt: '', updatedAt: '', concurrencyToken: 't' }], page: 1, pageSize: 100, totalCount: 1 })
  vi.mocked(professionalsApi.list).mockResolvedValue({ items: [{ id: 'professional-1', name: 'Ana Lima', profession: 'Fisio', whatsApp: '+5565999999999', isActive: true, hasPhoto: false, photoUrl: null, hasLinkedUser: false, createdAt: '', updatedAt: '', concurrencyToken: 'p' }], page: 1, pageSize: 100, totalCount: 1 })
  vi.mocked(roomsApi.list).mockResolvedValue({ items: [{ id: 'room-1', name: 'Sala 101', description: null, hourlyRate: 100, dailyRate: 500, isActive: true, createdAt: '', updatedAt: '', concurrencyToken: 'r' }], page: 1, pageSize: 100, totalCount: 1 })
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
