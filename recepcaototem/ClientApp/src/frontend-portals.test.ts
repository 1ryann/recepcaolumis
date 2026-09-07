import { expect, test } from 'vitest'
import appSource from './App.tsx?raw'
import developmentSource from './dev/DevelopmentApp.tsx?raw'
import protectedRouteSource from './components/ProtectedRoute.tsx?raw'
import customerHomeSource from './pages/customer/CustomerHome.tsx?raw'
import customerRegisterSource from './pages/customer/CustomerRegister.tsx?raw'
import professionalSource from './pages/professional/ProfessionalHome.tsx?raw'

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

test('professional portal consumes only owned professional endpoints', () => {
  expect(professionalSource).toContain("professionalReservationsApi.list")
  expect(professionalSource).toContain("professionalVisitsApi.list")
  expect(professionalSource).not.toContain('admin/professionals')
  expect(professionalSource).not.toContain('ProfessionalId')
})
