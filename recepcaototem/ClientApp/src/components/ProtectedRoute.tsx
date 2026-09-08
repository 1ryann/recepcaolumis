import { useEffect, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useSession, type SessionUser } from '../auth/SessionProvider'

import { homeForRoles } from '../auth/roleRoutes'

type ProtectedRouteProps = { allowedRoles?: string[] }

export function ProtectedRoute({ allowedRoles }: ProtectedRouteProps) {
  const location = useLocation()
  const { status, user, refresh } = useSession()
  const [validatedLocation, setValidatedLocation] = useState<string | null>(null)
  useEffect(() => { let active = true; void refresh().then(() => { if (active) setValidatedLocation(location.key) }); return () => { active = false } }, [location.key, refresh])
  // Only block on the first validation of this location. Later background revalidations
  // must not unmount the routed subtree (which would drop the user's unsaved work).
  if (validatedLocation !== location.key) return <div className="route-loading" role="status">Carregando…</div>
  if (status === 'error') return <div role="alert">Não foi possível confirmar a sessão. <button onClick={() => void refresh()}>Tentar novamente</button></div>
  if (status === 'anonymous') return <Navigate to={location.pathname.startsWith('/cliente') ? '/cliente/login' : '/login'} state={{ from: location.pathname }} replace />
  if (status === 'mustChangePassword') return <Navigate to="/change-password" replace />
  if (allowedRoles && user && !allowedRoles.some((role) => user.roles.includes(role))) {
    return <Navigate to={homeForRoles(user.roles)} replace />
  }
  return <Outlet key={user?.userId} />
}

export function ProfileLink({ user }: { user: SessionUser }) {
  return <Navigate to={homeForRoles(user.roles)} replace />
}
