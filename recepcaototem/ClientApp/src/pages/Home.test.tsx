import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { Home } from './Home'
import { useSession } from '../auth/SessionProvider'
import { ThemeProvider } from '../theme/ThemeProvider'

vi.mock('../auth/SessionProvider', () => ({ useSession: vi.fn() }))

function renderWith(session: { status: string; user: { roles: string[] } | null }) {
  vi.mocked(useSession).mockReturnValue(session as never)
  return render(<ThemeProvider><MemoryRouter><Home /></MemoryRouter></ThemeProvider>)
}

const anonymous = { status: 'anonymous', user: null }
const authed = (roles: string[]) => ({ status: 'authenticated', user: { roles } })

test('the landing presents the building and leads with renting a room', () => {
  renderWith(anonymous)
  expect(screen.queryByText(/Módulo ainda não disponível/i)).not.toBeInTheDocument()
  const rent = screen.getByRole('link', { name: /alugar sala/i })
  expect(rent).toHaveAttribute('href', '/salas')
  // The visitor also gets a way in as a customer, without competing with the main action.
  expect(screen.getByRole('link', { name: /sou cliente/i })).toHaveAttribute('href', '/cliente/login')
})

test('it explains the space and how renting works', () => {
  renderWith(anonymous)
  expect(screen.getByRole('heading', { name: /o espaço/i })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /como funciona/i })).toBeInTheDocument()
  expect(screen.getByText(/whatsapp/i)).toBeInTheDocument()
  expect(screen.getByText(/qr/i)).toBeInTheDocument()
})

test('a visitor still finds the three ways in, pointing at the public login routes', () => {
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
  expect(screen.getByRole('link', { name: /alugar sala/i })).toHaveAttribute('href', '/salas')
  expect(screen.getByRole('link', { name: /Área do Cliente/i })).toHaveAttribute('href', '/cliente/login')
  expect(screen.getByRole('link', { name: /Área do Profissional/i })).toHaveAttribute('href', '/profissional/login')
  expect(screen.getByRole('link', { name: /Acesso da equipe/i })).toHaveAttribute('href', '/login')
})
