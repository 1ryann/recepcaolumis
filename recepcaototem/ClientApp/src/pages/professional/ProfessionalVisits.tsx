import { Clock3 } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { professionalVisitsApi, type PagedResponse, type VisitDto, type VisitStatus } from '../../api/modules'
import { EmptyState, PageHeader, StatusBadge } from '../../components/PageElements'

const pageSize = 50
const empty: PagedResponse<VisitDto> = { items: [], page: 1, pageSize, totalCount: 0 }
const statusLabels: Record<VisitStatus, string> = {
  WAITING: 'Aguardando', IN_SERVICE: 'Em atendimento', ENDED: 'Encerrado', CANCELLED: 'Cancelado',
}
const statusTones: Record<VisitStatus, 'waiting' | 'in-service' | 'inactive' | 'overdue'> = {
  WAITING: 'waiting', IN_SERVICE: 'in-service', ENDED: 'inactive', CANCELLED: 'overdue',
}
const formatDate = (value: string) => new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Porto_Velho',
}).format(new Date(value))
const todayBoundary = (end = false) => {
  const now = new Date()
  const local = new Date(now.toLocaleString('en-US', { timeZone: 'America/Porto_Velho' }))
  const value = local.toISOString().slice(0, 10)
  return new Date(`${value}T${end ? '23:59:59' : '00:00:00'}-04:00`).toISOString()
}

type Section = { key: VisitStatus; title: string }
const sections: Section[] = [
  { key: 'WAITING', title: 'Aguardando' },
  { key: 'IN_SERVICE', title: 'Em atendimento' },
  { key: 'ENDED', title: 'Encerrados hoje' },
]

export function ProfessionalVisits() {
  const [results, setResults] = useState<Record<VisitStatus, PagedResponse<VisitDto>>>({
    WAITING: empty, IN_SERVICE: empty, ENDED: empty, CANCELLED: empty,
  })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async (signal?: AbortSignal) => {
    setError('')
    try {
      const [waitingRes, inServiceRes, endedRes] = await Promise.all([
        professionalVisitsApi.list({ status: 'WAITING', page: 1, pageSize }, signal),
        professionalVisitsApi.list({ status: 'IN_SERVICE', page: 1, pageSize }, signal),
        professionalVisitsApi.list({ status: 'ENDED', page: 1, pageSize, from: todayBoundary(), to: todayBoundary(true) }, signal),
      ])
      setResults(current => ({ ...current, WAITING: waitingRes, IN_SERVICE: inServiceRes, ENDED: endedRes }))
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError')
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os atendimentos.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  const upsert = (visit: VisitDto) => setResults(current => {
    const next: Record<VisitStatus, PagedResponse<VisitDto>> = { ...current }
    for (const status of Object.keys(next) as VisitStatus[]) {
      next[status] = { ...next[status], items: next[status].items.filter(item => item.id !== visit.id) }
    }
    next[visit.status] = { ...next[visit.status], items: [visit, ...next[visit.status].items] }
    return next
  })

  const failure = async (reason: unknown) => {
    if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') {
      await load()
      setError('O atendimento foi alterado por outra operação. A lista foi atualizada.')
      return
    }
    setError(reason instanceof Error ? reason.message : 'Não foi possível concluir a operação.')
  }

  const transition = async (visit: VisitDto, action: 'start' | 'end' | 'cancel') => {
    try {
      upsert(await professionalVisitsApi[action](visit.id, visit.concurrencyToken))
    } catch (reason) {
      await failure(reason)
    }
  }

  return <div className="page-enter">
    <PageHeader eyebrow="Fluxo operacional" title="Atendimentos" description="Acompanhe seus atendimentos do dia." />
    {loading ? <div className="empty-state" role="status">Carregando atendimentos…</div>
      : <>
        {error && <p className="form-error" role="alert">{error}</p>}
        {sections.map(section => {
          const result = results[section.key]
          return <section className="panel table-panel professional-section" key={section.key}>
            <div className="table-toolbar">
              <h2>{section.title}</h2>
              <span>{result.totalCount} atendimento{result.totalCount === 1 ? '' : 's'}</span>
            </div>
            {result.items.length === 0
              ? <EmptyState>Nenhum atendimento nesta seção.</EmptyState>
              : <div className="table-scroll"><table className="data-table"><thead><tr>
                <th>Visitante</th><th>Sala</th><th>Chegada</th><th>Status</th><th>Ações</th>
              </tr></thead><tbody>{result.items.map(visit => <tr key={visit.id}>
                <td><strong>{visit.visitorName}</strong></td>
                <td>{visit.roomName ?? 'Não informada'}</td>
                <td><span className="time-pill"><Clock3 size={14} /> {formatDate(visit.arrivedAt)}</span></td>
                <td><StatusBadge tone={statusTones[visit.status]} label={statusLabels[visit.status]} /></td>
                <td><div className="room-admin-actions">
                  {visit.status === 'WAITING' && <button className="secondary-button" aria-label={`Iniciar atendimento de ${visit.visitorName}`} onClick={() => void transition(visit, 'start')}>Iniciar atendimento</button>}
                  {visit.status === 'IN_SERVICE' && <button className="secondary-button" aria-label={`Encerrar atendimento de ${visit.visitorName}`} onClick={() => void transition(visit, 'end')}>Encerrar atendimento</button>}
                  {(visit.status === 'WAITING' || visit.status === 'IN_SERVICE') && <button className="ghost-button" aria-label={`Cancelar atendimento de ${visit.visitorName}`} onClick={() => void transition(visit, 'cancel')}>Cancelar</button>}
                </div></td>
              </tr>)}</tbody></table></div>}
          </section>
        })}
      </>}
  </div>
}
