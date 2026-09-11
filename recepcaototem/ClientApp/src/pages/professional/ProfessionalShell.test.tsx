import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, expect, test, vi } from 'vitest'
import { useSession } from '../../auth/SessionProvider'
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
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/profissional" element={<ProfessionalShell />}>
          <Route index element={<div>conteúdo do painel</div>} />
          <Route path="agenda" element={<div>conteúdo de agenda</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
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

test('renders the LUMIS logo with the correct alt text', async () => {
  renderShell()
  const logo = await screen.findByAltText('LUMIS')
  expect(logo).toHaveAttribute('src', '/lumis-logo-transparent.png')
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

test('topbar shows the PROFISSIONAL eyebrow', async () => {
  renderShell()
  await screen.findByText('conteúdo do painel')
  expect(screen.getByText('PROFISSIONAL')).toBeInTheDocument()
})

test('greets the professional using the loaded profile name, never a hardcoded name', async () => {
  renderShell()
  expect(await screen.findByText('Olá, Helena!')).toBeInTheDocument()
  expect(screen.queryByText(/Dra\. Helena/i)).not.toBeInTheDocument()
  expect(screen.getByText('Seu espaço, sua agenda, mais possibilidades.')).toBeInTheDocument()
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
