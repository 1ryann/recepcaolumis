import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { SessionProvider } from '../auth/SessionProvider'
import { HelpCenter } from './HelpCenter'
import { TourProvider } from './TourProvider'
import { criarTourProgressStore } from './tourStorage'

function montar() {
  return render(
    <MemoryRouter initialEntries={['/ajuda']}>
      <SessionProvider><TourProvider><HelpCenter /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
}

function sessaoDe(roles: string[]) {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify({
    userId: 'u1', displayName: 'Admin Real', email: 'admin@lumis.test', roles, mustChangePassword: false,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
}

test('o gerente não recebe a trilha do painel, que percorre rotas negadas para ele', async () => {
  vi.stubGlobal('fetch', sessaoDe(['GERENTE']))
  montar()
  await waitFor(() => expect(screen.getByText('Para quem vai ser atendido')).toBeInTheDocument())
  expect(screen.queryByText('Para a administração')).not.toBeInTheDocument()
})

test('o profissional recebe a própria trilha, e não a do painel', async () => {
  vi.stubGlobal('fetch', sessaoDe(['PROFISSIONAL']))
  montar()
  await waitFor(() => expect(screen.getByText('Para profissionais')).toBeInTheDocument())
  expect(screen.queryByText('Para a administração')).not.toBeInTheDocument()
})

test('sem sessão mostra as trilhas públicas e não a do administrador', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })))
  montar()
  await waitFor(() => expect(screen.getByText('Para quem vai ser atendido')).toBeInTheDocument())
  expect(screen.getByText('Para profissionais')).toBeInTheDocument()
  expect(screen.queryByText('Para a administração')).not.toBeInTheDocument()
})

test('com sessão de administração mostra a trilha do perfil', async () => {
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  montar()
  await waitFor(() => expect(screen.getByText('Para a administração')).toBeInTheDocument())
  expect(screen.getByRole('button', { name: 'Refazer o tour' })).toBeInTheDocument()
})

test('o texto de cada passo da trilha do cliente aparece na página', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })))
  montar()
  await waitFor(() => expect(screen.getByText('Check-in por QR')).toBeInTheDocument())
  expect(screen.getByText('Foto e privacidade')).toBeInTheDocument()
})

// As três trilhas são ancoradas agora, e duas delas são públicas: sem esta restrição o
// cliente veria "Refazer o tour" na trilha do profissional, que percorre /profissional —
// uma rota que o ProtectedRoute nega para ele.
test('só a trilha do próprio perfil oferece refazer o tour', async () => {
  vi.stubGlobal('fetch', sessaoDe(['CUSTOMER']))
  montar()
  await waitFor(() => expect(screen.getByText('Para quem vai ser atendido')).toBeInTheDocument())
  expect(screen.getByText('Para profissionais')).toBeInTheDocument()
  const doProfissional = screen.getByText('Para profissionais').closest('section')!
  expect(within(doProfissional).queryByRole('button', { name: 'Refazer o tour' })).not.toBeInTheDocument()
  expect(screen.getAllByRole('button', { name: 'Refazer o tour' })).toHaveLength(1)
})

test('sem sessão nenhuma trilha oferece refazer o tour', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })))
  montar()
  await waitFor(() => expect(screen.getByText('Para quem vai ser atendido')).toBeInTheDocument())
  expect(screen.queryByRole('button', { name: 'Refazer o tour' })).not.toBeInTheDocument()
})

test('refazer o tour limpa o progresso salvo da trilha', async () => {
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'concluido', ultimoPasso: 'admin-onde-esta-ajuda', versao: 1 })
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  render(
    <MemoryRouter initialEntries={['/ajuda']}>
      <SessionProvider><TourProvider store={store}><HelpCenter /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(screen.getByRole('button', { name: 'Refazer o tour' })).toBeInTheDocument())
  fireEvent.click(screen.getByRole('button', { name: 'Refazer o tour' }))
  expect(store.ler('admin')).toBeNull()
})
