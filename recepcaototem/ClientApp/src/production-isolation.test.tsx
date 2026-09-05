import { expect, test } from 'vitest'
import appSource from './App.tsx?raw'
import professionalsSource from './pages/admin/Professionals.tsx?raw'
import roomsSource from './pages/admin/Rooms.tsx?raw'

test('real module screens do not import AppStore or mock data', () => {
  for (const content of [professionalsSource, roomsSource]) {
    expect(content).not.toMatch(/AppStore|data\/mock|store\/AppStore/)
  }
})

test('production route source has a static DEV boundary and no external mock toggle', () => {
  expect(appSource).toContain('import.meta.env.DEV')
  expect(appSource).not.toMatch(/VITE_.*MOCK|localStorage.*mock|enableMocks/i)
})
