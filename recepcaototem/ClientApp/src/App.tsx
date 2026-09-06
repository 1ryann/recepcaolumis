import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AdminLayout } from './components/AdminLayout'
import { ModuleUnavailable } from './components/ModuleUnavailable'
import { ProtectedRoute } from './components/ProtectedRoute'
import { ChangePassword } from './pages/ChangePassword'
import { Login } from './pages/Login'
import { Professionals } from './pages/admin/Professionals'
import { Rooms } from './pages/admin/Rooms'
import { Leases } from './pages/admin/Leases'
import { Reservations } from './pages/admin/Reservations'
import { Visits } from './pages/admin/Visits'

const DevelopmentApp = import.meta.env.DEV ? lazy(() => import('./dev/DevelopmentApp')) : null

function ProductionApp() {
  return (
    <Routes>
      <Route path="/recepcao" element={<ModuleUnavailable title="Recepção" />} />
      <Route path="/login" element={<Login />} />
      <Route path="/change-password" element={<ChangePassword />} />
      <Route element={<ProtectedRoute />}>
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<ModuleUnavailable title="Visão geral" />} />
          <Route path="salas" element={<Rooms />} />
          <Route path="profissionais" element={<Professionals />} />
          <Route path="locacoes" element={<Leases />} />
          <Route path="reservas" element={<Reservations />} />
          <Route path="visitas" element={<Visits />} />
          <Route path="configuracoes" element={<ModuleUnavailable title="Configurações" />} />
        </Route>
      </Route>
      <Route path="/" element={<Navigate to="/recepcao" replace />} />
      <Route path="*" element={<Navigate to="/recepcao" replace />} />
    </Routes>
  )
}

export function App() {
  if (DevelopmentApp) return <Suspense fallback={null}><DevelopmentApp /></Suspense>
  return <ProductionApp />
}
