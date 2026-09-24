import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { SessionProvider } from '../auth/SessionProvider'
import { TourProvider, useTour } from './TourProvider'
import { criarTourProgressStore } from './tourStorage'

function sessaoDe(roles: string[], mustChangePassword = false) {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify({
    userId: 'u1', displayName: 'Admin Real', email: 'admin@lumis.test', roles, mustChangePassword,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
}

function Espiao() {
  const tour = useTour()
  return <div>{tour.trilha ? `${tour.trilha.id}:${tour.passo?.id}:${tour.indice + 1}/${tour.total}` : 'sem-tour'}</div>
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

test('o gerente não recebe o passo restrito a administradores', async () => {
  vi.stubGlobal('fetch', sessaoDe(['GERENTE']))
  montar()
  await waitFor(() => expect(screen.getByText(/^admin:/)).toBeInTheDocument())
  const total = Number(screen.getByText(/^admin:/).textContent!.split('/')[1])
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  const administrador = render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider><TourProvider><Espiao /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(within(administrador.container).getByText(/^admin:/)).toBeInTheDocument())
  expect(Number(within(administrador.container).getByText(/^admin:/).textContent!.split('/')[1])).toBe(total + 1)
})

test('o profissional não recebe tour, porque a trilha dele não tem passos ancorados', async () => {
  vi.stubGlobal('fetch', sessaoDe(['PROFISSIONAL']))
  montar()
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})
