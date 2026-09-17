import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, expect, test, vi } from 'vitest'
import { useSession } from '../../auth/SessionProvider'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { apiClient } from '../../api/client'
import { professionalReservationsApi, professionalVisitsApi } from '../../api/modules'
import { ProfessionalShell } from './ProfessionalHome'

vi.mock('../../auth/SessionProvider', () => ({ useSession: vi.fn() }))
vi.mock('../../api/client', async (orig) => ({
  ...(await orig<typeof import('../../api/client')>()),
  apiClient: { get: vi.fn() },
}))
vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalReservationsApi: { list: vi.fn() },
  professionalVisitsApi: { list: vi.fn() },
}))

const profile = { name: 'Helena Souza Ramos', profession: 'Fisioterapeuta', description: null, photoUrl: null }
const emptyPage = { items: [], page: 1, pageSize: 50, totalCount: 0 }

function renderShell(entry = '/profissional') {
  return render(
    <ThemeProvider>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/profissional" element={<ProfessionalShell />}>
            <Route index element={<div>conteúdo do painel</div>} />
            <Route path="agenda" element={<div>conteúdo de agenda</div>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </ThemeProvider>,
  )
}

const logout = vi.fn().mockResolvedValue(undefined)

beforeEach(() => {
  vi.mocked(useSession).mockReturnValue({ user: { displayName: 'Helena Souza Ramos', email: 'helena@example.com', roles: ['PROFISSIONAL'] }, logout } as never)
  vi.mocked(apiClient.get).mockResolvedValue(profile)
  vi.mocked(professionalReservationsApi.list).mockResolvedValue(emptyPage as never)
  vi.mocked(professionalVisitsApi.list).mockResolvedValue(emptyPage as never)
  logout.mockClear()
})

test('renders the LUMIS logo with the correct alt text, theme-aware asset', async () => {
  // No stored preference and no matchMedia in jsdom => ThemeProvider falls back to 'dark'
  // (see getSystemTheme), so the dark (white-on-transparent) wordmark is expected here.
  renderShell()
  const logo = await screen.findByAltText('LUMIS')
  expect(logo).toHaveAttribute('src', '/lumis-logo-transparent.png')
})

test('renders the light wordmark asset when the theme is explicitly light', async () => {
  localStorage.setItem('lumis-theme', 'light')
  renderShell()
  const logo = await screen.findByAltText('LUMIS')
  expect(logo).toHaveAttribute('src', '/lumis-logo-dark.png')
  localStorage.removeItem('lumis-theme')
})

test('nav has exactly the real professional routes, in a nav landmark', async () => {
  renderShell()
  await screen.findByText('conteúdo do painel')
  const nav = screen.getByRole('navigation', { name: /profissional/i })
  const links = within(nav).getAllByRole('link')
  expect(links).toHaveLength(8)
  expect(links.map((link) => link.getAttribute('href'))).toEqual([
    '/profissional',
    '/profissional/agenda',
    '/profissional/reservas',
    '/profissional/atendimentos',
    '/profissional/locacoes',
    '/profissional/disponibilidade',
    '/profissional/financeiro',
    '/profissional/perfil',
  ])
})

test('topbar shows the loaded profile name in the profile chip, never a hardcoded name, and no greeting heading', async () => {
  renderShell()
  expect((await screen.findAllByText('Helena Souza Ramos')).length).toBeGreaterThan(0)
  expect(screen.queryByText(/Dra\. Helena/i)).not.toBeInTheDocument()
  expect(screen.queryByText(/Olá,/i)).not.toBeInTheDocument()
  expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument()
})

test('has a mobile menu toggle with an accessible label that opens the drawer', async () => {
  renderShell()
  await screen.findByText('conteúdo do painel')
  const toggle = screen.getByRole('button', { name: /abrir menu/i })
  fireEvent.click(toggle)
  expect(screen.getByRole('button', { name: /fechar menu/i })).toBeInTheDocument()
})

test('clicking the overlay closes the drawer', async () => {
  renderShell()
  await screen.findByText('conteúdo do painel')
  fireEvent.click(screen.getByRole('button', { name: /abrir menu/i }))
  const overlay = screen.getByRole('button', { name: /fechar menu/i })
  fireEvent.click(overlay)
  expect(screen.queryByRole('button', { name: /fechar menu/i })).not.toBeInTheDocument()
})

test('logging out calls session.logout', async () => {
  renderShell()
  await screen.findByText('conteúdo do painel')
  fireEvent.click(screen.getByRole('button', { name: /sair/i }))
  expect(logout).toHaveBeenCalled()
})

test('keeps rendering the routed page content through the outlet', async () => {
  renderShell('/profissional/agenda')
  expect(await screen.findByText('conteúdo de agenda')).toBeInTheDocument()
})
