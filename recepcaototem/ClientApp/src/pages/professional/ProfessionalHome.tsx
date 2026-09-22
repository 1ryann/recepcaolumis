import { FallbackImage } from '../../components/FallbackImage'
import { apiClient, ApiError } from '../../api/client'
import { Activity, AlertTriangle, CalendarClock, CalendarDays, Clock3, DoorOpen, LayoutDashboard, LogOut, Menu, UserRound, UserRoundCheck, UsersRound } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, NavLink, Outlet, useNavigate, useOutletContext } from 'react-router-dom'
import { useSession } from '../../auth/SessionProvider'
import { professionalAvailabilityApi, professionalReservationsApi, professionalVisitsApi, type AvailabilityIntervalDto, type ProfessionalAvailabilityDto, type ReservationDto, type VisitDto } from '../../api/modules'
import { LumisPageShell } from '../../features/lumis/LumisPageShell'
import { LumisLogo } from '../../theme/LumisLogo'
import { ReportIncident } from '../../features/professionals/ReportIncident'

type ProfessionalContext = { reservations: ReservationDto[], visits: VisitDto[], loading: boolean, error: string }
const nav = [
  { to: '/profissional', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/profissional/agenda', label: 'Agenda', icon: CalendarDays },
  { to: '/profissional/reservas', label: 'Reservas', icon: CalendarDays },
  { to: '/profissional/atendimentos', label: 'Atendimentos', icon: UsersRound },
  { to: '/profissional/locacoes', label: 'Locações', icon: DoorOpen },
  { to: '/profissional/disponibilidade', label: 'Disponibilidade', icon: CalendarClock },
  { to: '/profissional/financeiro', label: 'Financeiro', icon: Activity },
  { to: '/profissional/perfil', label: 'Meu perfil', icon: UserRound },
]

export function ProfessionalShell() {
  const [open, setOpen] = useState(false)
  const session = useSession()
  const navigate = useNavigate()
  const [profile, setProfile] = useState<{ name: string; profession: string; description: string | null; photoUrl: string | null } | null>(null)
  const [reservations, setReservations] = useState<ReservationDto[]>([])
  const [visits, setVisits] = useState<VisitDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  useEffect(() => {
    let active = true
    Promise.all([
      apiClient.get<{ name: string; profession: string; description: string | null; photoUrl: string | null }>('/api/professional/me'),
      professionalReservationsApi.list({ status: 'all', page: 1, pageSize: 50 }),
      professionalVisitsApi.list({ status: 'all', page: 1, pageSize: 50 }),
    ]).then(([currentProfile, reservationPage, visitPage]) => { if (active) { setProfile(currentProfile); setReservations(reservationPage.items); setVisits(visitPage.items) } }).catch((failure) => { if (active) setError(failure instanceof ApiError && failure.code === 'PROFESSIONAL_PROFILE_NOT_LINKED' ? 'Seu acesso profissional não está vinculado corretamente. Procure a gerência.' : 'Não foi possível carregar sua agenda agora.') }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])
  const logout = async () => { await session.logout(); navigate('/login', { replace: true }) }
  const name = profile?.name || session.user?.displayName || 'Profissional'
  const initials = name.split(/\s+/).slice(0, 2).map((part) => part[0]).join('').toUpperCase()
  const todayLabel = new Date().toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: 'long' })
  const closeMenu = () => setOpen(false)
  return (
    <LumisPageShell intensity="muted" className="professional-shell-page">
      <div className="professional-shell">
        <aside className={`professional-sidebar ${open ? 'is-open' : ''}`}>
          <div className="professional-sidebar-brand">
            <Link to="/profissional" onClick={closeMenu}><LumisLogo className="professional-sidebar-logo" alt="LUMIS" /></Link>
          </div>
          <div className="professional-intro">
            <span>Área do profissional</span>
            <strong>{name}</strong>
            {profile?.profession && <small>{profile.profession}</small>}
            {profile?.description && <p>{profile.description}</p>}
            {profile?.photoUrl && <FallbackImage className="professional-intro-photo" src={profile.photoUrl} alt="Sua foto" width="56" height="56" fallback={null} />}
          </div>
          <nav className="professional-nav" aria-label="Navegação do profissional">
            {nav.map(({ to, label, icon: Icon, end }) => <NavLink key={to} to={to} end={end} onClick={closeMenu}><Icon size={18} />{label}</NavLink>)}
          </nav>
          <div className="professional-sidebar-footer">
            <button className="professional-logout" type="button" onClick={logout}><LogOut size={17} /> Sair</button>
          </div>
        </aside>
        {open && <button className="professional-sidebar-overlay" type="button" onClick={closeMenu} aria-label="Fechar menu" />}
        <div className="professional-main">
          <header className="professional-topbar">
            <button className="menu-trigger icon-button" type="button" onClick={() => setOpen(true)} aria-label="Abrir menu"><Menu size={20} /></button>
            <div className="professional-topbar-meta">
              <span className="professional-topbar-date">{todayLabel}</span>
              <div className="professional-topbar-profile"><span className="professional-avatar">{initials}</span><strong>{name}</strong></div>
            </div>
          </header>
          {error && <div className="customer-inline-error" role="alert">{error}</div>}
          <div className="professional-content"><Outlet context={{ reservations, visits, loading, error }} /></div>
        </div>
      </div>
    </LumisPageShell>
  )
}

