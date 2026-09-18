import { expect, test } from 'vitest'
import appSource from './App.tsx?raw'
import professionalsSource from './pages/admin/Professionals.tsx?raw'
import roomsSource from './pages/admin/Rooms.tsx?raw'
import leasesSource from './pages/admin/Leases.tsx?raw'
import reservationsSource from './pages/admin/Reservations.tsx?raw'
import visitsSource from './pages/admin/Visits.tsx?raw'

test('real module screens do not import AppStore or mock data', () => {
  for (const content of [professionalsSource, roomsSource, leasesSource, reservationsSource, visitsSource]) {
    expect(content).not.toMatch(/AppStore|data\/mock|store\/AppStore/)
  }
})

test('visits use the real screen on both route trees', async () => {
  const developmentSource = await import('./dev/DevelopmentApp.tsx?raw').then(module => module.default)
  expect(appSource).toContain('<Visits />')
  expect(developmentSource).toContain('<Visits />')
  expect(visitsSource).toContain('visitsApi.list')
  expect(visitsSource).toContain('professionalVisitsApi.list')
})

test('reservations use the real screen on both route trees', async () => {
  const developmentSource = await import('./dev/DevelopmentApp.tsx?raw').then(module => module.default)
  expect(appSource).toContain('<Reservations />')
  expect(developmentSource).toContain('<Reservations />')
  expect(reservationsSource).toContain('reservationsApi.list')
  expect(reservationsSource).toContain('professionalReservationsApi.list')
})

test('the room rental flow is routed identically on both route trees', async () => {
  const developmentSource = await import('./dev/DevelopmentApp.tsx?raw').then(module => module.default)
  for (const route of [
    '<Route path="/totem/salas" element={<TotemRoomsCatalog />} />',
    '<Route path="/totem/salas/:id" element={<TotemRoomDetail />} />',
    '<Route path="/totem/salas/:id/interesse" element={<TotemRoomInterestSuccess />} />',
    '<Route path="interesses-locacao" element={<RoomRentalInquiries />} />',
  ]) {
    expect(appSource).toContain(route)
    expect(developmentSource).toContain(route)
  }
})

test('leases use the real screen on both route trees', async () => {
  const developmentSource = await import('./dev/DevelopmentApp.tsx?raw').then(module => module.default)
  expect(appSource).toContain('<Leases />')
  expect(developmentSource).toContain('<Leases />')
  expect(leasesSource).toContain('leasesApi.list')
})

test('production route source has a static DEV boundary and no external mock toggle', () => {
  expect(appSource).toContain('import.meta.env.DEV')
  expect(appSource).not.toMatch(/VITE_.*MOCK|localStorage.*mock|enableMocks/i)
})
