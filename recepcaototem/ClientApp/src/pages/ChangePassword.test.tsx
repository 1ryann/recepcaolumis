import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { apiClient, ApiError } from '../api/client'
import { SessionProvider } from '../auth/SessionProvider'
import { ThemeProvider } from '../theme/ThemeProvider'
import { ChangePassword } from './ChangePassword'

vi.mock('../api/client', async (original) => ({ ...await original<typeof import('../api/client')>(), apiClient: { get: vi.fn(), post: vi.fn() } }))

const identity = (mustChangePassword: boolean) => ({
  userId: 'u1', displayName: 'Ana Souza', email: 'ana@lumis.test', roles: ['ADMINISTRADOR'], mustChangePassword,
})

// Real SessionProvider over a mocked apiClient, as in Login.test.tsx: the screen branches on the
// session status, so a hand-rolled context would test the mock instead of the screen.
async function renderScreen(mustChangePassword: boolean) {
  vi.mocked(apiClient.get).mockResolvedValue(identity(mustChangePassword))
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  render(
    <ThemeProvider>
      <MemoryRouter initialEntries={['/change-password']}>
        <SessionProvider>
          <Routes>
            <Route path="/change-password" element={<ChangePassword />} />
            <Route path="/admin" element={<p>painel de administração</p>} />
          </Routes>
        </SessionProvider>
      </MemoryRouter>
    </ThemeProvider>,
  )
  await screen.findByRole('heading', { name: 'Alterar senha' })
}

const fill = (current: string, next: string, confirmation: string) => {
  fireEvent.change(screen.getByLabelText(/senha (atual|temporária)/i), { target: { value: current } })
  fireEvent.change(screen.getByLabelText('Nova senha'), { target: { value: next } })
  fireEvent.change(screen.getByLabelText('Confirmar nova senha'), { target: { value: confirmation } })
  fireEvent.click(screen.getByRole('button', { name: 'Salvar nova senha' }))
}

// The regression: the screen redirected every authenticated visitor to their home page, so
// "Alterar senha" in the account menu only ever flashed and came back.
test('an already authenticated user gets the form instead of being sent home', async () => {
  await renderScreen(false)
  expect(screen.getByLabelText('Senha atual')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: /cancelar/i })).toHaveAttribute('href', '/admin')
  expect(screen.queryByText('painel de administração')).not.toBeInTheDocument()
})

test('the first access keeps the mandatory wording and no way around it', async () => {
  await renderScreen(true)
  expect(screen.getByLabelText('Senha temporária')).toBeInTheDocument()
  expect(screen.getByText('Esta troca é obrigatória no primeiro acesso.')).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /cancelar/i })).not.toBeInTheDocument()
})

test('a confirmation that does not match never reaches the API', async () => {
  await renderScreen(false)
  fill('senha-atual', 'novasenha1', 'novasenha2')
  expect(await screen.findByRole('alert')).toHaveTextContent('A confirmação não confere com a nova senha.')
  expect(apiClient.post).not.toHaveBeenCalled()
})

test('a voluntary change confirms on screen and stays put', async () => {
  await renderScreen(false)
  fill('senha-atual', 'novasenha1', 'novasenha1')
  await waitFor(() => expect(apiClient.post).toHaveBeenCalledWith('/api/auth/change-password', {
    currentPassword: 'senha-atual', newPassword: 'novasenha1', confirmation: 'novasenha1',
  }))
  expect(await screen.findByText('Senha alterada. Use a nova senha no próximo acesso.')).toBeInTheDocument()
  expect(screen.queryByText('painel de administração')).not.toBeInTheDocument()
})

test('the mandatory change lands on the home of the role', async () => {
  await renderScreen(true)
  // The account stops carrying the flag the moment the password changes.
  vi.mocked(apiClient.get).mockResolvedValue(identity(false))
  fill('temporaria-123', 'novasenha1', 'novasenha1')
  expect(await screen.findByText('painel de administração')).toBeInTheDocument()
})

test('a rejected change explains what to check', async () => {
  await renderScreen(false)
  vi.mocked(apiClient.post).mockRejectedValue(new ApiError(400, 'INVALID_PASSWORD_CHANGE', ''))
  fill('errada', 'novasenha1', 'novasenha1')
  expect(await screen.findByRole('alert')).toHaveTextContent(/Confira a senha atual/)
})
