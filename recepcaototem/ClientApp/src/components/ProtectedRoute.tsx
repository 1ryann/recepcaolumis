import { useEffect, useRef, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useSession, type SessionUser } from '../auth/SessionProvider'

import { homeForRoles } from '../auth/roleRoutes'

type ProtectedRouteProps = { allowedRoles?: string[] }

export function ProtectedRoute({ allowedRoles }: ProtectedRouteProps) {
  const location = useLocation()
  const { status, user, refresh } = useSession()
  const [validatedOnce, setValidatedOnce] = useState(false)
  const lastCheckedKey = useRef<string | null>(null)

  // First entry into any protected area: validate the cookie once, foreground, and let the
  // route-loading fallback stand in for the protected content until it resolves. Mount only
  // (refresh is stable) — later navigations must never re-block.
  useEffect(() => {
    let active = true
    lastCheckedKey.current = location.key
    void refresh().then(() => { if (active) setValidatedOnce(true) })
    return () => { active = false }
  }, [refresh])

  // Later in-app navigations: re-check the cookie in the background only — never unmount the
  // shell (AdminLayout etc.), never show route-loading. Fires only when location.key actually
  // changes, and never for the key the first-load effect already validated, so there is no
  // duplicate /api/auth/session right after the initial validation.
  useEffect(() => {
    if (!validatedOnce) return
    if (lastCheckedKey.current === location.key) return
    lastCheckedKey.current = location.key
    void refresh({ background: true })
  }, [location.key, validatedOnce, refresh])

  if (!validatedOnce) return <div className="route-loading" role="status">Carregando…</div>
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
