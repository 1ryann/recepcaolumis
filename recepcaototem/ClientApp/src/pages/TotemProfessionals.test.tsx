import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useSearchParams } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { totemApi } from '../api/modules'
import { TotemProfessionals } from './TotemProfessionals'

// Deviations from the task brief's verbatim test snippet (all harness-only; every queried
// role/name/text and every asserted result string stays byte-identical to the brief —
// see task-9-report.md for the full rationale):
//  1. Navigation clicks use `fireEvent.click` instead of a raw `.click()`. Under
//     react 19 / react-router 7 / @testing-library/react 16 a raw `.click()` does not
//     flush the router state update outside `act()` (the repo convention — see
//     `Login.test.tsx`). Route-change assertions are awaited via `findBy*` / `waitFor`.
//     The success test also waits for the Continuar button to be enabled first (the
//     carousel sets the active professional from its own mount effect) — an adaptation
//     the brief explicitly permits.
//  2. `Dest()` reads the query through `useSearchParams()`. `MemoryRouter` never updates
//     `window.location`, so the brief's `new URLSearchParams(window.location.search)`
//     could never observe `professionalId` and `AGENDAR professionalId=a` could never pass.
//  3. The brief's `matchMedia` + `scrollTo`/`scrollBy` stubs are kept verbatim.

vi.mock('../api/modules', async (orig) => ({
  ...(await orig<typeof import('../api/modules')>()),
  totemApi: { professionals: vi.fn() },
}))
vi.stubGlobal('matchMedia', (q: string) => ({
  matches: false, media: q, addEventListener: vi.fn(), removeEventListener: vi.fn(),
  addListener: vi.fn(), removeListener: vi.fn(), onchange: null, dispatchEvent: vi.fn(),
}))
beforeEach(() => {
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', { value: vi.fn(), writable: true })
  Object.defineProperty(HTMLElement.prototype, 'scrollBy', { value: vi.fn(), writable: true })
})
afterEach(() => vi.clearAllMocks())

const people = [
  { id: 'a', name: 'Ana Souza', profession: 'Fisio', photoUrl: null, status: 'AVAILABLE' as const },
  { id: 'b', name: 'Bruno Lima', profession: 'Psi', photoUrl: null, status: 'UNAVAILABLE' as const },
]
const renderAt = () => render(
  <MemoryRouter initialEntries={['/totem/profissionais']}>
    <Routes>
      <Route path="/totem/profissionais" element={<TotemProfessionals />} />
      <Route path="/totem" element={<div>ENTRY</div>} />
      <Route path="/totem/check-in" element={<div>CHECKIN</div>} />
      <Route path="/cliente/agendar" element={<Dest />} />
    </Routes>
  </MemoryRouter>,
)
function Dest() {
  const [p] = useSearchParams()
  return <div>AGENDAR professionalId={p.get('professionalId')}</div>
}

test('loading shows the kiosk layout with skeleton cards', () => {
  vi.mocked(totemApi.professionals).mockReturnValue(new Promise(() => {}))
  renderAt()
  expect(screen.getByRole('heading', { name: /escolha o profissional/i })).toBeInTheDocument()
  expect(screen.getAllByTestId('totem-skeleton-card').length).toBeGreaterThan(0)
})

test('success -> carousel + Continuar (uses the active professional id) + Voltar', async () => {
  vi.mocked(totemApi.professionals).mockResolvedValue(people)
  renderAt()
  await screen.findByRole('listbox')
  const continuar = screen.getByRole('button', { name: /continuar/i })
  await waitFor(() => expect(continuar).toBeEnabled())
  fireEvent.click(continuar)
  await waitFor(() => expect(screen.getByText('AGENDAR professionalId=a')).toBeInTheDocument())
})

test('Voltar goes to /totem', async () => {
  vi.mocked(totemApi.professionals).mockResolvedValue(people)
  renderAt()
  await screen.findByRole('listbox')
  fireEvent.click(screen.getByRole('button', { name: /voltar/i }))
  expect(await screen.findByText('ENTRY')).toBeInTheDocument()
})

test('empty -> message + Tentar novamente + Tenho código', async () => {
  vi.mocked(totemApi.professionals).mockResolvedValue([])
  renderAt()
  expect(await screen.findByText('Nenhum profissional disponível.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /tentar novamente/i })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /tenho código/i }))
  expect(await screen.findByText('CHECKIN')).toBeInTheDocument()
})

test('error -> message + Tentar novamente (refetches) + Tenho código', async () => {
  vi.mocked(totemApi.professionals).mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(people)
  renderAt()
  expect(await screen.findByText('Não foi possível carregar os profissionais.')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))
  await screen.findByRole('listbox')
})