const WEEKDAY_CODES = ['SUNDAY', 'MONDAY', 'TUESDAY', 'WEDNESDAY', 'THURSDAY', 'FRIDAY', 'SATURDAY']

function todayWindow() {
  const start = new Date()
  start.setHours(0, 0, 0, 0)
  const end = new Date(start.getTime() + 24 * 60 * 60 * 1000)
  return { from: start.toISOString(), to: end.toISOString() }
}

function toMinutes(value: string) {
  const [hours, minutes] = value.split(':').map(Number)
  return hours * 60 + minutes
}

function intervalsForToday(availability: ProfessionalAvailabilityDto | null): AvailabilityIntervalDto[] {
  if (!availability) return []
  const code = WEEKDAY_CODES[new Date().getDay()]
  return availability.effectiveDays.find((day) => day.dayOfWeek.toUpperCase() === code)?.intervals ?? []
}

function formatAvailabilityDuration(intervals: AvailabilityIntervalDto[]) {
  const totalMinutes = intervals.reduce((sum, interval) => sum + (toMinutes(interval.endTime) - toMinutes(interval.startTime)), 0)
  if (totalMinutes <= 0) return 'Sem expediente hoje'
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  return minutes ? `${hours}h${String(minutes).padStart(2, '0')}` : `${hours}h`
}

