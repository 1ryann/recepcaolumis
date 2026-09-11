import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { totemApi } from '../api/modules'
import { TotemProfessionals } from './TotemProfessionals'

// `useNavigate` is spied so every navigation target can be asserted directly — in
// particular that the Totem "Continuar" flow reaches `/totem/handoff` with a full
// navigation `state` and NEVER a `/cliente/*` path. Every other react-router export
// (MemoryRouter, Routes, useSearchParams, …) stays real. The carousel/matchMedia/
// scroll stubs are carried over from the pre-handoff version of this file.
const navigateSpy = vi.fn()
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => navigateSpy,
}))
vi.mock('../api/modules', async (orig) => ({
  ...(await orig<typeof import('../api/modules')>()),
  totemApi: { professionals: vi.fn(), createHandoff: vi.fn() },
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
const handoff = {
  id: 'h1', handoffToken: 'H', statusToken: 'S', expiresAt: '2026-09-10T10:00:00Z',
  professionalName: 'Ana Souza', profession: 'Fisio',
}
const renderAt = () => render(
  <MemoryRouter initialEntries={['/totem/profissionais']}>
    <Routes>
      <Route path="/totem/profissionais" element={<TotemProfessionals />} />
    </Routes>
  </MemoryRouter>,
)
const noClienteNav = () => {
  for (const call of navigateSpy.mock.calls) {
    expect(String(call[0])).not.toContain('/cliente')
  }
}

test('loading shows the kiosk layout with skeleton cards', () => {
  vi.mocked(totemApi.professionals).mockReturnValue(new Promise(() => {}))
  renderAt()
  expect(screen.getByRole('heading', { name: /escolha o profissional/i })).toBeInTheDocument()
  expect(screen.getAllByTestId('totem-skeleton-card').length).toBeGreaterThan(0)
})

test('Continuar creates a handoff and navigates to /totem/handoff with the 7-field state', async () => {
  vi.mocked(totemApi.professionals).mockResolvedValue(people)
  vi.mocked(totemApi.createHandoff).mockResolvedValue(handoff)
  renderAt()
  await screen.findByRole('listbox')
  const continuar = screen.getByRole('button', { name: /continuar/i })
  await waitFor(() => expect(continuar).toBeEnabled())
  fireEvent.click(continuar)

  await waitFor(() => expect(totemApi.createHandoff).toHaveBeenCalledWith('a'))
  await waitFor(() => expect(navigateSpy).toHaveBeenCalledWith('/totem/handoff', {
    state: {
      handoffId: 'h1',
      handoffToken: 'H',
      statusToken: 'S',
      professionalId: 'a',
      professionalName: 'Ana Souza',
      profession: 'Fisio',
      expiresAt: '2026-09-10T10:00:00Z',
    },
  }))
  noClienteNav()
})

test('a createHandoff rejection shows an inline alert and does not navigate', async () => {
  vi.mocked(totemApi.professionals).mockResolvedValue(people)
  vi.mocked(totemApi.createHandoff).mockRejectedValue(new Error('boom'))
  renderAt()
  await screen.findByRole('listbox')
  const continuar = screen.getByRole('button', { name: /continuar/i })
  await waitFor(() => expect(continuar).toBeEnabled())
  fireEvent.click(continuar)

  expect(await screen.findByRole('alert')).toBeInTheDocument()
  expect(navigateSpy).not.toHaveBeenCalledWith('/totem/handoff', expect.anything())
  noClienteNav()
})

test('Voltar navigates to /totem', async () => {
  vi.mocked(totemApi.professionals).mockResolvedValue(people)
  renderAt()
  await screen.findByRole('listbox')
  fireEvent.click(screen.getByRole('button', { name: /voltar/i }))
  expect(navigateSpy).toHaveBeenCalledWith('/totem')
})

test('empty -> message + Tentar novamente + Tenho código', async () => {
  vi.mocked(totemApi.professionals).mockResolvedValue([])
  renderAt()
  expect(await screen.findByText('Nenhum profissional disponível.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /tentar novamente/i })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /tenho código/i }))
  expect(navigateSpy).toHaveBeenCalledWith('/totem/check-in')
})

test('error -> message + Tentar novamente (refetches)', async () => {
  vi.mocked(totemApi.professionals).mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(people)
  renderAt()
  expect(await screen.findByText('Não foi possível carregar os profissionais.')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))
  await screen.findByRole('listbox')
})
