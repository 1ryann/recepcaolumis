import { ArrowRight, CalendarDays, CalendarPlus, CalendarRange, CircleOff, Clock3, Copy, DoorOpen, KeyRound, LayoutDashboard, LogOut, Menu, MessageCircle, QrCode } from 'lucide-react'
import QRCode from 'qrcode'
import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useNavigate, useOutletContext } from 'react-router-dom'
import { useSession } from '../../auth/SessionProvider'
import { customerApi, whatsAppOptInApi, type CustomerProfileDto, type PagedResponse, type ReservationDto } from '../../api/modules'
import { WhatsAppOptInPanel } from '../../features/whatsapp/WhatsAppOptIn'
import { CUSTOMER_OPT_IN_TEXT } from '../../features/whatsapp/optInText'
import { ApiError } from '../../api/client'
import { LumisPageShell } from '../../features/lumis/LumisPageShell'
import { LumisLogo } from '../../theme/LumisLogo'

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
            <Link to="/cliente" onClick={closeMenu}><LumisLogo className="customer-sidebar-logo" alt="LUMIS" /></Link>
          </div>
          <nav className="customer-sidebar-nav" aria-label="Navegação do cliente">
            {customerNavItems.map(({ to, label, icon: Icon, end }) => (
              <NavLink key={to} to={to} end={end} onClick={closeMenu}><Icon size={18} />{label}</NavLink>
            ))}
          </nav>
          <div className="customer-sidebar-footer">
            <Link className="sidebar-account-link" to="/change-password" onClick={closeMenu}><KeyRound size={17} /> Alterar senha</Link>
            <button className="customer-logout" type="button" onClick={logout}><LogOut size={17} /> Sair</button>
          </div>
        </aside>
        {open && <button className="customer-sidebar-overlay" type="button" onClick={closeMenu} aria-label="Fechar menu" />}
        <div className="customer-main">
          <header className="customer-topbar">
            <button className="menu-trigger icon-button" type="button" onClick={() => setOpen(true)} aria-label="Abrir menu"><Menu size={20} /></button>
            <div className="customer-topbar-meta">
              <span className="customer-topbar-date">{todayLabel}</span>
              <div className="customer-topbar-profile"><span className="customer-avatar">{initials}</span><strong>{name}</strong></div>
            </div>
          </header>
          {error && <div className="customer-inline-error" role="alert">{error}</div>}
          {/* A deactivated customer keeps a live cookie until the session is revalidated, and every
              scheduling route answers 404 meanwhile — which read as "não foi possível carregar".
              Name the real reason instead of letting each page report a loading failure. */}
          {profile?.isActive === false
            ? <main className="customer-content"><div className="customer-empty panel" role="status">
                <CircleOff size={22} />
                <strong>Sua conta está inativa</strong>
                <span>Procure a recepção do LUMIS para reativá-la e voltar a agendar.</span>
              </div></main>
            : <main className="customer-content"><Outlet context={{ profile, firstName }} /></main>}
        </div>
      </div>
    </LumisPageShell>
  )
}

function dateLabel(value: string) { return new Date(value).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' }) }
function timeLabel(value: string) { return new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }) }
function fullDateLabel(value: string) { return new Date(value).toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: 'long' }) }

function ReservationRow({ reservation }: { reservation: ReservationDto }) {
  return (
    <Link className="customer-reservation-card panel" to={`/cliente/agendamentos/${reservation.id}`}>
      <div><span>{dateLabel(reservation.startAt)}</span><strong>{timeLabel(reservation.startAt)}</strong></div>
      <section><strong>{reservation.professionalName}</strong><span>{reservation.roomName}</span></section>
      <b className={`customer-status status-${reservation.status.toLowerCase()}`}>{reservation.status === 'APPROVED' ? 'Confirmado' : reservation.status}</b>
    </Link>
  )
}

