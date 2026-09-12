import { render, screen } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalLeasesApi } from '../../api/modules'
import { ProfessionalLeases } from './ProfessionalLeases'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalLeasesApi: { list: vi.fn(), detail: vi.fn() },
}))

beforeEach(() => {
  vi.mocked(professionalLeasesApi.list).mockResolvedValue({
    items: [{ id: 'l1', tenantName: 'Maria Clara', roomId: 'room1', roomName: 'Sala 1', mode: 'MONTHLY',
      contractedRate: 1800, billingStartAt: '2026-01-01T00:00:00Z', billingDueDay: 5,
      occupancyStartAt: '2026-01-01T00:00:00Z', occupancyEndAt: null, status: 'ATIVA' }],
    page: 1, pageSize: 20, totalCount: 1,
  })
})

test('lists leases read-only with no edit controls', async () => {
  render(<ProfessionalLeases />)
  const roomCell = await screen.findByText('Sala 1')
  expect(roomCell).toBeInTheDocument()
  const table = screen.getByRole('table')
  expect(table).toBeInTheDocument()
  // Status should be visible within the table
  const statusElements = screen.getAllByText('Ativa')
  expect(statusElements.length).toBeGreaterThan(0)
  expect(screen.queryByRole('button', { name: /editar/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /excluir/i })).not.toBeInTheDocument()
})