function deriveAgendaStatus(reservationItem: ReservationDto, visitsForToday: VisitDto[]) {
  if (reservationItem.status === 'CANCELLED') return 'Cancelado'
  const matched = visitsForToday.find((item) => item.reservationId === reservationItem.id)
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

export function ProfessionalDashboard() {
  const { error } = useProfessionalContext()
  const [attendancesToday, setAttendancesToday] = useState(0)
  const [checkinsToday, setCheckinsToday] = useState(0)
  const [agendaToday, setAgendaToday] = useState<ReservationDto[]>([])
  const [visitsToday, setVisitsToday] = useState<VisitDto[]>([])
  const [availability, setAvailability] = useState<ProfessionalAvailabilityDto | null>(null)
  const [pendingRequests, setPendingRequests] = useState<ReservationDto[]>([])
  // Fetched separately from the 50-row "all reservations" context list: that list is
  // ordered furthest-future-first and can be truncated, so a professional with more
  // than 50 upcoming reservations would silently lose their true next appointment.
  // This is a small, ascending, from-now page instead.
  const [upcomingSoon, setUpcomingSoon] = useState<ReservationDto[]>([])
  const [dashLoading, setDashLoading] = useState(true)
  const [dashError, setDashError] = useState('')
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    let active = true
    const { from, to } = todayWindow()
    const nowIso = new Date().toISOString()
    Promise.all([
      professionalVisitsApi.list({ status: 'IN_SERVICE', from, to, page: 1, pageSize: 1 }),
      professionalVisitsApi.list({ status: 'ENDED', from, to, page: 1, pageSize: 1 }),
      professionalVisitsApi.list({ status: 'all', from, to, page: 1, pageSize: 1 }),
      professionalVisitsApi.list({ status: 'all', from, to, page: 1, pageSize: 100 }),
      professionalReservationsApi.list({ status: 'all', from, to, page: 1, pageSize: 100 }),
      professionalReservationsApi.list({ status: 'PENDING', page: 1, pageSize: 100 }),
      professionalReservationsApi.list({ status: 'APPROVED', from: nowIso, orderBy: 'asc', page: 1, pageSize: 10 }),
      professionalAvailabilityApi.get(),
    ]).then(([inServiceCount, endedCount, checkinsCount, visitsPage, reservationsPage, pendingPage, upcomingPage, availabilityDto]) => {
      if (!active) return
      setAttendancesToday(inServiceCount.totalCount + endedCount.totalCount)
      setCheckinsToday(checkinsCount.totalCount)
      setVisitsToday(visitsPage.items)
      setAgendaToday(reservationsPage.items)
      setPendingRequests(pendingPage.items)
      setUpcomingSoon(upcomingPage.items)
      setAvailability(availabilityDto)
    }).catch(() => { if (active) setDashError('Não foi possível carregar os indicadores de hoje.') })
      .finally(() => { if (active) setDashLoading(false) })
    return () => { active = false }
  }, [reloadKey])

  const next = upcomingSoon[0]
  const todaysIntervals = useMemo(() => intervalsForToday(availability), [availability])
  const availabilitySummary = formatAvailabilityDuration(todaysIntervals)
  const pendingSummaryCount = pendingRequests.filter((item) => item.kind === 'RESCHEDULE' || item.kind === 'CANCELLATION').length
  const sortedAgenda = useMemo(() => [...agendaToday].sort((a, b) => a.startAt.localeCompare(b.startAt)), [agendaToday])

  return (
    <div className="professional-dashboard page-enter">
      <section className="professional-dashboard-hero">
        <div>
          <span className="eyebrow">Visão do dia</span>
          <h2>Seu atendimento começa com uma boa leitura do tempo.</h2>
          <p>Veja o que está acontecendo agora e prepare o próximo encontro.</p>
        </div>
        <div className="professional-dashboard-hero-mark"><Clock3 size={27} /><span>America/Porto Velho</span></div>
      </section>
      {(error || dashError) && <div className="form-error" role="alert">{error || dashError}</div>}
      <div className="professional-kpi-grid">
        <article className="professional-kpi-card">
          <span className="professional-kpi-icon"><UsersRound size={20} /></span>
          <div><small>Atendimentos hoje</small><strong>{dashLoading ? '—' : attendancesToday}</strong></div>
        </article>
        <article className="professional-kpi-card">
          <span className="professional-kpi-icon"><UserRoundCheck size={20} /></span>
          <div><small>Check-ins confirmados hoje</small><strong>{dashLoading ? '—' : checkinsToday}</strong></div>
        </article>
        <article className="professional-kpi-card" data-testid="professional-kpi-next">
          <span className="professional-kpi-icon"><Clock3 size={20} /></span>
          <div><small>Próximo horário</small><strong>{next ? timeLabel(next.startAt) : '—'}</strong>{next && <span>{next.roomName}</span>}</div>
        </article>
        <article className="professional-kpi-card">
          <span className="professional-kpi-icon"><CalendarClock size={20} /></span>
          <div><small>Disponibilidade</small><strong>{dashLoading ? '—' : availabilitySummary}</strong></div>
        </article>
      </div>
      <div className="professional-dashboard-columns">
        <section className="professional-agenda-today-panel">
          <div className="panel-header"><div><h2>Agenda de hoje</h2><p>Seus compromissos de hoje, com o status mais recente.</p></div><ReportIncident onReported={() => setReloadKey(value => value + 1)} /></div>
          {dashLoading ? <div className="professional-loading">Carregando agenda de hoje…</div>
            : sortedAgenda.length === 0 ? <div className="professional-empty"><CalendarDays size={25} /><span>Nenhum compromisso hoje.</span></div>
            : <div className="professional-agenda-today" data-testid="professional-agenda-today">
                {sortedAgenda.map((item) => {
                  const label = deriveAgendaStatus(item, visitsToday)
                  return (
                    <div className="professional-agenda-today-row" key={item.id}>
                      <div className="professional-agenda-today-time"><strong>{timeLabel(item.startAt)}</strong><span>até {timeLabel(item.endAt)}</span></div>
                      <div><strong>{item.roomName}</strong><span>{item.professionalName}</span></div>
                      <b className={`customer-status ${agendaStatusClass(label)}`}>{label}</b>
                    </div>
                  )
                })}
              </div>}
        </section>
        <div className="professional-side-column">
          <section className="professional-side-panel">
            <div className="panel-header"><div><h2>Disponibilidade de hoje</h2><p>Seus intervalos abertos para hoje.</p></div></div>
            {dashLoading ? <div className="professional-loading">Carregando…</div>
              : todaysIntervals.length === 0 ? <div className="professional-empty"><CalendarClock size={22} /><span>Sem expediente hoje.</span></div>
              : <div className="professional-availability-bar">{todaysIntervals.map((interval) => <span className="professional-availability-interval" key={`${interval.startTime}-${interval.endTime}`}>{interval.startTime}–{interval.endTime}</span>)}</div>}
          </section>
          <section className="professional-side-panel">
            <div className="panel-header"><div><h2>Próximas reservas</h2><p>Seus próximos compromissos aprovados.</p></div></div>
            {upcomingSoon.length === 0 ? <div className="professional-empty"><CalendarDays size={22} /><span>Nenhuma reserva futura.</span></div>
              : <div className="professional-upcoming-list">{upcomingSoon.slice(0, 4).map((item) => <div className="professional-upcoming-row" key={item.id}><strong>{shortDateLabel(item.startAt)} · {timeLabel(item.startAt)}</strong><span>{item.roomName}</span></div>)}</div>}
          </section>
          <section className="professional-side-panel professional-alert-summary">
            <div className="panel-header"><div><h2>Resumo/avisos</h2><p>Solicitações aguardando sua atenção.</p></div><AlertTriangle size={20} /></div>
            <div className="professional-alert-count"><strong>{dashLoading ? '—' : pendingSummaryCount}</strong><span>reagendamento(s)/cancelamento(s) pendente(s)</span></div>
          </section>
        </div>
      </div>
    </div>
  )
}

function useProfessionalContext() { return useOutletContext<ProfessionalContext>() }
