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
import { ReceptionMonitor } from './pages/admin/ReceptionMonitor'
import { CustomerHome, CustomerReservations, CustomerShell } from './pages/customer/CustomerHome'
import { CustomerRegister } from './pages/customer/CustomerRegister'
import { CustomerBooking } from './pages/customer/CustomerBooking'
import { CustomerReservationDetail } from './pages/customer/CustomerReservationDetail'
import { TotemCheckIn } from './pages/TotemCheckIn'
import { ProfessionalAgenda, ProfessionalDashboard, ProfessionalPlaceholder, ProfessionalShell } from './pages/professional/ProfessionalHome'

const DevelopmentApp = import.meta.env.DEV ? lazy(() => import('./dev/DevelopmentApp')) : null

function ProductionApp() {
  return (
    <Routes>
      <Route path="/recepcao" element={<ModuleUnavailable title="Recepção" />} />
  <Route path="/login" element={<Login />} />
  <Route path="/cliente/login" element={<Login audience="customer" />} />
  <Route path="/cliente/cadastro" element={<CustomerRegister />} />
  <Route path="/totem/check-in" element={<TotemCheckIn />} />
  <Route path="/change-password" element={<ChangePassword />} />
  <Route element={<ProtectedRoute allowedRoles={['CUSTOMER']} />}>
   <Route path="/cliente" element={<CustomerShell />}>
    <Route index element={<CustomerHome />} />
    <Route path="agendamentos" element={<CustomerReservations />} />
    <Route path="agendar" element={<CustomerBooking />} />
    <Route path="agendamentos/:id" element={<CustomerReservationDetail />} />
   </Route>
  </Route>
  <Route element={<ProtectedRoute allowedRoles={['PROFISSIONAL']} />}>
   <Route path="/profissional" element={<ProfessionalShell />}>
    <Route index element={<ProfessionalDashboard />} />
    <Route path="agenda" element={<ProfessionalAgenda />} />
    <Route path="reservas" element={<ProfessionalPlaceholder title="Reservas" />} />
    <Route path="atendimentos" element={<ProfessionalPlaceholder title="Atendimentos" />} />
    <Route path="locacoes" element={<ProfessionalPlaceholder title="Locações" />} />
    <Route path="financeiro" element={<ProfessionalPlaceholder title="Financeiro" />} />
    <Route path="perfil" element={<ProfessionalPlaceholder title="Meu perfil" />} />
   </Route>
  </Route>
  <Route element={<ProtectedRoute allowedRoles={['ADMINISTRADOR', 'GERENTE']} />}>
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<ModuleUnavailable title="Visão geral" />} />
          <Route path="salas" element={<Rooms />} />
          <Route path="profissionais" element={<Professionals />} />
          <Route path="locacoes" element={<Leases />} />
          <Route path="reservas" element={<Reservations />} />
          <Route path="visitas" element={<Visits />} />
          <Route path="recepcao" element={<ReceptionMonitor />} />
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
