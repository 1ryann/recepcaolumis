import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import { SessionProvider } from '../auth/SessionProvider'
import { apiClient, ApiError } from '../api/client'
import { Login } from './Login'

vi.mock('../api/client', async (original) => ({ ...await original<typeof import('../api/client')>(), apiClient: { get: vi.fn(), post: vi.fn() } }))
const user = (role: string) => ({ userId: role, displayName: role, email: 'test@example.test', roles: [role], mustChangePassword: false })
test.each([['CUSTOMER', '/cliente'], ['PROFISSIONAL', '/profissional'], ['PROFESSIONAL_APPLICANT', '/profissional/aguardando'], ['ADMINISTRADOR', '/admin'], ['GERENTE', '/recepcao']])('redirects fresh %s session to %s regardless of login presentation', async (role, destination) => {
  vi.mocked(apiClient.get).mockRejectedValueOnce(new ApiError(401, 'UNAUTHORIZED', '')).mockResolvedValue(user(role))
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  render(<MemoryRouter initialEntries={['/cliente/login']}><SessionProvider><Routes><Route path="/cliente/login" element={<Login audience="customer" />} /><Route path={destination} element={<p>correct destination</p>} /></Routes></SessionProvider></MemoryRouter>)
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: 'test@example.test' } })
  fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'test-only' } })
  fireEvent.click(screen.getByRole('button', { name: /Entrar/ }))
  expect(await screen.findByText('correct destination')).toBeInTheDocument()
})
