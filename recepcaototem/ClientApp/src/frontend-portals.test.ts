import { expect, test } from 'vitest'
import appSource from './App.tsx?raw'
import developmentSource from './dev/DevelopmentApp.tsx?raw'
import protectedRouteSource from './components/ProtectedRoute.tsx?raw'
import customerHomeSource from './pages/customer/CustomerHome.tsx?raw'
import customerRegisterSource from './pages/customer/CustomerRegister.tsx?raw'
import professionalSource from './pages/professional/ProfessionalHome.tsx?raw'
import customerBookingSource from './pages/customer/CustomerBooking.tsx?raw'
import customerDetailSource from './pages/customer/CustomerReservationDetail.tsx?raw'
import totemCheckInSource from './pages/TotemCheckIn.tsx?raw'
import totemProfessionalsSource from './pages/TotemProfessionals.tsx?raw'
import totemHandoffSource from './pages/TotemHandoff.tsx?raw'
import modulesSource from './api/modules.ts?raw'
import receptionMonitorSource from './pages/admin/ReceptionMonitor.tsx?raw'
import availabilitySource from './features/availability/AvailabilityEditor.tsx?raw'

test('profile routes are protected in both development and production route trees', () => {
  for (const source of [appSource, developmentSource]) {
    expect(source).toContain("allowedRoles={['CUSTOMER']}")
    expect(source).toContain("allowedRoles={['PROFISSIONAL']}")
    expect(source).toContain("allowedRoles={['ADMINISTRADOR', 'GERENTE']}")
  }
  expect(protectedRouteSource).toContain("location.pathname.startsWith('/cliente') ? '/cliente/login' : '/login'")
})

test('customer portal uses its own backend identity and does not persist credentials', () => {
  expect(customerRegisterSource).toContain("customerApi.register")
  expect(customerHomeSource).toContain("customerApi.me")
  expect(customerHomeSource).toContain("customerApi.reservations")
  expect(customerRegisterSource).not.toMatch(/localStorage|sessionStorage|CustomerId|role:/i)
})

test('customer registration mirrors the server password policy', () => {
  // Mirrors LumisIdentityOptions: at least 6 characters, a letter and a digit; no capital or symbol required.
  expect(customerRegisterSource).toContain('password.length >= 6')
  expect(customerRegisterSource).toContain("/[a-z]/.test(password)")
  expect(customerRegisterSource).toContain("/\\d/.test(password)")
  expect(customerRegisterSource).not.toContain("/[A-Z]/.test(password)")
  expect(customerRegisterSource).not.toContain("/[^A-Za-z0-9]/.test(password)")
})

test('professional portal consumes only owned professional endpoints', () => {
  expect(professionalSource).toContain("professionalReservationsApi.list")
  expect(professionalSource).toContain("professionalVisitsApi.list")
  expect(professionalSource).not.toContain('admin/professionals')
  expect(professionalSource).not.toContain('ProfessionalId')
})

test('customer booking and check-in use the real reservation and QR contracts', () => {
  expect(customerBookingSource).toContain('customerApi.professionals')
  expect(customerBookingSource).toContain('customerApi.availability')
  expect(customerBookingSource).toContain('customerApi.createReservation')
  expect(customerDetailSource).toContain('customerApi.reservation')
  expect(customerDetailSource).toContain('customerApi.issueCheckInToken')
  expect(customerDetailSource).toContain('QRCode.toDataURL(result.token')
  expect(customerDetailSource).not.toMatch(/localStorage|sessionStorage/)
  expect(totemCheckInSource).toContain('totemApi.resolveCheckIn')
  expect(totemCheckInSource).toContain('totemApi.confirmCheckIn')
  expect(modulesSource).toContain("'/api/totem/check-in/confirm'")
})

test('the Totem kiosk is reachable at /totem and /totem/check-in on both route trees', () => {
  for (const source of [appSource, developmentSource]) {
    expect(source).toContain('<Route path="/totem" element={<TotemEntry />} />')
    expect(source).toContain('<Route path="/totem/check-in" element={<TotemCheckIn />} />')
    expect(source).toContain('<Route path="/totem/profissionais" element={<TotemProfessionals />} />')
  }
  expect(totemCheckInSource).toContain('totemApi.resolveCheckIn')
  expect(totemCheckInSource).toContain('totemApi.confirmCheckIn')
  expect(modulesSource).toContain("'/api/totem/check-in/confirm'")
  expect(modulesSource).toContain("'/api/totem/professionals'")
})

test('the Totem booking handoff route is public and the Totem flow never falls back to a login screen', () => {
  expect(appSource).toContain('<Route path="/totem/handoff" element={<TotemHandoff />} />')
  const handoffRouteIndex = appSource.indexOf('<Route path="/totem/handoff" element={<TotemHandoff />} />')
  const firstProtectedRouteIndex = appSource.indexOf('<ProtectedRoute')
  expect(handoffRouteIndex).toBeGreaterThan(-1)
  expect(firstProtectedRouteIndex).toBeGreaterThan(-1)
  expect(handoffRouteIndex).toBeLessThan(firstProtectedRouteIndex)

  expect(totemProfessionalsSource).toContain('totemApi.createHandoff')
  expect(totemProfessionalsSource).toContain("navigate('/totem/handoff'")
  expect(totemProfessionalsSource).not.toContain('/cliente/agendar')
  expect(totemProfessionalsSource).not.toContain('/cliente/login')

  for (const source of [totemProfessionalsSource, totemHandoffSource]) {
    expect(source).not.toContain('/cliente/login')
    expect(source).not.toContain('/profissional/login')
    expect(source).not.toMatch(/navigate\(['"]\/login['"]/)
    expect(source).not.toMatch(/<Navigate to=['"]\/(cliente|profissional|login)/)
  }
})

test('reception monitor reads the real queue and transitions visits through the API', () => {
  expect(receptionMonitorSource).toContain('receptionApi.overview')
  expect(receptionMonitorSource).toContain('receptionApi.visits')
  expect(receptionMonitorSource).toContain('receptionApi.startVisit')
  expect(receptionMonitorSource).toContain('receptionApi.endVisit')
  expect(receptionMonitorSource).not.toContain('useAppStore')
})

test('the real /admin dashboard (not the old mock-data one) is routed on both development and production route trees', () => {
  for (const source of [appSource, developmentSource]) {
    expect(source).toContain("import { AdminDashboard } from '")
    expect(source).toMatch(/<Route path="\/admin" element=\{<AdminLayout \/>\}>\s*<Route index element=\{<AdminDashboard \/>\}/)
    expect(source).not.toContain("import { Dashboard } from '")
    expect(source).not.toContain('<Route index element={<Dashboard />} />')
  }
})

test('availability management is exposed to both Professional and Operations routes', () => {
  for (const source of [appSource, developmentSource]) {
    expect(source).toContain('path="disponibilidade"')
    expect(source).toContain('path="/recepcao/configuracoes"')
    expect(source).toContain('path="/recepcao/profissionais"')
    expect(source).toContain("allowedRoles={['ADMINISTRADOR', 'GERENTE']}")
  }
  expect(availabilitySource).toContain('Os horários personalizados continuam preservados')
  expect(availabilitySource).toContain('existingReservationsOutsideAvailabilityCount')
})
