import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { ThemeProvider } from '../theme/ThemeProvider'
import { TotemEntry } from './TotemEntry'

// Two deviations from the task brief's verbatim snippet, both forced by dependency
// versions that moved after the brief was written (react / react-router are pinned to
// "latest" -> 19.2 / 7.18 here):
//  1. The option buttons are triggered with `fireEvent.click` (the repo convention, and
//     what every other *.test.tsx here uses) instead of a raw `.click()`. Under React 19
//     + @testing-library/react 16 a raw `.click()` does not flush the router state update
//     outside `act()`, so the navigation assertion never sees the new route.
//  2. The button name matchers are anchored (`/^tenho código$/i`) because the plain
//     `/tenho código/i` also matches the second card ("Não tenho código" contains
//     "tenho código"), which makes `getByRole` throw on multiple matches.
// Structure, copy, routes and every assertion are otherwise identical to the brief.

vi.stubGlobal('matchMedia', (q: string) => ({
  matches: false, media: q, addEventListener: vi.fn(), removeEventListener: vi.fn(),
  addListener: vi.fn(), removeListener: vi.fn(), onchange: null, dispatchEvent: vi.fn(),
}))

const renderAt = () => render(
  <ThemeProvider><MemoryRouter initialEntries={['/totem']}>
    <Routes>
      <Route path="/totem" element={<TotemEntry />} />
      <Route path="/totem/check-in" element={<div>CHECKIN</div>} />
      <Route path="/totem/profissionais" element={<div>CARROSSEL</div>} />
      <Route path="/totem/salas" element={<div>SALAS</div>} />
    </Routes>
  </MemoryRouter></ThemeProvider>,
)

test('asks only how to continue, with two options and one support line', () => {
  renderAt()
  expect(screen.getByRole('img', { name: 'LUMIS' })).toBeInTheDocument()
  expect(screen.getByText('Bem-vindo')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /como deseja continuar\?/i })).toBeInTheDocument()
  expect(screen.getByText('Toque em uma opção para continuar.')).toBeInTheDocument()
  expect(screen.getByText(/^\d{2}:\d{2}$/)).toBeInTheDocument()        // KioskClock
  expect(screen.queryByLabelText(/código/i)).not.toBeInTheDocument()    // no code field
})

test('"Tenho código" navigates to /totem/check-in', () => {
  renderAt()
  fireEvent.click(screen.getByRole('button', { name: /^tenho código$/i }))
  expect(screen.getByText('CHECKIN')).toBeInTheDocument()
})

test('"Não tenho código" navigates to /totem/profissionais', () => {
  renderAt()
  fireEvent.click(screen.getByRole('button', { name: /^não tenho código$/i }))
  expect(screen.getByText('CARROSSEL')).toBeInTheDocument()
})

test('the kiosk no longer offers "Alugar sala" — renting moved to the public landing page', () => {
  renderAt()
  expect(screen.queryByRole('button', { name: /alugar sala/i })).not.toBeInTheDocument()
  expect(screen.getAllByRole('button')).toHaveLength(2)
})
