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
          <button data-tour="pagina-salas">Salas</button>
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

test('foca o cartão no primeiro passo', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  montar()
  const dialogo = await waitFor(() => screen.getByRole('dialog'))
  await waitFor(() => expect(document.activeElement).toBe(dialogo))
})

test('foca o cartão ao mudar para um passo com outro alvo', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  montar()
  await waitFor(() => screen.getByRole('dialog'))
  fireEvent.click(screen.getByRole('button', { name: 'Avançar' }))
  const dialogo = await waitFor(() => {
    const elemento = screen.getByRole('dialog')
    expect(elemento).toHaveTextContent('Salas e tarifas')
    return elemento
  })
  await waitFor(() => expect(document.activeElement).toBe(dialogo))
})

test('seta não avança o tour quando o foco está em um campo de texto', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider>
        <TourProvider>
          <nav data-tour="nav-lateral">Navegação</nav>
          <input aria-label="busca" />
        </TourProvider>
      </SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => screen.getByRole('dialog'))
  const campo = screen.getByLabelText('busca')
  campo.focus()
  fireEvent.keyDown(campo, { key: 'ArrowRight' })
  expect(screen.getByText(/passo 1 de/i)).toBeInTheDocument()
})

test('Shift+Tab logo após a troca de passo mantém o foco dentro do cartão', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  montar()
  const dialogo = await waitFor(() => screen.getByRole('dialog'))
  await waitFor(() => expect(document.activeElement).toBe(dialogo))
  fireEvent.keyDown(dialogo, { key: 'Tab', shiftKey: true })
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Avançar' }))
})

test('pula o passo cujo alvo não existe na tela', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider>
        <TourProvider>
          <button data-tour="pagina-salas">Salas</button>
        </TourProvider>
      </SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(screen.getByText('Salas e tarifas')).toBeInTheDocument(), { timeout: 4000 })
})

// A barra lateral off-canvas do celular continua no DOM, deslocada para fora da tela.
// Pular esses passos silenciava justamente o que ensina onde reencontrar o tutorial, cujo
// alvo é o botão de ajuda dentro dela. O passo aparece; o que some é o holofote, que não
// teria onde pousar.
function comAlvoForaDaTela() {
  const foraDaTela = { top: 0, left: -240, width: 210, height: 600, right: -30, bottom: 600, x: -240, y: 0, toJSON: () => ({}) } as DOMRect
  const original = Element.prototype.getBoundingClientRect
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    return this.getAttribute('data-tour') === 'nav-lateral' ? foraDaTela : original.call(this)
  })
}

test('mostra o passo cujo alvo está fora da viewport, sem holofote, em vez de pulá-lo', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  comAlvoForaDaTela()
  const { container } = montar()
  await waitFor(() => expect(screen.getByText('A navegação')).toBeInTheDocument())
  expect(screen.getByText(/passo 1 de/i)).toBeInTheDocument()
  expect(container.querySelector('.tour-spotlight')).toBeNull()
})

test('o passo com alvo fora da viewport continua avançando, não trava o tour', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  comAlvoForaDaTela()
  montar()
  await waitFor(() => expect(screen.getByText('A navegação')).toBeInTheDocument())
  fireEvent.click(screen.getByRole('button', { name: 'Avançar' }))
  await waitFor(() => expect(screen.getByText('Salas e tarifas')).toBeInTheDocument())
})

test('o passo cuja âncora está visível mantém o holofote', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  const visivel = { top: 40, left: 0, width: 210, height: 600, right: 210, bottom: 640, x: 0, y: 40, toJSON: () => ({}) } as DOMRect
  const original = Element.prototype.getBoundingClientRect
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    return this.getAttribute('data-tour') === 'nav-lateral' ? visivel : original.call(this)
  })
  const { container } = montar()
  await waitFor(() => expect(screen.getByText('A navegação')).toBeInTheDocument())
  await waitFor(() => expect(container.querySelector('.tour-spotlight')).not.toBeNull())
})
