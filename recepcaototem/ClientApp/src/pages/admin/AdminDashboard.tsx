import { AlertTriangle, CalendarCheck2, DoorOpen, UserRoundCheck, UsersRound } from 'lucide-react'
import { useEffect, useState } from 'react'
import { dashboardApi, receptionApi } from '../../api/modules'
import type { DashboardSnapshotDto, ReceptionProfessionalDto } from '../../api/modules'
import { EmptyState, PageHeader } from '../../components/PageElements'

const ROOM_STATUS_LABEL: Record<string, string> = {
  OCCUPIED: 'Em uso',
  RESERVED: 'Reservada',
  AVAILABLE: 'Livre',
  CLOSED: 'Fechada',
  BLOCKED: 'Bloqueada',
}

const ROOM_STATUS_TONE: Record<string, 'occupied' | 'pending' | 'available' | 'inactive' | 'overdue'> = {
  OCCUPIED: 'occupied',
  RESERVED: 'pending',
  AVAILABLE: 'available',
  CLOSED: 'inactive',
  BLOCKED: 'overdue',
}

function formatTime(value: string | null) {
  if (!value) return null
  return new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
}

export function AdminDashboard() {
  const [snapshot, setSnapshot] = useState<DashboardSnapshotDto | null>(null)
  const [professionals, setProfessionals] = useState<ReceptionProfessionalDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    const controller = new AbortController()
    Promise.all([
      dashboardApi.get(controller.signal),
      receptionApi.professionals(controller.signal),
    ]).then(([nextSnapshot, nextProfessionals]) => {
      if (!active) return
      setSnapshot(nextSnapshot)
      setProfessionals(nextProfessionals)
    }).catch(() => { if (active) setError('Não foi possível carregar o painel administrativo agora.') })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false; controller.abort() }
  }, [])

  const counts = snapshot?.counts
  const presentProfessionals = professionals.filter((item) => item.presence === 'PRESENT')

  return (
    <div className="page-enter admin-dashboard">
      <PageHeader eyebrow="Visão geral" title="Painel administrativo"
        description="Indicadores operacionais de hoje, direto da recepção." />
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="metrics-grid">
        <article className="metric-card">
          <span className="metric-icon tone-blue"><CalendarCheck2 size={18} /></span>
          <div>
            <small>Atendimentos hoje</small>
            <strong>{loading ? '—' : counts?.todayReservations ?? 0}</strong>
            {!loading && <p>{counts?.pendingReservations ?? 0} aguardando aprovação</p>}
          </div>
        </article>
        <article className="metric-card">
          <span className="metric-icon tone-green"><UserRoundCheck size={18} /></span>
          <div><small>Check-ins realizados</small><strong>{loading ? '—' : counts?.todayCheckIns ?? 0}</strong></div>
        </article>
        <article className="metric-card">
          <span className="metric-icon tone-navy"><DoorOpen size={18} /></span>
          <div><small>Salas ocupadas</small><strong>{loading ? '—' : `${counts?.occupiedRooms ?? 0}/${counts?.activeRooms ?? 0}`}</strong></div>
        </article>
        <article className="metric-card">
          <span className="metric-icon tone-amber"><CalendarCheck2 size={18} /></span>
          <div><small>Reservas hoje</small><strong>{loading ? '—' : counts?.todayReservations ?? 0}</strong></div>
        </article>
      </div>

      <div className="dashboard-grid">
        <section className="panel table-panel" data-testid="admin-current-visits">
          <div className="panel-header"><div><h2>Recepção · Em atendimento</h2><p>Quem está sendo atendido agora.</p></div></div>
          {loading ? <div className="empty-state" role="status">Carregando…</div>
            : (snapshot?.currentVisits.length ?? 0) === 0
              ? <EmptyState><UsersRound size={22} /><span>Ninguém em atendimento no momento.</span></EmptyState>
              : <div className="table-scroll"><table className="data-table">
                  <thead><tr><th>#</th><th>Cliente</th><th>Profissional</th><th>Status</th><th>Espera</th></tr></thead>
                  <tbody>{snapshot!.currentVisits.map((visit, index) => (
                    <tr key={visit.visitId}>
                      <td>{index + 1}</td>
                      <td>{visit.visitorName}</td>
                      <td>{visit.professionalName}</td>
                      <td>{visit.status}</td>
                      <td>{visit.durationMinutes} min</td>
                    </tr>
                  ))}</tbody>
                </table></div>}
        </section>

        <div className="dashboard-side">
          <section className="panel" data-testid="admin-room-status">
            <div className="panel-header"><div><h2>Ocupação das salas</h2><p>Status atual de cada sala.</p></div></div>
            {loading ? <div className="empty-state" role="status">Carregando…</div>
              : (snapshot?.rooms.length ?? 0) === 0
                ? <EmptyState><DoorOpen size={22} /><span>Nenhuma sala cadastrada.</span></EmptyState>
                : <div className="admin-mini-list">{snapshot!.rooms.map((room) => (
                    <div className="admin-mini-row" key={room.roomId}>
                      <div><strong>{room.roomName}</strong>{room.nextCommitmentAt && <small>Próx.: {formatTime(room.nextCommitmentAt)}</small>}</div>
                      <span className={`status-badge status-${ROOM_STATUS_TONE[room.status] ?? 'inactive'}`}><i />{ROOM_STATUS_LABEL[room.status] ?? room.status}</span>
                    </div>
                  ))}</div>}
          </section>

          <section className="panel" data-testid="admin-professionals-present">
            <div className="panel-header"><div><h2>Profissionais presentes</h2><p>Quem está fisicamente presente agora.</p></div></div>
            {loading ? <div className="empty-state" role="status">Carregando…</div>
              : presentProfessionals.length === 0
                ? <EmptyState><UsersRound size={22} /><span>Nenhum profissional presente no momento.</span></EmptyState>
                : <div className="admin-mini-list">{presentProfessionals.map((item) => (
                    <div className="admin-mini-row" key={item.professionalId}>
                      <div><strong>{item.name}</strong><small>{item.profession}</small></div>
                    </div>
                  ))}</div>}
          </section>

          <section className="panel" data-testid="admin-alerts">
            <div className="panel-header"><div><h2>Atividades/Alertas</h2><p>Avisos operacionais recentes.</p></div><AlertTriangle size={18} /></div>
            <div className="admin-alert-stats">
              <div><strong>{loading ? '—' : snapshot?.alerts.total ?? 0}</strong><span>Total</span></div>
              <div><strong>{loading ? '—' : snapshot?.alerts.warning ?? 0}</strong><span>Atenção</span></div>
              <div><strong>{loading ? '—' : snapshot?.alerts.critical ?? 0}</strong><span>Críticos</span></div>
            </div>
            {loading ? null
              : (snapshot?.alerts.recent.length ?? 0) === 0
                ? <EmptyState><AlertTriangle size={22} /><span>Nenhum alerta no momento.</span></EmptyState>
                : <div className="admin-mini-list">{snapshot!.alerts.recent.map((alert) => (
                    <div className="admin-mini-row" key={alert.id}>
                      <div><strong>{alert.title}</strong><small>{alert.message}</small></div>
                    </div>
                  ))}</div>}
          </section>
        </div>
      </div>
    </div>
  )
}
