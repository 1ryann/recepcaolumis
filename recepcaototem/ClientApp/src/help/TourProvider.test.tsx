import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, useNavigate } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { SessionProvider, useSession } from '../auth/SessionProvider'
import { trilhaPorId } from './content'
import { TourProvider, useTour } from './TourProvider'
import { criarTourProgressStore } from './tourStorage'

function sessaoDe(roles: string[], mustChangePassword = false) {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify({
    userId: 'u1', displayName: 'Admin Real', email: 'admin@lumis.test', roles, mustChangePassword,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
}

function sessaoRenovavel(roles: string[]) {
  return vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
    userId: 'u1', displayName: 'Admin Real', email: 'admin@lumis.test', roles, mustChangePassword: false,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
}

function Espiao() {
  const tour = useTour()
  return <div>{tour.trilha ? `${tour.trilha.id}:${tour.passo?.id}:${tour.indice + 1}/${tour.total}` : 'sem-tour'}</div>
}

function EspiaoComLogout() {
  const tour = useTour()
  const session = useSession()
  return (
    <div>
      <span>{tour.trilha ? `${tour.trilha.id}:${tour.passo?.id}:${tour.indice + 1}/${tour.total}` : 'sem-tour'}</span>
      <button type="button" onClick={() => { void session.logout() }}>Sair</button>
    </div>
  )
}

function EspiaoComNavegacao() {
  const tour = useTour()
  const navigate = useNavigate()
  return (
    <div>
      <span>{tour.trilha ? `${tour.trilha.id}:${tour.passo?.id}:${tour.indice + 1}/${tour.total}` : 'sem-tour'}</span>
      <button type="button" onClick={() => navigate('/ajuda')}>Ir para ajuda</button>
    </div>
  )
}

function montar(rota = '/admin') {
  return render(
    <MemoryRouter initialEntries={[rota]}>
      <SessionProvider><TourProvider><Espiao /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
}

test('inicia a trilha do administrador no primeiro passo ancorado', async () => {
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  montar()
  await waitFor(() => expect(screen.getByText(/^admin:admin-navegacao:1\//)).toBeInTheDocument())
})

test('não inicia para usuário anônimo', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })))
  montar()
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})

test('não inicia durante troca de senha obrigatória', async () => {
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR'], true))
  montar()
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})

test('não inicia na central de ajuda', async () => {
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  montar('/ajuda')
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})

test('não reinicia quando a trilha já foi concluída na mesma versão', async () => {
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'concluido', ultimoPasso: 'admin-paginacao', versao: 1 })
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider><TourProvider store={store}><Espiao /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})

test('reinicia quando o progresso salvo é de uma versão anterior', async () => {
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'concluido', ultimoPasso: 'admin-paginacao', versao: 0 })
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider><TourProvider store={store}><Espiao /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(screen.getByText(/^admin:admin-navegacao/)).toBeInTheDocument())
})

test('o gerente não recebe tour, porque a trilha do painel percorre /admin e o portal dele é /recepcao', async () => {
  vi.stubGlobal('fetch', sessaoDe(['GERENTE']))
  montar()
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})

test('todo passo ancorado aponta para um elemento que existe sem depender de dados cadastrados', () => {
  const ancorados = trilhaPorId('admin')!.passos.filter(passo => passo.alvo).map(passo => passo.alvo)
  expect(ancorados).not.toContain('acoes-profissional')
  expect(ancorados).not.toContain('paginacao-profissionais')
})

test('o profissional não recebe tour, porque a trilha dele não tem passos ancorados', async () => {
  vi.stubGlobal('fetch', sessaoDe(['PROFISSIONAL']))
  montar()
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})

test('encerra o tour ao sair da sessão, sem gravar progresso', async () => {
  const store = criarTourProgressStore()
  vi.stubGlobal('fetch', sessaoRenovavel(['ADMINISTRADOR']))
  render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider><TourProvider store={store}><EspiaoComLogout /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(screen.getByText(/^admin:/)).toBeInTheDocument())
  fireEvent.click(screen.getByRole('button', { name: 'Sair' }))
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
  expect(store.ler('admin')).toBeNull()
})

test('encerra o tour ao navegar para /ajuda, sem gravar progresso', async () => {
  const store = criarTourProgressStore()
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider><TourProvider store={store}><EspiaoComNavegacao /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(screen.getByText(/^admin:/)).toBeInTheDocument())
  fireEvent.click(screen.getByRole('button', { name: 'Ir para ajuda' }))
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
  expect(store.ler('admin')).toBeNull()
})
