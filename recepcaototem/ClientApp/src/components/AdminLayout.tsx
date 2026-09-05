import { CalendarRange, ChevronDown, DoorOpen, LayoutDashboard, LogOut, Menu, Settings, Users, UserRoundSearch, X } from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'

const navItems = [
  { to: '/admin', label: 'Visão geral', icon: LayoutDashboard, end: true },
  { to: '/admin/salas', label: 'Salas', icon: DoorOpen },
  { to: '/admin/profissionais', label: 'Profissionais', icon: Users },
  { to: '/admin/locacoes', label: 'Locações', icon: CalendarRange },
  { to: '/admin/visitas', label: 'Visitas', icon: UserRoundSearch },
  { to: '/admin/configuracoes', label: 'Configurações', icon: Settings },
]

const pageNames: Record<string, string> = {
  '/admin': 'Visão geral', '/admin/salas': 'Salas', '/admin/profissionais': 'Profissionais', '/admin/locacoes': 'Locações', '/admin/visitas': 'Visitas', '/admin/configuracoes': 'Configurações',
}

export function AdminLayout() {
  const [open, setOpen] = useState(false)
  const location = useLocation()
  const navigate = useNavigate()
  const session = useSession()
  const name = session.user?.displayName || session.user?.email || 'Usuário'
  const initials = name.split(/\s+/).slice(0, 2).map(part => part[0]).join('').toUpperCase()
  const role = session.user?.roles[0] ?? ''
  const logout = async () => { await session.logout(); navigate('/login') }

  return (
    <div className="admin-shell">
      <aside className={`sidebar ${open ? 'sidebar-open' : ''}`}>
        <div className="sidebar-brand">
            <img className="sidebar-logo" src="/lumis-logo-transparent.png" alt="LUMIS" />
          <span className="sr-only">LUMIS Administração</span>
          <button className="mobile-close icon-button" onClick={() => setOpen(false)} aria-label="Fechar menu"><X size={20} /></button>
        </div>
        <nav className="admin-nav" aria-label="Navegação administrativa">
          <span className="nav-label">Gestão</span>
          {navItems.slice(0, 5).map(({ to, label, icon: Icon, end }) => (
            <NavLink key={to} to={to} end={end} onClick={() => setOpen(false)}><Icon size={19} />{label}</NavLink>
          ))}
          <span className="nav-label nav-label-second">Preferências</span>
          {navItems.slice(5).map(({ to, label, icon: Icon }) => (
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
  )
}
