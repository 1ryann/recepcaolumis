import { CalendarDays } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { professionalReservationsApi, professionalVisitsApi, type ReservationDto, type VisitDto } from '../../api/modules'

function todayWindow() {
  const start = new Date()
  start.setHours(0, 0, 0, 0)
  const end = new Date(start.getTime() + 24 * 60 * 60 * 1000)
  return { from: start.toISOString(), to: end.toISOString() }
}

function weekWindow() {
  const now = new Date()
  const start = new Date(now)
  start.setHours(0, 0, 0, 0)
  // Week starts on Sunday, matching the pt-BR calendar convention used elsewhere in this app.
  start.setDate(start.getDate() - start.getDay())
  const end = new Date(start.getTime() + 7 * 24 * 60 * 60 * 1000)
  return { from: start.toISOString(), to: end.toISOString() }
}

function deriveAgendaStatus(reservationItem: ReservationDto, visits: VisitDto[]) {
  if (reservationItem.status === 'CANCELLED') return 'Cancelado'
  const matched = visits.find((item) => item.reservationId === reservationItem.id)
  if (!matched) return 'Agendado'
  if (matched.status === 'CANCELLED') return 'Cancelado'
  if (matched.status === 'ENDED') return 'Concluído'
  if (matched.status === 'IN_SERVICE') return 'Em atendimento'
  if (matched.status === 'WAITING') return 'Aguardando'
  return 'Agendado'
}

function agendaStatusClass(label: string) {
  if (label === 'Concluído') return 'status-approved'
  if (label === 'Em atendimento') return 'status-approved'
  if (label === 'Aguardando') return 'status-pending'
  if (label === 'Cancelado') return 'status-cancelled'
  return 'status-pending'
}

function timeLabel(value: string) { return new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }) }
function shortDateLabel(value: string) { return new Date(value).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' }) }

// Priority order for the name shown against each appointment, all backed by real recorded data:
//   1. Customer.Name via the reservation's own linked customer, when present.
//   2. Visit.visitorName, when a Visit already exists for that reservation but has no linked customer.
//   3. Neither exists yet: an explicit, honest absence marker — never a fabricated/generic label.
function resolveDisplayName(reservationItem: ReservationDto, visits: VisitDto[]): string | null {
  if (reservationItem.customerName) return reservationItem.customerName
  const matched = visits.find((item) => item.reservationId === reservationItem.id)
  return matched?.visitorName ?? null
}

export function ProfessionalAgenda() {
  const [view, setView] = useState<'today' | 'week'>('today')
  const [reservations, setReservations] = useState<ReservationDto[]>([])
  const [visits, setVisits] = useState<VisitDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    setLoading(true)
    const { from, to } = view === 'today' ? todayWindow() : weekWindow()
    Promise.all([
      professionalReservationsApi.list({ status: 'all', from, to, orderBy: 'asc', page: 1, pageSize: 100 }),
      professionalVisitsApi.list({ status: 'all', from, to, page: 1, pageSize: 100 }),
    ]).then(([reservationsPage, visitsPage]) => {
      if (!active) return
      setReservations(reservationsPage.items)
      setVisits(visitsPage.items)
    }).catch(() => { if (active) setError('Não foi possível carregar sua agenda agora.') })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [view])

  const grouped = useMemo(() => {
    const sorted = [...reservations].sort((a, b) => a.startAt.localeCompare(b.startAt))
    const byDay = new Map<string, ReservationDto[]>()
    for (const item of sorted) {
      const key = new Date(item.startAt).toISOString().slice(0, 10)
      const bucket = byDay.get(key)
      if (bucket) bucket.push(item)
      else byDay.set(key, [item])
    }
    return [...byDay.entries()]
  }, [reservations])

  return (
    <section className="professional-section page-enter">
      <div className="page-header">
        <div>
          <span className="page-eyebrow">Agenda</span>
          <h1>Sua agenda</h1>
          <p>Seus compromissos, com o nome real de quem está agendado.</p>
        </div>
        <div className="professional-agenda-view-toggle" role="group" aria-label="Alternar período da agenda">
          <button type="button" className={view === 'today' ? 'is-active' : ''} onClick={() => setView('today')}>Hoje</button>
          <button type="button" className={view === 'week' ? 'is-active' : ''} onClick={() => setView('week')}>Semana</button>
        </div>
      </div>
      {error && <div className="form-error" role="alert">{error}</div>}
      {loading
        ? <div className="professional-loading">Carregando agenda…</div>
        : grouped.length === 0
          ? <div className="professional-empty panel"><CalendarDays size={25} /><span>Nenhum compromisso encontrado.</span></div>
          : <div className="professional-agenda-list">
              {grouped.map(([day, items]) => (
                <div className="professional-agenda-day-group" key={day}>
                  {view === 'week' && <h2 className="professional-agenda-day-heading">{shortDateLabel(items[0].startAt)}</h2>}
                  {items.map((item) => {
                    const label = deriveAgendaStatus(item, visits)
                    const displayName = resolveDisplayName(item, visits)
                    return (
                      <article className="professional-agenda-row panel" key={item.id}>
                        <div className="professional-agenda-date">
                          <strong>{new Date(item.startAt).toLocaleDateString('pt-BR', { day: '2-digit' })}</strong>
                          <span>{new Date(item.startAt).toLocaleDateString('pt-BR', { month: 'short' })}</span>
                        </div>
                        <div>
                          <strong>{item.roomName}</strong>
                          <span>{timeLabel(item.startAt)} até {timeLabel(item.endAt)}</span>
                          {displayName
                            ? <span className="professional-agenda-customer-name">{displayName}</span>
                            : <span aria-label="Cliente não identificado" className="professional-agenda-customer-name professional-agenda-customer-unknown">—</span>}
                        </div>
                        <b className={`customer-status ${agendaStatusClass(label)}`}>{label}</b>
                      </article>
                    )
                  })}
                </div>
              ))}
            </div>}
    </section>
  )
}
