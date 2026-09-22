import { useCallback, useEffect, useState } from 'react'
import { professionalLeasesApi, type LeaseStatus, type PagedResponse, type ProfessionalLeaseDto } from '../../api/modules'
import { EmptyState, PageHeader, StatusBadge } from '../../components/PageElements'
import { ProfessionalFilterBar } from '../../components/ProfessionalFilterBar'

const pageSize = 20
const empty: PagedResponse<ProfessionalLeaseDto> = { items: [], page: 1, pageSize, totalCount: 0 }
const statusLabels: Record<LeaseStatus, string> = {
  AGENDADA: 'Agendada',
  ATIVA: 'Ativa',
  ENCERRAMENTO_PENDENTE: 'Encerramento pendente',
  ENCERRADA: 'Encerrada',
  CANCELADA: 'Cancelada',
}
const statusTones: Record<LeaseStatus, 'waiting' | 'active' | 'pending' | 'inactive' | 'cancelled'> = {
  AGENDADA: 'waiting',
  ATIVA: 'active',
  ENCERRAMENTO_PENDENTE: 'pending',
  ENCERRADA: 'inactive',
  CANCELADA: 'cancelled',
}

const formatDate = (value: string) => new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'short', timeZone: 'America/Porto_Velho',
}).format(new Date(value))

const formatPeriod = (startAt: string, endAt: string | null) => {
  const start = formatDate(startAt)
  return endAt ? `${start} até ${formatDate(endAt)}` : `${start} em diante`
}

export function ProfessionalLeases() {
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [status, setStatus] = useState<LeaseStatus | 'all'>('all')

  const load = useCallback(async (signal?: AbortSignal) => {
    setError('')
    try {
      const response = await professionalLeasesApi.list({ page: 1, pageSize }, signal)
      setResult(response)
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError')
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as locações.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  const filteredItems = status === 'all' ? result.items : result.items.filter(item => item.status === status)
  const displayCount = status === 'all' ? result.totalCount : filteredItems.length

  return <div className="page-enter">
    <PageHeader eyebrow="Contratos" title="Locações" description="Consulte suas locações e contratos." />
    <section className="panel table-panel">
      <div className="table-toolbar">
        <ProfessionalFilterBar>
          <select className="field-input compact-select" value={status} aria-label="Status das locações"
            onChange={event => setStatus(event.target.value as LeaseStatus | 'all')}>
            <option value="all">Todos os status</option>
            {Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
          <span>{displayCount} locação{displayCount === 1 ? '' : 's'}</span>
        </ProfessionalFilterBar>
      </div>
      {loading ? <div className="empty-state" role="status">Carregando locações…</div>
        : error && result.items.length === 0 ? <EmptyState><p>{error}</p><button className="secondary-button" onClick={() => void load()}>Tentar novamente</button></EmptyState>
          : result.items.length === 0 ? <EmptyState>Nenhuma locação encontrada.</EmptyState>
            : filteredItems.length === 0 ? <EmptyState>Nenhuma locação com o status selecionado.</EmptyState>
              : <div className="table-scroll"><table className="data-table"><thead><tr>
                <th>Sala</th><th>Período</th><th>Valor</th><th>Status</th>
              </tr></thead><tbody>{filteredItems.map(lease => <tr key={lease.id}>
                <td><strong>{lease.roomName}</strong></td>
                <td>{formatPeriod(lease.occupancyStartAt, lease.occupancyEndAt)}</td>
                <td>{new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(lease.contractedRate)}</td>
                <td><StatusBadge tone={statusTones[lease.status]} label={statusLabels[lease.status]} /></td>
              </tr>)}</tbody></table></div>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}
    </section>
  </div>
}
