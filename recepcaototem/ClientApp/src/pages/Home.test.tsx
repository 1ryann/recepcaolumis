import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { Home } from './Home'
import { useSession } from '../auth/SessionProvider'

vi.mock('../auth/SessionProvider', () => ({ useSession: vi.fn() }))

function renderWith(session: { status: string; user: { roles: string[] } | null }) {
  vi.mocked(useSession).mockReturnValue(session as never)
  return render(<MemoryRouter><Home /></MemoryRouter>)
}

const anonymous = { status: 'anonymous', user: null }
const authed = (roles: string[]) => ({ status: 'authenticated', user: { roles } })

test('/ no longer shows the "Módulo ainda não disponível" placeholder', () => {
  renderWith(anonymous)
  expect(screen.queryByText(/Módulo ainda não disponível/i)).not.toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /Gestão inteligente/i })).toBeInTheDocument()
})

test('a visitor sees the three ways in, pointing at the public login routes', () => {
  renderWith(anonymous)
  expect(screen.getByRole('link', { name: /Área do Cliente/i })).toHaveAttribute('href', '/cliente/login')
  expect(screen.getByRole('link', { name: /Área do Profissional/i })).toHaveAttribute('href', '/profissional/login')
  expect(screen.getByRole('link', { name: /Acesso da equipe/i })).toHaveAttribute('href', '/login')
})

test('an authenticated CUSTOMER gets a shortcut into /cliente', () => {
  renderWith(authed(['CUSTOMER']))
  const link = screen.getByRole('link', { name: /Ir para minha área/i })
  expect(link).toHaveAttribute('href', '/cliente')
  expect(screen.queryByRole('link', { name: /^Área do Cliente/i })).not.toBeInTheDocument()
})

test('an authenticated PROFISSIONAL gets a shortcut into /profissional', () => {
  renderWith(authed(['PROFISSIONAL']))
  expect(screen.getByRole('link', { name: /Ir para minha área/i })).toHaveAttribute('href', '/profissional')
})

test('an authenticated ADMINISTRADOR sends the team access to /admin', () => {
  renderWith(authed(['ADMINISTRADOR']))
  expect(screen.getByRole('link', { name: /Ir para Administração/i })).toHaveAttribute('href', '/admin')
})

test('an authenticated GERENTE sends the team access to /recepcao', () => {
  renderWith(authed(['GERENTE']))
  expect(screen.getByRole('link', { name: /Ir para Recepção/i })).toHaveAttribute('href', '/recepcao')
})

test('a failing session check does not remove the Home — it renders as public', () => {
  renderWith({ status: 'error', user: null })
  expect(screen.getByRole('heading', { name: /Gestão inteligente/i })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: /Área do Cliente/i })).toHaveAttribute('href', '/cliente/login')
  expect(screen.getByRole('link', { name: /Área do Profissional/i })).toHaveAttribute('href', '/profissional/login')
  expect(screen.getByRole('link', { name: /Acesso da equipe/i })).toHaveAttribute('href', '/login')
})

test('while the session is still loading the Home shows the public login targets', () => {
  renderWith({ status: 'loading', user: null })
  expect(screen.getByRole('link', { name: /Área do Cliente/i })).toHaveAttribute('href', '/cliente/login')
  expect(screen.getByRole('link', { name: /Acesso da equipe/i })).toHaveAttribute('href', '/login')
})
