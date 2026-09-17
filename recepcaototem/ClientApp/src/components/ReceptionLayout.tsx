import { Outlet } from 'react-router-dom'
import { LumisPageShell } from '../features/lumis/LumisPageShell'

// A deliberately small wrapper — NOT a copy of AdminLayout's sidebar/nav. ReceptionMonitor,
// ProfessionalApplications, Professionals and Settings (mounted at /recepcao/*) already have
// their own in-page navigation via header buttons, so this only needs to give them the same
// theme-scoped surface Admin gets (.admin-shell/.admin-main/.admin-content, minus the sidebar).
// See styles.css's ".admin-shell:has(> .sidebar) .admin-main" note: .admin-main's margin-left
// is scoped to shells that actually render a sidebar, so it collapses correctly here.
export function ReceptionLayout() {
  return (
    <LumisPageShell intensity="muted" className="admin-shell-page">
      <div className="admin-shell">
        <div className="admin-main">
          <div className="admin-content"><Outlet /></div>
        </div>
      </div>
    </LumisPageShell>
  )
}
