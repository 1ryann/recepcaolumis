import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, expect, test, vi } from 'vitest'
import { leasesApi, professionalsApi, roomRentalInquiriesApi, roomsApi, tenantsApi } from '../../api/modules'
import { Leases } from './Leases'

vi.mock('../../api/modules', () => ({
  leasesApi: { list: vi.fn(), detail: vi.fn(), create: vi.fn(), update: vi.fn(), postpone: vi.fn(), cancel: vi.fn(), end: vi.fn() },
  tenantsApi: { list: vi.fn(), create: vi.fn() }, professionalsApi: { list: vi.fn() }, roomsApi: { list: vi.fn() },
  roomRentalInquiriesApi: { get: vi.fn() },
}))

// Unlike Leases.test.tsx (which stubs useSearchParams), these tests run the page inside a real router:
// clearing ?inquiryId= re-renders and re-runs the URL effects exactly as in the browser.

const lease = {
  id: 'lease-1', tenantId: 'tenant-1', tenantName: 'Clínica Aurora', professionalId: 'professional-1',
  professionalName: 'Ana Lima', roomId: 'room-1', roomName: 'Sala 101', mode: 'HOURLY' as const,
  contractedRate: 150.5, billingStartAt: '2026-09-06T10:00:00Z', billingDueDay: 10,
  occupancyStartAt: '2026-09-07T10:00:00Z', occupancyEndAt: '2026-09-07T12:00:00Z',
  status: 'AGENDADA' as const, createdAt: '2026-09-06T00:00:00Z', updatedAt: '2026-09-06T00:00:00Z',
  concurrencyToken: 'lease-token-1',
}

const convertedInquiry = {
  id: 'inquiry-1', roomId: 'room-1', roomName: 'Sala 101', fullName: 'Ana Souza', whatsApp: '+5569999999999',
  professionOrCompany: 'Clínica A', note: null, presentedAvailabilityStatus: 'AVAILABLE_NOW' as const,
  presentedAvailableFrom: null, presentedAvailabilityLabel: 'Disponível agora',
  status: 'CONVERTED' as const, leaseId: 'lease-1', convertedAt: '2026-11-15T10:00:00Z', createdAt: '2026-11-14T15:00:00Z',
  desiredStartDate: '2026-11-10', desiredEndDate: '2026-11-20',
}

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location-search">{location.search}</output>
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(leasesApi.list).mockResolvedValue({ items: [lease], page: 1, pageSize: 20, totalCount: 1 })
  vi.mocked(tenantsApi.list).mockResolvedValue({ items: [], page: 1, pageSize: 100, totalCount: 0 })
  vi.mocked(professionalsApi.list).mockResolvedValue({ items: [], page: 1, pageSize: 100, totalCount: 0 })
  vi.mocked(roomsApi.list).mockResolvedValue({ items: [], page: 1, pageSize: 100, totalCount: 0 })
})

test('with a real router, a CONVERTED inquiry still opens the linked lease detail after inquiryId is cleared', async () => {
  vi.mocked(roomRentalInquiriesApi.get).mockResolvedValue(convertedInquiry)
  // Resolve the detail only after the page has had the chance to clear ?inquiryId= (as over a real network).
  vi.mocked(leasesApi.detail).mockImplementation(() => new Promise(resolve => setTimeout(() => resolve(lease), 20)))

  render(<MemoryRouter initialEntries={['/admin/locacoes?inquiryId=inquiry-1']}><Leases /><LocationProbe /></MemoryRouter>)

  expect(await screen.findByText('Este interesse já foi convertido em uma locação.')).toBeInTheDocument()
  expect(await screen.findByRole('heading', { name: 'Detalhes da locação' })).toBeInTheDocument()
  expect(leasesApi.detail).toHaveBeenCalledWith('lease-1')
  // The router applies setSearchParams in a transition, which can land one render after the detail.
  await waitFor(() => expect(screen.getByTestId('location-search').textContent).not.toContain('inquiryId'))
  expect(screen.queryByRole('heading', { name: 'Nova locação' })).not.toBeInTheDocument()
  expect(leasesApi.create).not.toHaveBeenCalled()
})
