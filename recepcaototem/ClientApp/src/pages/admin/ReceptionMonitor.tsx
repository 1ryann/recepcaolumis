import { Activity, ArrowRight, Clock3, RefreshCw, UserRound } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { receptionApi, type ReceptionOverviewDto, type ReceptionVisitDto } from '../../api/modules'

function timeLabel(value: string) { return new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }) }

export function ReceptionMonitor() {
  const location = useLocation()
  const operationsPrefix = location.pathname.startsWith('/admin') ? '/admin' : '/recepcao'
  const [overview, setOverview] = useState<ReceptionOverviewDto | null>(null)
  const [visits, setVisits] = useState<ReceptionVisitDto[]>([])
  const [error, setError] = useState('')
  const [refreshing, setRefreshing] = useState(false)
  const [mutating, setMutating] = useState('')
  const load = useCallback(async (silent = false) => {
    if (!silent) setRefreshing(true)
    try {
      const [nextOverview, nextVisits] = await Promise.all([receptionApi.overview(), receptionApi.visits({ status: 'all', page: 1, pageSize: 50 })])
      setOverview(nextOverview); setVisits(nextVisits.items); setError('')
    } catch (caught) {
      setError(caught instanceof ApiError && caught.status === 403 ? 'Sua conta não possui acesso à Recepção.' : 'Não foi possível atualizar a operação.')
    } finally { setRefreshing(false) }
  }, [])
  useEffect(() => { void load(); const timer = window.setInterval(() => void load(true), 10000); return () => window.clearInterval(timer) }, [load])
  const transition = async (visit: ReceptionVisitDto, action: 'start' | 'end') => {
    setMutating(visit.id); setError('')
    try {
      action === 'start' ? await receptionApi.startVisit(visit.id, visit.concurrencyToken) : await receptionApi.endVisit(visit.id, visit.concurrencyToken)
      await load(true)
    } catch (caught) {
      setError(caught instanceof ApiError && caught.code === 'RESOURCE_MODIFIED' ? 'A visita mudou em outra tela. Atualize e tente novamente.' : 'Não foi possível atualizar esta visita.')
    } finally { setMutating('') }
  }
  const activeVisits = visits.filter((visit) => visit.status === 'WAITING' || visit.status === 'IN_SERVICE')
  return <section className="admin-page reception-monitor-page">
    <div className="page-header"><div><span className="page-eyebrow">Operação ao vivo</span><h1>Recepção</h1><p>Veja quem chegou e acompanhe os atendimentos em andamento.</p></div><div className="page-header-actions"><Link className="secondary-button" to={`${operationsPrefix}/profissionais`}>Profissionais</Link><Link className="secondary-button" to={`${operationsPrefix}/configuracoes`}>Horários e salas</Link><Link className="secondary-button" to={`${operationsPrefix}/solicitacoes-profissionais`}>Solicitações de profissionais</Link><Link className="secondary-button" to={`${operationsPrefix}/whatsapp`}>WhatsApp dos clientes</Link><button className="secondary-button" type="button" onClick={() => void load()} disabled={refreshing}><RefreshCw size={16} className={refreshing ? 'spin-icon' : ''} /> Atualizar</button></div></div>
    {error && <div className="form-error" role="alert">{error}</div>}
    <div className="reception-monitor-metrics">{[['Aguardando', overview?.visitorsWaiting ?? '—'], ['Em atendimento', overview?.visitsInService ?? '—'], ['Reservas hoje', overview?.reservationsToday ?? '—']].map(([label, value]) => <div className="metric-card" key={label}><span className="metric-icon tone-green"><Activity size={18} /></span><div><small>{label}</small><strong>{value}</strong></div></div>)}</div>
    <div className="reception-monitor-grid"><section className="panel reception-live-panel"><div className="panel-header"><div><h2>Fila de atendimento</h2><p>Atualiza automaticamente a cada 10 segundos.</p></div><span className="reception-live-dot"><i /> ao vivo</span></div>
      {activeVisits.length === 0 && <div className="customer-empty"><UserRound size={24} /><strong>Ninguém aguardando agora</strong><span>Novos check-ins aparecerão aqui.</span></div>}
      <div className="reception-live-list">{activeVisits.map((visit) => <article className="reception-live-row" key={visit.id}><span className={`reception-live-status ${visit.status === 'WAITING' ? 'is-waiting' : 'is-service'}`} /><div><strong>{visit.visitorName}</strong><small>{visit.professionalName}{visit.roomName ? ` · ${visit.roomName}` : ''} · chegada {timeLabel(visit.arrivedAt)}</small></div><div className="reception-live-actions">{visit.status === 'WAITING' && <button className="primary-button" type="button" onClick={() => void transition(visit, 'start')} disabled={mutating === visit.id}>Iniciar <ArrowRight size={14} /></button>}{visit.status === 'IN_SERVICE' && <button className="secondary-button" type="button" onClick={() => void transition(visit, 'end')} disabled={mutating === visit.id}>Encerrar</button>}</div></article>)}</div>
    </section><section className="panel reception-quick-panel"><div className="panel-header"><div><h2>Estado agora</h2><p>Resumo da operação.</p></div></div><div className="reception-quick-row"><Clock3 size={17} /><span><small>Profissionais disponíveis</small><strong>{overview?.professionalsAvailable ?? '—'}</strong></span></div><div className="reception-quick-row"><UserRound size={17} /><span><small>Profissionais em atendimento</small><strong>{overview?.professionalsInService ?? '—'}</strong></span></div><div className="reception-quick-row"><Activity size={17} /><span><small>Alertas críticos</small><strong>{overview?.criticalAlerts ?? '—'}</strong></span></div></section></div>
  </section>
}
