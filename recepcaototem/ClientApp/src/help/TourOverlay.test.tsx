import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { SessionProvider } from '../auth/SessionProvider'
import { TourProvider } from './TourProvider'
import { criarTourProgressStore } from './tourStorage'

function sessaoAdmin() {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify({
    userId: 'u1', displayName: 'Admin Real', email: 'admin@lumis.test', roles: ['ADMINISTRADOR'], mustChangePassword: false,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
}

function montar(store = criarTourProgressStore()) {
  return render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider>
        <TourProvider store={store}>
          <nav data-tour="nav-lateral">Navegação</nav>
          <button data-tour="nova-sala">Nova sala</button>
        </TourProvider>
      </SessionProvider>
    </MemoryRouter>,
  )
}

test('mostra o primeiro passo como diálogo acessível', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  montar()
  const dialogo = await waitFor(() => screen.getByRole('dialog'))
  expect(dialogo).toHaveAttribute('aria-modal', 'true')
  expect(screen.getByText('A navegação')).toBeInTheDocument()
})

test('avança e mostra o progresso', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  montar()
  await waitFor(() => screen.getByRole('dialog'))
  expect(screen.getByText(/passo 1 de/i)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Avançar' }))
  await waitFor(() => expect(screen.getByText(/passo 2 de/i)).toBeInTheDocument())
})

test('voltar retorna ao passo anterior', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  montar()
  await waitFor(() => screen.getByRole('dialog'))
  fireEvent.click(screen.getByRole('button', { name: 'Avançar' }))
  await waitFor(() => expect(screen.getByText(/passo 2 de/i)).toBeInTheDocument())
  fireEvent.click(screen.getByRole('button', { name: 'Voltar' }))
  await waitFor(() => expect(screen.getByText(/passo 1 de/i)).toBeInTheDocument())
})

test('pular encerra o tour e grava o progresso', async () => {
  const store = criarTourProgressStore()
  vi.stubGlobal('fetch', sessaoAdmin())
  montar(store)
  await waitFor(() => screen.getByRole('dialog'))
  fireEvent.click(screen.getByRole('button', { name: 'Pular tutorial' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(store.ler('admin')?.estado).toBe('pulado')
})

test('Escape encerra o tour', async () => {
  const store = criarTourProgressStore()
  vi.stubGlobal('fetch', sessaoAdmin())
  montar(store)
  await waitFor(() => screen.getByRole('dialog'))
  fireEvent.keyDown(document, { key: 'Escape' })
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(store.ler('admin')?.estado).toBe('pulado')
})

test('pula o passo cujo alvo não existe na tela', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider>
        <TourProvider>
          <button data-tour="nova-sala">Nova sala</button>
        </TourProvider>
      </SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(screen.getByText('Cadastrar uma sala')).toBeInTheDocument(), { timeout: 4000 })
})
