import { Link, Outlet, useLocation } from 'react-router-dom'
import { LumisPageShell } from '../features/lumis/LumisPageShell'
import { LumisLogo } from '../theme/LumisLogo'
import { AccountMenu } from './AccountMenu'

const pageNames: Record<string, string> = {
  '/recepcao': 'Recepção',
  '/recepcao/solicitacoes-profissionais': 'Solicitações de profissionais',
  '/recepcao/profissionais': 'Profissionais',
  '/recepcao/configuracoes': 'Horários e salas',
  '/recepcao/whatsapp': 'WhatsApp dos clientes',
}

// A deliberately small wrapper — NOT a copy of AdminLayout's sidebar/nav. ReceptionMonitor,
// ProfessionalApplications, Professionals and Settings (mounted at /recepcao/*) already have
// their own in-page navigation via header buttons, so this only gives them the same
// theme-scoped surface Admin gets (.admin-shell/.admin-main/.admin-content, minus the sidebar)
// plus a slim top bar: the logo back to /recepcao, the page name and the account menu — without
// it a manager had no way to sign out. See styles.css's ".admin-shell:has(> .sidebar)
// .admin-main" note: .admin-main's margin-left is scoped to shells that render a sidebar.
export function ReceptionLayout() {
  const location = useLocation()
  return (
    <LumisPageShell intensity="muted" className="admin-shell-page">
      <div className="admin-shell">
        <div className="admin-main">
          <header className="admin-topbar reception-topbar">
            <div className="topbar-title">
              <Link to="/recepcao" className="reception-topbar-logo" aria-label="LUMIS, início da recepção"><LumisLogo alt="" /></Link>
              <span>{pageNames[location.pathname] ?? 'Recepção'}</span>
            </div>
            <AccountMenu />
          </header>
          <div className="admin-content"><Outlet /></div>
        </div>
      </div>
    </LumisPageShell>
  )
}
