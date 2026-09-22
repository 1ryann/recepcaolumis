import { CalendarClock, Wallet } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { professionalFinanceApi, type FinancialChargeDto, type FinancialChargeStatus, type PagedResponse } from '../../api/modules'
import { EmptyState, PageHeader, StatusBadge } from '../../components/PageElements'

const pageSize = 20
const empty: PagedResponse<FinancialChargeDto> = { items: [], page: 1, pageSize, totalCount: 0 }

const statusTones: Record<FinancialChargeStatus, 'pending' | 'overdue' | 'paid' | 'cancelled'> = {
  PENDING: 'pending',
  OVERDUE: 'overdue',
  PAID: 'paid',
  CANCELLED: 'cancelled',
}

const statusLabels: Record<FinancialChargeStatus, string> = {
  PENDING: 'Pendente',
  OVERDUE: 'Em atraso',
  PAID: 'Pago',
  CANCELLED: 'Cancelada',
}

const currencyFormatter = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
const formatCurrency = (value: number) => currencyFormatter.format(value)

const formatDate = (value: string) => new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'short', timeZone: 'America/Porto_Velho',
}).format(new Date(value))

const isOpen = (status: FinancialChargeStatus) => status === 'PENDING' || status === 'OVERDUE'

export function ProfessionalFinance() {
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [page, setPage] = useState(1)

  const load = useCallback(async (signal?: AbortSignal) => {
    setError('')
    try {
      const response = await professionalFinanceApi.list({ status: 'all', page, pageSize }, signal)
      setResult(response)
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError')
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar o financeiro.')
    } finally {
      setLoading(false)
    }
  }, [page])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  const pages = Math.max(1, Math.ceil(result.totalCount / pageSize))
  const openCharges = result.items.filter(charge => isOpen(charge.status))
  const openAmount = openCharges.reduce((sum, charge) => sum + charge.finalAmount, 0)
  const nextDueDate = openCharges.reduce<string | null>((earliest, charge) => (
    earliest === null || charge.dueDate < earliest ? charge.dueDate : earliest
  ), null)

  return <div className="page-enter">
    <PageHeader eyebrow="Cobranças" title="Financeiro" description="Consulte suas cobranças e vencimentos." />
    <div className="professional-kpi-grid finance-summary-grid">
      <article className="professional-kpi-card">
        <span className="professional-kpi-icon"><Wallet size={20} /></span>
        <div><small>Valor em aberto</small><strong>{formatCurrency(openAmount)}</strong></div>
      </article>
      <article className="professional-kpi-card">
        <span className="professional-kpi-icon"><CalendarClock size={20} /></span>
        <div><small>Próximo vencimento</small><strong>{nextDueDate ? formatDate(nextDueDate) : '—'}</strong></div>
      </article>
    </div>
    <section className="panel table-panel">
      {loading ? <div className="empty-state" role="status">Carregando financeiro…</div>
        : error && result.items.length === 0 ? <EmptyState><p>{error}</p><button className="secondary-button" onClick={() => void load()}>Tentar novamente</button></EmptyState>
          : result.items.length === 0 ? <EmptyState>Nenhuma cobrança encontrada.</EmptyState>
            : <div className="table-scroll"><table className="data-table"><thead><tr>
              <th>Competência</th><th>Vencimento</th><th>Valor</th><th>Status</th>
            </tr></thead><tbody>{result.items.map(charge => <tr key={charge.id}>
              <td>{formatDate(charge.referencePeriodStart)} até {formatDate(charge.referencePeriodEnd)}</td>
              <td>{formatDate(charge.dueDate)}</td>
              <td>{formatCurrency(charge.finalAmount)}</td>
              <td><StatusBadge tone={statusTones[charge.status]} label={statusLabels[charge.status]} /></td>
            </tr>)}</tbody></table></div>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}
      {result.totalCount > pageSize && <div className="pagination"><button className="secondary-button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>
  </div>
}
