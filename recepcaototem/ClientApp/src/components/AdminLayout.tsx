import { Activity, CalendarCheck2, CalendarRange, ChevronDown, ClipboardCheck, DoorOpen, LayoutDashboard, LogOut, Menu, MessageSquareText, Settings, Users, UserRoundSearch, X } from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { LumisPageShell } from '../features/lumis/LumisPageShell'
import { useTheme } from '../theme/ThemeProvider'

const navItems = [
  { to: '/admin', label: 'Visão geral', icon: LayoutDashboard, end: true },
  { to: '/admin/salas', label: 'Salas', icon: DoorOpen },
  { to: '/admin/profissionais', label: 'Profissionais', icon: Users },
  { to: '/admin/locacoes', label: 'Locações', icon: CalendarRange },
  { to: '/admin/reservas', label: 'Reservas', icon: CalendarCheck2 },
  { to: '/admin/visitas', label: 'Visitas', icon: UserRoundSearch },
  { to: '/admin/recepcao', label: 'Recepção', icon: Activity },
  { to: '/admin/solicitacoes-profissionais', label: 'Solicitações', icon: ClipboardCheck },
  { to: '/admin/interesses-locacao', label: 'Interesses de locação', icon: MessageSquareText },
  { to: '/admin/configuracoes', label: 'Configurações', icon: Settings },
]

const pageNames: Record<string, string> = {
  '/admin': 'Visão geral', '/admin/salas': 'Salas', '/admin/profissionais': 'Profissionais', '/admin/locacoes': 'Locações', '/admin/reservas': 'Reservas', '/admin/visitas': 'Visitas', '/admin/recepcao': 'Recepção', '/admin/solicitacoes-profissionais': 'Solicitações de profissionais', '/admin/interesses-locacao': 'Interesses de locação', '/admin/configuracoes': 'Configurações',
}

export function AdminLayout() {
  const [open, setOpen] = useState(false)
  const location = useLocation()
  const navigate = useNavigate()
  const session = useSession()
  const { theme } = useTheme()
  const name = session.user?.displayName || session.user?.email || 'Usuário'
  const initials = name.split(/\s+/).slice(0, 2).map(part => part[0]).join('').toUpperCase()
  const role = session.user?.roles[0] ?? ''
  const logout = async () => { await session.logout(); navigate('/login') }

  return (
    <LumisPageShell intensity="muted" className="admin-shell-page">
      <div className="admin-shell">
        <aside className={`sidebar ${open ? 'sidebar-open' : ''}`}>
          <div className="sidebar-brand">
              <img className="sidebar-logo" src={theme === 'dark' ? '/lumis-logo-transparent.png' : '/lumis-logo-dark.png'} alt="LUMIS" />
            <span className="sr-only">LUMIS Administração</span>
            <button className="mobile-close icon-button" onClick={() => setOpen(false)} aria-label="Fechar menu"><X size={20} /></button>
          </div>
          <nav className="admin-nav" aria-label="Navegação administrativa">
            <span className="nav-label">Gestão</span>
            {navItems.slice(0, 9).map(({ to, label, icon: Icon, end }) => (
              <NavLink key={to} to={to} end={end} onClick={() => setOpen(false)}><Icon size={19} />{label}</NavLink>
            ))}
            <span className="nav-label nav-label-second">Preferências</span>
            {navItems.slice(9).map(({ to, label, icon: Icon }) => (
              <NavLink key={to} to={to} onClick={() => setOpen(false)}><Icon size={19} />{label}</NavLink>
            ))}
          </nav>
          <div className="sidebar-footer">
            <div className="sidebar-avatar">{initials}</div>
            <div><strong>{name}</strong><small>{session.user?.email}</small></div>
            <button className="icon-button" type="button" onClick={logout} aria-label="Sair"><LogOut size={18} /></button>
          </div>
        </aside>
        {open && <button className="sidebar-overlay" onClick={() => setOpen(false)} aria-label="Fechar menu" />}
        <div className="admin-main">
          <header className="admin-topbar">
            <div className="topbar-title"><button className="menu-trigger icon-button" onClick={() => setOpen(true)} aria-label="Abrir menu"><Menu size={21} /></button><span>{pageNames[location.pathname] ?? 'Administração'}</span></div>
            <button className="profile-chip" type="button"><span>{initials}</span><div><strong>{name}</strong><small>{role}</small></div><ChevronDown size={15} /></button>
          </header>
          <div className="admin-content"><Outlet /></div>
        </div>
      </div>
    </LumisPageShell>
  )
}
