import { Navigate, Outlet, useLocation } from 'react-router-dom'

export function ProtectedRoute() {
  const location = useLocation()
  const signedIn = localStorage.getItem('atrium_session') === 'active'
  return signedIn ? <Outlet /> : <Navigate to="/login" state={{ from: location.pathname }} replace />
}
