import { ArrowRight, CalendarPlus, CalendarRange, LayoutDashboard, LogOut, Menu, UserRound } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useNavigate, useOutletContext } from 'react-router-dom'
import { useSession } from '../../auth/SessionProvider'
import { customerApi, type CustomerProfileDto, type PagedResponse, type ReservationDto } from '../../api/modules'
import { ApiError } from '../../api/client'
import { LumisPageShell } from '../../features/lumis/LumisPageShell'

const customerNavItems = [
  { to: '/cliente', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/cliente/agendamentos', label: 'Agendamentos', icon: CalendarRange, end: false },
  { to: '/cliente/agendar', label: 'Novo agendamento', icon: CalendarPlus, end: false },
]

function CustomerShell() {
  const [open, setOpen] = useState(false)
  const session = useSession()
  const navigate = useNavigate()
  const [profile, setProfile] = useState<CustomerProfileDto | null>(null)
  const [error, setError] = useState('')
  useEffect(() => { customerApi.me().then(setProfile).catch((caught) => { if (caught instanceof ApiError && caught.status === 401) navigate('/cliente/login', { replace: true }); else setError('Não foi possível carregar sua área.') }) }, [navigate])
  const logout = async () => { await session.logout(); navigate('/cliente/login', { replace: true }) }
  const name = profile?.name || session.user?.displayName || 'Cliente'
  const firstName = name.split(/\s+/)[0]
  const initials = name.split(/\s+/).slice(0, 2).map((part) => part[0]).join('').toUpperCase()
  const todayLabel = new Date().toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: 'long' })
  const closeMenu = () => setOpen(false)

  return (
    <LumisPageShell intensity="muted" className="customer-shell-page">
      <div className="customer-shell">
        <aside className={`customer-sidebar ${open ? 'is-open' : ''}`}>
          <div className="customer-sidebar-brand">
            <Link to="/cliente" onClick={closeMenu}><img className="customer-sidebar-logo" src="/lumis-logo-transparent.png" alt="LUMIS" /></Link>
          </div>
          <nav className="customer-sidebar-nav" aria-label="Navegação do cliente">
            {customerNavItems.map(({ to, label, icon: Icon, end }) => (
              <NavLink key={to} to={to} end={end} onClick={closeMenu}><Icon size={18} />{label}</NavLink>
            ))}
          </nav>
          <div className="customer-sidebar-footer">
            <button className="customer-logout" type="button" onClick={logout}><LogOut size={17} /> Sair</button>
          </div>
        </aside>
        {open && <button className="customer-sidebar-overlay" type="button" onClick={closeMenu} aria-label="Fechar menu" />}
        <div className="customer-main">
          <header className="customer-topbar">
            <button className="menu-trigger icon-button" type="button" onClick={() => setOpen(true)} aria-label="Abrir menu"><Menu size={20} /></button>
            <div className="customer-topbar-heading">
              <span className="eyebrow">Área do cliente</span>
              <h1>Olá, {firstName}!</h1>
              <p>Seu bem-estar em um só lugar.</p>
            </div>
            <div className="customer-topbar-meta">
              <span className="customer-topbar-date">{todayLabel}</span>
              <div className="customer-topbar-profile"><span className="customer-avatar">{initials}</span><strong>{name}</strong></div>
            </div>
          </header>
          {error && <div className="customer-inline-error" role="alert">{error}</div>}
          <main className="customer-content"><Outlet context={{ profile, firstName }} /></main>
        </div>
      </div>
    </LumisPageShell>
  )
}

export function CustomerHome() {
  const { profile, firstName } = useCustomerOutlet()
  return <div className="customer-home page-enter"><section className="customer-welcome"><span className="eyebrow">Área do cliente</span><h1>Olá, {firstName}.</h1><p>Seu próximo cuidado começa com um pequeno passo.</p><div className="customer-home-actions"><Link className="primary-button" to="/cliente/agendar"><CalendarPlus size={18} /> Agendar atendimento <ArrowRight size={17} /></Link><Link className="secondary-button" to="/cliente/agendamentos"><CalendarRange size={18} /> Meus agendamentos</Link></div></section><section className="customer-home-card panel"><div className="customer-home-card-icon"><UserRound size={20} /></div><div><strong>Seu cadastro está pronto</strong><p>{profile?.phone ? `Telefone confirmado: ${profile.phone}` : 'Mantenha seus dados sempre atualizados.'}</p></div></section></div>
}

export function CustomerReservations() {
  const [data, setData] = useState<PagedResponse<ReservationDto> | null>(null)
  const [error, setError] = useState('')
  useEffect(() => { customerApi.reservations({ page: 1, pageSize: 20 }).then(setData).catch(() => setError('Não foi possível carregar seus agendamentos.')) }, [])
  return <section className="customer-section page-enter"><div className="customer-section-heading"><div><span className="eyebrow">Sua agenda</span><h1>Meus agendamentos</h1><p>Acompanhe seus próximos horários no LUMIS.</p></div><Link className="primary-button" to="/cliente/agendar"><CalendarPlus size={17} /> Novo agendamento</Link></div>{error && <div className="form-error" role="alert">{error}</div>}{!data && !error && <div className="customer-loading" role="status">Carregando seus horários…</div>}{data?.items.length === 0 && <div className="customer-empty panel"><CalendarRange size={25} /><strong>Nenhum agendamento ainda</strong><span>Escolha um profissional para começar.</span></div>}{data && data.items.length > 0 && <div className="customer-reservation-list">{data.items.map((reservation) => <Link className="customer-reservation-card panel" key={reservation.id} to={`/cliente/agendamentos/${reservation.id}`}><div><span>{new Date(reservation.startAt).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })}</span><strong>{new Date(reservation.startAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</strong></div><section><strong>{reservation.professionalName}</strong><span>{reservation.roomName}</span></section><b className={`customer-status status-${reservation.status.toLowerCase()}`}>{reservation.status === 'APPROVED' ? 'Confirmado' : reservation.status}</b></Link>)}</div>}</section>
}

export function CustomerComingSoon({ title }: { title: string }) { return <section className="customer-section page-enter"><span className="eyebrow">Próxima etapa</span><h1>{title}</h1><div className="customer-empty panel"><CalendarRange size={25} /><strong>Estamos preparando essa experiência</strong><span>Em breve você poderá escolher o melhor horário por aqui.</span><Link className="secondary-button" to="/cliente">Voltar para o início</Link></div></section> }

function useCustomerOutlet() { return useOutletContext<{ profile: CustomerProfileDto | null, firstName: string }>() }

export { CustomerShell }