export function CustomerHome() {
  const { firstName } = useCustomerOutlet()
  const [data, setData] = useState<PagedResponse<ReservationDto> | null>(null)
  const [error, setError] = useState('')
  useEffect(() => { customerApi.reservations({ page: 1, pageSize: 50 }).then(setData).catch(() => setError('Não foi possível carregar seus agendamentos.')) }, [])

  const [issuing, setIssuing] = useState(false)
  const [qrError, setQrError] = useState('')
  const [token, setToken] = useState('')
  const [manualCode, setManualCode] = useState('')
  const [qrDataUrl, setQrDataUrl] = useState('')

  const items = data?.items ?? []
  const now = Date.now()
  const nextAppointment = items
    .filter((reservation) => reservation.status === 'APPROVED' && Date.parse(reservation.endAt) >= now)
    .sort((a, b) => a.startAt.localeCompare(b.startAt))[0]
  const upcoming = items
    .filter((reservation) => Date.parse(reservation.endAt) >= now)
    .sort((a, b) => a.startAt.localeCompare(b.startAt))
  const history = items
    .filter((reservation) => Date.parse(reservation.endAt) < now)
    .sort((a, b) => b.startAt.localeCompare(a.startAt))

  const issueToken = async () => {
    if (!nextAppointment) return
    setIssuing(true); setQrError('')
    try {
      const result = await customerApi.issueCheckInToken(nextAppointment.id)
      setToken(result.token)
      setManualCode(result.manualCode)
      setQrDataUrl(await QRCode.toDataURL(result.token, { margin: 1, width: 280, color: { dark: '#292929', light: '#ffffff' } }))
    } catch (caught) {
      setQrError(caught instanceof ApiError && caught.code === 'CHECK_IN_NOT_ELIGIBLE'
        ? 'Disponível 1 h antes do horário e até o fim do atendimento.'
        : 'Não foi possível gerar o QR Code agora.')
    } finally { setIssuing(false) }
  }
  const copyCode = async () => { if (manualCode) await navigator.clipboard?.writeText(manualCode) }

  if (!data && !error) return <div className="customer-loading page-enter" role="status">Carregando seu painel…</div>
  if (error) return <div className="form-error page-enter" role="alert">{error}</div>

  return (
    <div className="customer-home page-enter">
      <div className="customer-dashboard-grid">
        <section className="customer-home-card panel customer-next-card">
          <div className="panel-header"><div><h2><CalendarDays size={18} /> Próximo atendimento</h2><p>Seu horário confirmado mais próximo.</p></div></div>
          {nextAppointment ? (
            <div className="customer-next-details">
              <strong>{nextAppointment.professionalName}</strong>
              <span><CalendarDays size={14} /> {fullDateLabel(nextAppointment.startAt)}</span>
              <span><Clock3 size={14} /> {timeLabel(nextAppointment.startAt)} – {timeLabel(nextAppointment.endAt)}</span>
              <span><DoorOpen size={14} /> {nextAppointment.roomName}</span>
              <b className={`customer-status status-${nextAppointment.status.toLowerCase()}`}>Confirmado</b>
              <Link className="text-link" to={`/cliente/agendamentos/${nextAppointment.id}`}>Ver detalhes <ArrowRight size={14} /></Link>
            </div>
          ) : (
            <div className="customer-empty"><CalendarRange size={22} /><strong>Nenhum atendimento agendado</strong><span>{firstName}, que tal agendar seu próximo atendimento?</span></div>
          )}
        </section>

        <section className="customer-home-card panel customer-qr-card-home">
          <div className="panel-header"><div><h2><QrCode size={18} /> Meu QR Code</h2><p>Apresente no Totem para o check-in.</p></div></div>
          {!nextAppointment && <div className="customer-qr-empty"><QrCode size={22} /><span>Você ainda não tem um atendimento agendado para gerar o QR Code.</span></div>}
          {nextAppointment && !qrDataUrl && (
            <div className="customer-qr-empty">
              <QrCode size={22} />
              <span>Gere seu QR Code para apresentar no Totem quando chegar.</span>
              {qrError && <div className="form-error" role="alert">{qrError}</div>}
              <button className="primary-button" type="button" onClick={issueToken} disabled={issuing}>{issuing ? 'Gerando…' : 'Gerar QR Code'}</button>
            </div>
          )}
          {qrDataUrl && (
            <div className="customer-qr-generated">
              <img className="customer-qr-image" src={qrDataUrl} alt="QR Code de check-in" />
              <div className="customer-checkin-code"><small>Código</small><strong>{manualCode.split('').join(' ')}</strong></div>
              <button className="secondary-button" type="button" onClick={copyCode}><Copy size={14} /> copiar</button>
            </div>
          )}
        </section>

        <section className="customer-home-card panel">
          <div className="panel-header"><div><h2><CalendarPlus size={18} /> Novo agendamento</h2><p>Agende seus atendimentos de forma rápida e prática.</p></div></div>
          <Link className="primary-button" to="/cliente/agendar">Novo agendamento <ArrowRight size={16} /></Link>
        </section>

        <section className="customer-home-card panel customer-optin-card">
          <div className="panel-header"><div><h2><MessageCircle size={18} /> Avisos por WhatsApp</h2><p>Confirmações, mudanças e atrasos dos seus atendimentos.</p></div></div>
          <WhatsAppOptInPanel text={CUSTOMER_OPT_IN_TEXT} load={whatsAppOptInApi.customer} save={whatsAppOptInApi.setCustomer} />
        </section>
      </div>

      <div className="customer-section-heading"><div><h2>Próximos agendamentos</h2></div></div>
      {upcoming.length === 0
        ? <div className="customer-empty panel"><CalendarRange size={22} /><strong>Nenhum agendamento futuro</strong><span>Que tal agendar seu próximo atendimento?</span></div>
        : <div className="customer-reservation-list">{upcoming.map((reservation) => <ReservationRow key={reservation.id} reservation={reservation} />)}</div>}

      <div className="customer-section-heading"><div><h2>Histórico recente</h2></div></div>
      {history.length === 0
        ? <div className="customer-empty panel"><CalendarRange size={22} /><strong>Nenhum atendimento anterior</strong><span>Seu histórico aparecerá aqui.</span></div>
        : <div className="customer-reservation-list">{history.map((reservation) => <ReservationRow key={reservation.id} reservation={reservation} />)}</div>}
    </div>
  )
}

