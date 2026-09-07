import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useSession, type SessionUser } from '../auth/SessionProvider'

export function homeForRoles(roles: string[]) {
  if (roles.includes('CUSTOMER')) return '/cliente'
  if (roles.includes('PROFISSIONAL')) return '/profissional'
  return '/admin'
}

type ProtectedRouteProps = { allowedRoles?: string[] }

export function ProtectedRoute({ allowedRoles }: ProtectedRouteProps) {
  const location = useLocation()
  const { status, user } = useSession()
  if (status === 'loading') return <div className="route-loading" role="status">Carregando…</div>
  if (status === 'anonymous') return <Navigate to={location.pathname.startsWith('/cliente') ? '/cliente/login' : '/login'} state={{ from: location.pathname }} replace />
  if (status === 'mustChangePassword') return <Navigate to="/change-password" replace />
  if (allowedRoles && user && !allowedRoles.some((role) => user.roles.includes(role))) {
    return <Navigate to={homeForRoles(user.roles)} replace />
  }
  return <Outlet />
}

export function ProfileLink({ user }: { user: SessionUser }) {
  return <Navigate to={homeForRoles(user.roles)} replace />
}
