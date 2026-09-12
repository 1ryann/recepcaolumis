import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, expect, test, vi } from 'vitest'
import { useSession } from '../../auth/SessionProvider'
import { customerApi, type CustomerProfileDto } from '../../api/modules'
import { CustomerShell } from './CustomerHome'

vi.mock('../../auth/SessionProvider', () => ({ useSession: vi.fn() }))
vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  customerApi: { me: vi.fn() },
}))

const profile: CustomerProfileDto = {
  id: 'c1', name: 'Marina Alves', phone: '11999990000', isActive: true, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
}

function renderShell(entry = '/cliente') {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/cliente" element={<CustomerShell />}>
          <Route index element={<div>conteúdo do painel</div>} />
          <Route path="agendamentos" element={<div>conteúdo de agendamentos</div>} />
          <Route path="agendar" element={<div>conteúdo de agendar</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

const logout = vi.fn().mockResolvedValue(undefined)

beforeEach(() => {
  vi.mocked(useSession).mockReturnValue({ user: { displayName: 'Marina Alves', email: 'marina@example.com', roles: ['CUSTOMER'] }, logout } as never)
  vi.mocked(customerApi.me).mockResolvedValue(profile)
  logout.mockClear()
})

test('renders the LUMIS logo with the correct alt text', async () => {
  renderShell()
  const logo = await screen.findByAltText('LUMIS')
  expect(logo).toHaveAttribute('src', '/lumis-logo-transparent.png')
})

test('nav has exactly 3 links pointing at the real customer routes', async () => {
  renderShell()
  await screen.findByText('conteúdo do painel')
  const nav = screen.getByRole('navigation', { name: /cliente/i })
  const links = within(nav).getAllByRole('link')
  expect(links).toHaveLength(3)
  expect(links[0]).toHaveTextContent(/dashboard/i)
  expect(links[0]).toHaveAttribute('href', '/cliente')
  expect(links[1]).toHaveTextContent(/agendamentos/i)
  expect(links[1]).toHaveAttribute('href', '/cliente/agendamentos')
  expect(links[2]).toHaveTextContent(/novo agendamento/i)
  expect(links[2]).toHaveAttribute('href', '/cliente/agendar')
})

test('does not render nav items for routes that do not exist', async () => {
  renderShell()
  await screen.findByText('conteúdo do painel')
  expect(screen.queryByRole('link', { name: /reservas/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /check-in/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /profissionais/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /perfil/i })).not.toBeInTheDocument()
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

test('shows the profile chip once loaded, with no greeting heading', async () => {
  renderShell()
  expect(await screen.findByText('Marina Alves')).toBeInTheDocument()
  expect(screen.getByText('MA')).toBeInTheDocument()
  expect(screen.queryByText(/Olá,/i)).not.toBeInTheDocument()
})

test('logging out calls session.logout', async () => {
  renderShell()
  await screen.findByText('conteúdo do painel')
  fireEvent.click(screen.getByRole('button', { name: /sair/i }))
  expect(logout).toHaveBeenCalled()
})

test('keeps rendering the routed page content through the outlet', async () => {
  renderShell('/cliente/agendamentos')
  expect(await screen.findByText('conteúdo de agendamentos')).toBeInTheDocument()
})
