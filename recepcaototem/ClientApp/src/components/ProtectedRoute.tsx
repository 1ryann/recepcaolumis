import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'

export function ProtectedRoute() {
  const location = useLocation()
  const { status } = useSession()
  if (status === 'loading') return <div className="route-loading" role="status">Carregando…</div>
  if (status === 'anonymous') return <Navigate to="/login" state={{ from: location.pathname }} replace />
  if (status === 'mustChangePassword') return <Navigate to="/change-password" replace />
  return <Outlet />
}