export function CustomerReservations() {
  const [data, setData] = useState<PagedResponse<ReservationDto> | null>(null)
  const [error, setError] = useState('')
  useEffect(() => { customerApi.reservations({ page: 1, pageSize: 20 }).then(setData).catch(() => setError('Não foi possível carregar seus agendamentos.')) }, [])
  return <section className="customer-section page-enter"><div className="customer-section-heading"><div><span className="eyebrow">Sua agenda</span><h1>Meus agendamentos</h1><p>Acompanhe seus horários no LUMIS, dos próximos aos mais recentes.</p></div><Link className="primary-button" to="/cliente/agendar"><CalendarPlus size={17} /> Novo agendamento</Link></div>{error && <div className="form-error" role="alert">{error}</div>}{!data && !error && <div className="customer-loading" role="status">Carregando seus horários…</div>}{data?.items.length === 0 && <div className="customer-empty panel"><CalendarRange size={25} /><strong>Nenhum agendamento ainda</strong><span>Escolha um profissional para começar.</span></div>}{data && data.items.length > 0 && <div className="customer-reservation-list">{data.items.map((reservation) => <Link className="customer-reservation-card panel" key={reservation.id} to={`/cliente/agendamentos/${reservation.id}`}><div><span>{new Date(reservation.startAt).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })}</span><strong>{new Date(reservation.startAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</strong></div><section><strong>{reservation.professionalName}</strong><span>{reservation.roomName}</span></section><b className={`customer-status status-${reservation.status.toLowerCase()}`}>{reservation.status === 'APPROVED' ? 'Confirmado' : reservation.status}</b></Link>)}</div>}</section>
}

export function CustomerComingSoon({ title }: { title: string }) { return <section className="customer-section page-enter"><span className="eyebrow">Próxima etapa</span><h1>{title}</h1><div className="customer-empty panel"><CalendarRange size={25} /><strong>Estamos preparando essa experiência</strong><span>Em breve você poderá escolher o melhor horário por aqui.</span><Link className="secondary-button" to="/cliente">Voltar para o início</Link></div></section> }

function useCustomerOutlet() { return useOutletContext<{ profile: CustomerProfileDto | null, firstName: string }>() }

export { CustomerShell }
