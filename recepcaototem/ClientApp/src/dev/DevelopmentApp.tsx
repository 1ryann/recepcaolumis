import { Navigate, Route, Routes } from 'react-router-dom'
import { AdminLayout } from '../components/AdminLayout'
import { ProtectedRoute } from '../components/ProtectedRoute'
import { HelpCenter } from '../help/HelpCenter'
import { ChangePassword } from '../pages/ChangePassword'
import { Login } from '../pages/Login'
import { Reception } from '../pages/Reception'
import { Dashboard } from '../pages/admin/Dashboard'
import { Leases } from '../pages/admin/Leases'
import { Professionals } from '../pages/admin/Professionals'
import { Rooms } from '../pages/admin/Rooms'
import { Settings } from '../pages/admin/Settings'
import { Visits } from '../pages/admin/Visits'
import { DevelopmentAppStore } from './DevelopmentAppStore'

export default function DevelopmentApp() {
  return (
    <DevelopmentAppStore>
      <Routes>
        <Route path="/recepcao" element={<Reception />} />
        <Route path="/login" element={<Login />} />
        <Route path="/change-password" element={<ChangePassword />} />
        <Route path="/ajuda" element={<HelpCenter />} />
        <Route element={<ProtectedRoute />}>
          <Route path="/admin" element={<AdminLayout />}>
            <Route index element={<Dashboard />} />
            <Route path="salas" element={<Rooms />} />
            <Route path="profissionais" element={<Professionals />} />
            <Route path="locacoes" element={<Leases />} />
            <Route path="visitas" element={<Visits />} />
            <Route path="configuracoes" element={<Settings />} />
          </Route>
        </Route>
        <Route path="/" element={<Navigate to="/recepcao" replace />} />
        <Route path="*" element={<Navigate to="/recepcao" replace />} />
      </Routes>
    </DevelopmentAppStore>
  )
}
