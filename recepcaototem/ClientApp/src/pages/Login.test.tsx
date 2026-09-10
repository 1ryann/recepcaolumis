import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useSearchParams } from 'react-router-dom'
import { vi } from 'vitest'
import { SessionProvider } from '../auth/SessionProvider'
import { apiClient, ApiError } from '../api/client'
import { Login } from './Login'

function BookingSink() {
  const [params] = useSearchParams()
  return <p>booking sink professionalId={params.get('professionalId')}</p>
}
const fillAndSubmitLogin = () => {
  fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: 'test@example.test' } })
  fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'test-only' } })
  fireEvent.click(screen.getByRole('button', { name: /Entrar/ }))
}

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

test('customer login with a safe returnUrl navigates there', async () => {
  vi.mocked(apiClient.get).mockRejectedValueOnce(new ApiError(401, 'UNAUTHORIZED', '')).mockResolvedValue(user('CUSTOMER'))
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  render(<MemoryRouter initialEntries={['/cliente/login?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx']}><SessionProvider><Routes>
    <Route path="/cliente/login" element={<Login audience="customer" />} />
    <Route path="/cliente/agendar" element={<BookingSink />} />
    <Route path="/cliente" element={<p>customer home</p>} />
  </Routes></SessionProvider></MemoryRouter>)
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  fillAndSubmitLogin()
  expect(await screen.findByText('booking sink professionalId=x')).toBeInTheDocument()
})

test('customer login ignores an unsafe returnUrl and uses homeForRoles', async () => {
  vi.mocked(apiClient.get).mockRejectedValueOnce(new ApiError(401, 'UNAUTHORIZED', '')).mockResolvedValue(user('CUSTOMER'))
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  render(<MemoryRouter initialEntries={['/cliente/login?returnUrl=%2F%2Fevil.com']}><SessionProvider><Routes>
    <Route path="/cliente/login" element={<Login audience="customer" />} />
    <Route path="/cliente" element={<p>customer home</p>} />
    <Route path="/cliente/agendar" element={<BookingSink />} />
  </Routes></SessionProvider></MemoryRouter>)
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  fillAndSubmitLogin()
  expect(await screen.findByText('customer home')).toBeInTheDocument()
  expect(screen.queryByText(/booking sink/)).not.toBeInTheDocument()
})

test('mustChangePassword wins over returnUrl', async () => {
  vi.mocked(apiClient.get).mockRejectedValueOnce(new ApiError(401, 'UNAUTHORIZED', '')).mockResolvedValue({ ...user('CUSTOMER'), mustChangePassword: true })
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  render(<MemoryRouter initialEntries={['/cliente/login?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx']}><SessionProvider><Routes>
    <Route path="/cliente/login" element={<Login audience="customer" />} />
    <Route path="/change-password" element={<p>change password page</p>} />
    <Route path="/cliente/agendar" element={<BookingSink />} />
  </Routes></SessionProvider></MemoryRouter>)
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  fillAndSubmitLogin()
  expect(await screen.findByText('change password page')).toBeInTheDocument()
})

test('admin login ignores returnUrl entirely', async () => {
  vi.mocked(apiClient.get).mockRejectedValueOnce(new ApiError(401, 'UNAUTHORIZED', '')).mockResolvedValue(user('ADMINISTRADOR'))
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  render(<MemoryRouter initialEntries={['/login?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx']}><SessionProvider><Routes>
    <Route path="/login" element={<Login audience="admin" />} />
    <Route path="/admin" element={<p>admin panel</p>} />
    <Route path="/cliente/agendar" element={<BookingSink />} />
  </Routes></SessionProvider></MemoryRouter>)
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  fillAndSubmitLogin()
  expect(await screen.findByText('admin panel')).toBeInTheDocument()
  expect(screen.queryByText(/booking sink/)).not.toBeInTheDocument()
})

test('the "Criar minha conta" link carries an encoded returnUrl when present', async () => {
  vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
  render(<MemoryRouter initialEntries={['/cliente/login?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx']}><SessionProvider><Routes>
    <Route path="/cliente/login" element={<Login audience="customer" />} />
  </Routes></SessionProvider></MemoryRouter>)
  const link = await screen.findByRole('link', { name: /criar minha conta/i })
  expect(link).toHaveAttribute('href', '/cliente/cadastro?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx')
})

test('the "Criar minha conta" link stays plain when no returnUrl is present', async () => {
  vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
  render(<MemoryRouter initialEntries={['/cliente/login']}><SessionProvider><Routes>
    <Route path="/cliente/login" element={<Login audience="customer" />} />
  </Routes></SessionProvider></MemoryRouter>)
  const link = await screen.findByRole('link', { name: /criar minha conta/i })
  expect(link).toHaveAttribute('href', '/cliente/cadastro')
})
