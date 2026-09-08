import { apiClient, ApiError } from '../../api/client'
import { Activity, CalendarClock, CalendarDays, ChevronRight, Clock3, DoorOpen, LayoutDashboard, LogOut, UserRound, UsersRound } from 'lucide-react'
import { type ReactNode, useEffect, useMemo, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate, useOutletContext } from 'react-router-dom'
import { useSession } from '../../auth/SessionProvider'
import { professionalReservationsApi, professionalVisitsApi, type PagedResponse, type ReservationDto, type VisitDto } from '../../api/modules'

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
  return <div className="professional-shell"><aside className="professional-sidebar"><Link className="professional-brand" to="/profissional"><img src="/lumis-logo-transparent.png" alt="LUMIS" /></Link><div className="professional-intro"><span>Área do profissional</span><strong>{name}</strong><small>{profile?.profession}</small>{profile?.description && <p>{profile.description}</p>}{profile?.photoUrl && <img src={profile.photoUrl} alt="Sua foto" width="72" height="72" />}</div><nav className="professional-nav" aria-label="Navegação do profissional">{nav.map(({ to, label, icon: Icon, end }) => <NavLink key={to} to={to} end={end}><Icon size={18} />{label}</NavLink>)}</nav><div className="professional-sidebar-footer"><span>{initials}</span><div><strong>{name}</strong><small>Profissional</small></div><button type="button" onClick={logout} aria-label="Sair"><LogOut size={17} /></button></div></aside><main className="professional-main"><header className="professional-topbar"><div><span className="eyebrow">{new Date().toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' })}</span><h1>{locationTitle(useLocation().pathname)}</h1></div><div className="professional-topbar-avatar">{initials}</div></header><div className="professional-content"><Outlet context={{ reservations, visits, loading, error }} /></div></main></div>
}

export function ProfessionalDashboard() {
  const { reservations, visits, loading, error } = useProfessionalContext()
  const today = new Date(); const dayStart = new Date(today.getFullYear(), today.getMonth(), today.getDate()).getTime(); const dayEnd = dayStart + 86400000
  const todayReservations = reservations.filter((item) => { const time = new Date(item.startAt).getTime(); return time >= dayStart && time < dayEnd })
  const waiting = visits.filter((item) => item.status === 'WAITING')
  const inService = visits.filter((item) => item.status === 'IN_SERVICE')
  const next = useMemo(() => reservations.filter((item) => item.status === 'APPROVED' && new Date(item.endAt).getTime() >= Date.now()).sort((a, b) => a.startAt.localeCompare(b.startAt))[0], [reservations])
  return <div className="professional-dashboard page-enter"><section className="professional-hero"><div><span className="eyebrow">Visão do dia</span><h2>Seu atendimento começa com uma boa leitura do tempo.</h2><p>Veja o que está acontecendo agora e prepare o próximo encontro.</p></div><div className="professional-hero-mark"><Clock3 size={27} /><span>America/Porto Velho</span></div></section>{error && <div className="form-error" role="alert">{error}</div>}<div className="professional-metrics"><Metric icon={<CalendarDays size={20} />} label="Reservas hoje" value={todayReservations.length} tone="blue" /><Metric icon={<UsersRound size={20} />} label="Aguardando" value={waiting.length} tone="mint" /><Metric icon={<Activity size={20} />} label="Em atendimento" value={inService.length} tone="amber" /></div><div className="professional-dashboard-grid"><section className="panel professional-next"><div className="panel-header"><div><h2>Próximo compromisso</h2><p>O que vem a seguir na sua agenda.</p></div><CalendarDays size={20} /></div>{loading ? <div className="professional-loading">Carregando agenda…</div> : next ? <div className="professional-next-card"><div className="professional-next-time"><strong>{new Date(next.startAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</strong><span>{new Date(next.startAt).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })}</span></div><div><strong>{next.roomName}</strong><span>{next.endAt && `até ${new Date(next.endAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}`}</span></div><ChevronRight size={18} /></div> : <div className="professional-empty"><CalendarDays size={25} /><span>Nenhum compromisso próximo.</span></div>}</section><section className="panel professional-waiting"><div className="panel-header"><div><h2>Atendimentos atuais</h2><p>Visitas que pedem sua atenção.</p></div><Link to="/profissional/atendimentos" className="text-link">Ver todos <ChevronRight size={14} /></Link></div>{waiting.length + inService.length === 0 ? <div className="professional-empty"><UsersRound size={25} /><span>Nenhum atendimento em andamento.</span></div> : <div className="professional-visit-list">{[...waiting, ...inService].slice(0, 4).map((visit) => <div className="professional-visit-row" key={visit.id}><span className={`professional-status-dot ${visit.status === 'WAITING' ? 'is-waiting' : 'is-service'}`} /><div><strong>{visit.visitorName}</strong><small>{visit.status === 'WAITING' ? 'Aguardando atendimento' : 'Em atendimento'}</small></div><span>{new Date(visit.arrivedAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</span></div>)}</div>}</section></div></div>
}

export function ProfessionalAgenda() {
  const { reservations, loading, error } = useProfessionalContext()
  const sorted = [...reservations].filter((item) => item.status !== 'CANCELLED').sort((a, b) => a.startAt.localeCompare(b.startAt))
  return <section className="professional-section page-enter"><div className="page-header"><div><span className="page-eyebrow">Agenda</span><h1>Seus próximos horários</h1><p>Uma visão simples dos compromissos que dependem de você.</p></div></div>{error && <div className="form-error" role="alert">{error}</div>}{loading ? <div className="professional-loading">Carregando agenda…</div> : sorted.length === 0 ? <div className="professional-empty panel"><CalendarDays size={25} /><span>Nenhum compromisso encontrado.</span></div> : <div className="professional-agenda-list">{sorted.map((item) => <article className="professional-agenda-row panel" key={item.id}><div className="professional-agenda-date"><strong>{new Date(item.startAt).toLocaleDateString('pt-BR', { day: '2-digit' })}</strong><span>{new Date(item.startAt).toLocaleDateString('pt-BR', { month: 'short' })}</span></div><div><strong>{new Date(item.startAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })} — {new Date(item.endAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</strong><span>{item.roomName}</span></div><b className={`customer-status status-${item.status.toLowerCase()}`}>{item.status}</b></article>)}</div>}</section>
}

export function ProfessionalPlaceholder({ title }: { title: string }) { return <section className="professional-section page-enter"><span className="page-eyebrow">Área do profissional</span><h1>{title}</h1><div className="professional-empty panel"><Activity size={25} /><strong>Estamos preparando esta área</strong><span>O dashboard e a agenda já estão disponíveis.</span><Link className="secondary-button" to="/profissional">Voltar ao dashboard</Link></div></section> }

function Metric({ icon, label, value, tone }: { icon: ReactNode, label: string, value: number, tone: string }) { return <article className={`professional-metric tone-${tone}`}><span>{icon}</span><div><small>{label}</small><strong>{value}</strong></div></article> }
function useProfessionalContext() { return useOutletContext<ProfessionalContext>() }
function locationTitle(path: string) { return nav.find((item) => item.end ? path === item.to : path.startsWith(item.to))?.label ?? 'Área do profissional' }
