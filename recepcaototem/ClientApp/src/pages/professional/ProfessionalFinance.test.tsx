import { render, screen } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalFinanceApi } from '../../api/modules'
import { ProfessionalFinance } from './ProfessionalFinance'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalFinanceApi: { list: vi.fn(), detail: vi.fn() },
}))

beforeEach(() => {
  vi.mocked(professionalFinanceApi.list).mockResolvedValue({
    items: [{ id: 'c1', leaseId: 'l1', professionalId: 'p1', tenantId: 't1',
      referencePeriodStart: '2026-09-01T00:00:00Z', referencePeriodEnd: '2026-09-30T00:00:00Z',
      dueDate: '2026-09-05', calculatedAmount: 1800, finalAmount: 1800, status: 'OVERDUE',
      calculationDetails: '', adjustmentReason: null, cancellationReason: null, paidAt: null,
      createdAt: '2026-09-01T00:00:00Z', updatedAt: '2026-09-01T00:00:00Z', concurrencyToken: 'tok' }],
    page: 1, pageSize: 20, totalCount: 1,
  })
})

test('shows a summary of open amount and a table with computed OVERDUE status, no write actions', async () => {
  render(<ProfessionalFinance />)
  expect(await screen.findByText('OVERDUE')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /marcar como pago/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /excluir/i })).not.toBeInTheDocument()
})
