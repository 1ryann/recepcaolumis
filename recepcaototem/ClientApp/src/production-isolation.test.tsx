import { expect, test } from 'vitest'
import appSource from './App.tsx?raw'
import professionalsSource from './pages/admin/Professionals.tsx?raw'
import roomsSource from './pages/admin/Rooms.tsx?raw'
import leasesSource from './pages/admin/Leases.tsx?raw'

test('real module screens do not import AppStore or mock data', () => {
  for (const content of [professionalsSource, roomsSource, leasesSource]) {
    expect(content).not.toMatch(/AppStore|data\/mock|store\/AppStore/)
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
