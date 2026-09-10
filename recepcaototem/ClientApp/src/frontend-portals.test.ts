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
  expect(customerRegisterSource).toContain('password.length >= 12')
  expect(customerRegisterSource).toContain("/[a-z]/.test(password)")
  expect(customerRegisterSource).toContain("/[A-Z]/.test(password)")
  expect(customerRegisterSource).toContain("/\\d/.test(password)")
  expect(customerRegisterSource).toContain("/[^A-Za-z0-9]/.test(password)")
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
  }
  expect(totemCheckInSource).toContain('totemApi.resolveCheckIn')
  expect(totemCheckInSource).toContain('totemApi.confirmCheckIn')
  expect(modulesSource).toContain("'/api/totem/check-in/confirm'")
  expect(modulesSource).toContain("'/api/totem/professionals'")
})

test('reception monitor reads the real queue and transitions visits through the API', () => {
  expect(receptionMonitorSource).toContain('receptionApi.overview')
  expect(receptionMonitorSource).toContain('receptionApi.visits')
  expect(receptionMonitorSource).toContain('receptionApi.startVisit')
  expect(receptionMonitorSource).toContain('receptionApi.endVisit')
  expect(receptionMonitorSource).not.toContain('useAppStore')
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
