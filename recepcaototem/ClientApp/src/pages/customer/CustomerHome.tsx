import { ArrowRight, CalendarPlus, CalendarRange, LogOut, UserRound } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, Outlet, useLocation, useNavigate, useOutletContext } from 'react-router-dom'
import { useSession } from '../../auth/SessionProvider'
import { customerApi, type CustomerProfileDto, type PagedResponse, type ReservationDto } from '../../api/modules'
import { ApiError } from '../../api/client'

function CustomerShell() {
  const session = useSession()
  const navigate = useNavigate()
  const location = useLocation()
  const [profile, setProfile] = useState<CustomerProfileDto | null>(null)
  const [error, setError] = useState('')
  useEffect(() => { customerApi.me().then(setProfile).catch((caught) => { if (caught instanceof ApiError && caught.status === 401) navigate('/cliente/login', { replace: true }); else setError('Não foi possível carregar sua área.') }) }, [navigate])
  const logout = async () => { await session.logout(); navigate('/cliente/login', { replace: true }) }
  const firstName = (profile?.name || session.user?.displayName || 'Cliente').split(/\s+/)[0]
  return <div className="customer-shell">
    <header className="customer-topbar"><Link className="customer-brand" to="/cliente"><img src="/lumis-logo-dark.png" alt="LUMIS" /></Link><nav className="customer-nav" aria-label="Área do cliente"><Link className={location.pathname === '/cliente' ? 'is-active' : ''} to="/cliente">Início</Link><Link className={location.pathname.startsWith('/cliente/agendamentos') ? 'is-active' : ''} to="/cliente/agendamentos">Meus agendamentos</Link></nav><button className="customer-logout" type="button" onClick={logout}><LogOut size={16} /> Sair</button></header>
    {error && <div className="customer-inline-error" role="alert">{error}</div>}
    <main className="customer-content"><Outlet context={{ profile, firstName }} /></main>
  </div>
}

export function CustomerHome() {
  const { profile, firstName } = useCustomerOutlet()
  return <div className="customer-home page-enter"><section className="customer-welcome"><span className="eyebrow">Área do cliente</span><h1>Olá, {firstName}.</h1><p>Seu próximo cuidado começa com um pequeno passo.</p><div className="customer-home-actions"><Link className="primary-button" to="/cliente/agendar"><CalendarPlus size={18} /> Agendar atendimento <ArrowRight size={17} /></Link><Link className="secondary-button" to="/cliente/agendamentos"><CalendarRange size={18} /> Meus agendamentos</Link></div></section><section className="customer-home-card panel"><div className="customer-home-card-icon"><UserRound size={20} /></div><div><strong>Seu cadastro está pronto</strong><p>{profile?.phone ? `Telefone confirmado: ${profile.phone}` : 'Mantenha seus dados sempre atualizados.'}</p></div></section></div>
}

export function CustomerReservations() {
  const [data, setData] = useState<PagedResponse<ReservationDto> | null>(null)
  const [error, setError] = useState('')
  useEffect(() => { customerApi.reservations({ page: 1, pageSize: 20 }).then(setData).catch(() => setError('Não foi possível carregar seus agendamentos.')) }, [])
  return <section className="customer-section page-enter"><div className="customer-section-heading"><div><span className="eyebrow">Sua agenda</span><h1>Meus agendamentos</h1><p>Acompanhe seus próximos horários no LUMIS.</p></div><Link className="primary-button" to="/cliente/agendar"><CalendarPlus size={17} /> Novo agendamento</Link></div>{error && <div className="form-error" role="alert">{error}</div>}{!data && !error && <div className="customer-loading" role="status">Carregando seus horários…</div>}{data?.items.length === 0 && <div className="customer-empty panel"><CalendarRange size={25} /><strong>Nenhum agendamento ainda</strong><span>Escolha um profissional para começar.</span></div>}{data && data.items.length > 0 && <div className="customer-reservation-list">{data.items.map((reservation) => <article className="customer-reservation-card panel" key={reservation.id}><div><span>{new Date(reservation.startAt).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })}</span><strong>{new Date(reservation.startAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</strong></div><section><strong>{reservation.professionalName}</strong><span>{reservation.roomName}</span></section><b className={`customer-status status-${reservation.status.toLowerCase()}`}>{reservation.status === 'APPROVED' ? 'Confirmado' : reservation.status}</b></article>)}</div>}</section>
}

export function CustomerComingSoon({ title }: { title: string }) { return <section className="customer-section page-enter"><span className="eyebrow">Próxima etapa</span><h1>{title}</h1><div className="customer-empty panel"><CalendarRange size={25} /><strong>Estamos preparando essa experiência</strong><span>Em breve você poderá escolher o melhor horário por aqui.</span><Link className="secondary-button" to="/cliente">Voltar para o início</Link></div></section> }

function useCustomerOutlet() { return useOutletContext<{ profile: CustomerProfileDto | null, firstName: string }>() }

export { CustomerShell }
