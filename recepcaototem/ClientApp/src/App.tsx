import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AdminLayout } from './components/AdminLayout'
import { ReceptionLayout } from './components/ReceptionLayout'
import { ProtectedRoute } from './components/ProtectedRoute'
import { ChangePassword } from './pages/ChangePassword'
import { Home } from './pages/Home'
import { Login } from './pages/Login'
import { AdminDashboard } from './pages/admin/AdminDashboard'
import { Professionals } from './pages/admin/Professionals'
import { Settings } from './pages/admin/Settings'
import { Rooms } from './pages/admin/Rooms'
import { Leases } from './pages/admin/Leases'
import { Reservations } from './pages/admin/Reservations'
import { Visits } from './pages/admin/Visits'
import { ReceptionMonitor } from './pages/admin/ReceptionMonitor'
import { RoomRentalInquiries } from './pages/admin/RoomRentalInquiries'
import { CustomerHome, CustomerReservations, CustomerShell } from './pages/customer/CustomerHome'
import { CustomerRegister } from './pages/customer/CustomerRegister'
import { CustomerBooking } from './pages/customer/CustomerBooking'
import { CustomerReservationDetail } from './pages/customer/CustomerReservationDetail'
import { TotemCheckIn } from './pages/TotemCheckIn'
import { TotemEntry } from './pages/TotemEntry'
import { TotemHandoff } from './pages/TotemHandoff'
import { TotemProfessionals } from './pages/TotemProfessionals'
import { TotemRoomsCatalog } from './pages/TotemRoomsCatalog'
import { TotemRoomDetail } from './pages/TotemRoomDetail'
import { TotemRoomInterestSuccess } from './pages/TotemRoomInterestSuccess'
import { ProfessionalDashboard, ProfessionalShell } from './pages/professional/ProfessionalHome'
import { ProfessionalAgenda } from './pages/professional/ProfessionalAgenda'
import { ProfessionalRegistration } from './pages/professional/ProfessionalRegistration'
import { ProfessionalApplicationStatus } from './pages/professional/ProfessionalApplicationStatus'
import { ProfessionalAvailability } from './pages/professional/ProfessionalAvailability'
import { ProfessionalProfile } from './pages/professional/ProfessionalProfile'
import { ProfessionalReservations } from './pages/professional/ProfessionalReservations'
import { ProfessionalVisits } from './pages/professional/ProfessionalVisits'
import { ProfessionalLeases } from './pages/professional/ProfessionalLeases'
import { ProfessionalFinance } from './pages/professional/ProfessionalFinance'
import { ProfessionalApplications } from './pages/admin/ProfessionalApplications'

const DevelopmentApp = import.meta.env.DEV ? lazy(() => import('./dev/DevelopmentApp')) : null

function ProductionApp() {
  return (
    <Routes>
      <Route path="/" element={<Home />} />
  <Route path="/login" element={<Login />} /><Route path="/profissional/login" element={<Login audience="professional" />} />
  <Route path="/cliente/login" element={<Login audience="customer" />} />
  <Route path="/cliente/cadastro" element={<CustomerRegister />} />
  <Route path="/profissional/cadastro" element={<ProfessionalRegistration />} />
  <Route path="/totem" element={<TotemEntry />} />
  <Route path="/totem/check-in" element={<TotemCheckIn />} />
  <Route path="/totem/profissionais" element={<TotemProfessionals />} />
  <Route path="/totem/salas" element={<TotemRoomsCatalog />} />
  <Route path="/totem/salas/:id" element={<TotemRoomDetail />} />
  <Route path="/totem/salas/:id/interesse" element={<TotemRoomInterestSuccess />} />
  <Route path="/totem/handoff" element={<TotemHandoff />} />
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
    <Route path="reservas" element={<ProfessionalReservations />} />
    <Route path="atendimentos" element={<ProfessionalVisits />} />
    <Route path="disponibilidade" element={<ProfessionalAvailability />} />
    <Route path="locacoes" element={<ProfessionalLeases />} />
    <Route path="financeiro" element={<ProfessionalFinance />} />
    <Route path="perfil" element={<ProfessionalProfile />} />
   </Route>
  </Route>
  <Route element={<ProtectedRoute allowedRoles={['PROFESSIONAL_APPLICANT']} />}><Route path="/profissional/aguardando" element={<ProfessionalApplicationStatus />} /></Route>
  <Route element={<ProtectedRoute allowedRoles={['ADMINISTRADOR', 'GERENTE']} />}>
    <Route element={<ReceptionLayout />}>
      <Route path="/recepcao" element={<ReceptionMonitor />} />
      <Route path="/recepcao/solicitacoes-profissionais" element={<ProfessionalApplications />} />
      <Route path="/recepcao/profissionais" element={<Professionals />} />
      <Route path="/recepcao/configuracoes" element={<Settings />} />
    </Route>
  </Route>
<Route element={<ProtectedRoute allowedRoles={['ADMINISTRADOR']} />}>
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<AdminDashboard />} />
          <Route path="salas" element={<Rooms />} />
          <Route path="profissionais" element={<Professionals />} />
          <Route path="locacoes" element={<Leases />} />
          <Route path="reservas" element={<Reservations />} />
          <Route path="visitas" element={<Visits />} />
          <Route path="recepcao" element={<ReceptionMonitor />} />
          <Route path="solicitacoes-profissionais" element={<ProfessionalApplications />} />
          <Route path="interesses-locacao" element={<RoomRentalInquiries />} />
          <Route path="configuracoes" element={<Settings />} />
        </Route>
      </Route>
      <Route path="/acesso-negado" element={<p className="lumis-access-denied">Acesso indisponível para esta conta.</p>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

export function App() {
  if (DevelopmentApp) return <Suspense fallback={null}><DevelopmentApp /></Suspense>
  return <ProductionApp />
}
