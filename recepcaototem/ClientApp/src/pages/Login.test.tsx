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

type Audience = 'admin' | 'customer' | 'professional'
const pathFor: Record<Audience, string> = { admin: '/login', customer: '/cliente/login', professional: '/profissional/login' }
// Match the existing file's approach: real SessionProvider over a mocked apiClient. The mount
// call to /api/auth/session rejects (401) so status settles to 'anonymous' and the card renders.
async function renderLogin(audience: Audience = 'admin', entry?: string) {
  vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
  const path = pathFor[audience]
  const utils = render(
    <MemoryRouter initialEntries={[entry ?? path]}>
      <SessionProvider>
        <Routes>
          <Route path={path} element={<Login audience={audience} />} />
        </Routes>
      </SessionProvider>
    </MemoryRouter>,
  )
  await screen.findByRole('heading', { name: /bem-vindo de volta/i })
  return utils
}

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

test('submits the entered credentials to the session login', async () => {
  vi.mocked(apiClient.get).mockRejectedValueOnce(new ApiError(401, 'UNAUTHORIZED', '')).mockResolvedValue(user('ADMINISTRADOR'))
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  render(<MemoryRouter initialEntries={['/login']}><SessionProvider><Routes>
    <Route path="/login" element={<Login audience="admin" />} />
    <Route path="/admin" element={<p>admin panel</p>} />
  </Routes></SessionProvider></MemoryRouter>)
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  fillAndSubmitLogin()
  await screen.findByText('admin panel')
  expect(apiClient.post).toHaveBeenCalledWith('/api/auth/login', { email: 'test@example.test', password: 'test-only' })
})

test('shows an inline error when the credentials are rejected', async () => {
  vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
  vi.mocked(apiClient.post).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
  render(<MemoryRouter initialEntries={['/login']}><SessionProvider><Routes>
    <Route path="/login" element={<Login audience="admin" />} />
  </Routes></SessionProvider></MemoryRouter>)
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  fillAndSubmitLogin()
  expect(await screen.findByText('E-mail ou senha inválidos.')).toBeInTheDocument()
})

test('shows the cooldown message after too many attempts (429)', async () => {
  vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
  vi.mocked(apiClient.post).mockRejectedValue(new ApiError(429, 'TOO_MANY_REQUESTS', ''))
  render(<MemoryRouter initialEntries={['/login']}><SessionProvider><Routes>
    <Route path="/login" element={<Login audience="admin" />} />
  </Routes></SessionProvider></MemoryRouter>)
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  fillAndSubmitLogin()
  expect(await screen.findByText('Muitas tentativas. Aguarde alguns instantes e tente novamente.')).toBeInTheDocument()
})

test('the "Criar conta" link carries an encoded returnUrl when present', async () => {
  vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
  render(<MemoryRouter initialEntries={['/cliente/login?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx']}><SessionProvider><Routes>
    <Route path="/cliente/login" element={<Login audience="customer" />} />
  </Routes></SessionProvider></MemoryRouter>)
  const link = await screen.findByRole('link', { name: /criar conta/i })
  expect(link).toHaveAttribute('href', '/cliente/cadastro?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx')
})

test('the "Criar conta" link stays plain when no returnUrl is present', async () => {
  vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
  render(<MemoryRouter initialEntries={['/cliente/login']}><SessionProvider><Routes>
    <Route path="/cliente/login" element={<Login audience="customer" />} />
  </Routes></SessionProvider></MemoryRouter>)
  const link = await screen.findByRole('link', { name: /criar conta/i })
  expect(link).toHaveAttribute('href', '/cliente/cadastro')
})

test('customer login: discrete card, no social / no forgot / no remember-me', async () => {
  await renderLogin('customer')
  expect(screen.getByText('ÁREA DO CLIENTE')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /bem-vindo de volta/i })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: /criar conta/i })).toHaveAttribute('href', expect.stringContaining('/cliente/cadastro'))
  expect(screen.queryByText(/google|apple|icloud/i)).toBeNull()
  expect(screen.queryByText(/esqueci.*senha|recuperar senha/i)).toBeNull()
  expect(screen.queryByLabelText(/lembrar de mim|manter conectado/i)).toBeNull()
})

test('professional login points to the real registration flow', async () => {
  await renderLogin('professional')
  expect(screen.getByText('ÁREA DO PROFISSIONAL')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: /solicitar cadastro/i })).toHaveAttribute('href', '/profissional/cadastro')
})

test('admin login has no account-creation affordance', async () => {
  await renderLogin('admin')
  expect(screen.getByText('ADMINISTRAÇÃO')).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /criar conta|solicitar/i })).toBeNull()
})

test('customer returnUrl is preserved into the create-account link', async () => {
  await renderLogin('customer', '/cliente/login?returnUrl=%2Fcliente%2Fagendar%3Fhandoff%3DAbc-1')
  expect(screen.getByRole('link', { name: /criar conta/i }).getAttribute('href'))
    .toContain('returnUrl=')
})

test('customer login claims the handoff on mount when the returnUrl carries one', async () => {
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  await renderLogin('customer', '/cliente/login?returnUrl=%2Fcliente%2Fagendar%3Fhandoff%3DAbc-1')
  await waitFor(() => expect(apiClient.post).toHaveBeenCalledWith('/api/totem/booking-handoffs/claim', { handoffToken: 'Abc-1' }))
})

test('professional login never claims a handoff, even with a handoff-shaped returnUrl', async () => {
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  await renderLogin('professional', '/profissional/login?returnUrl=%2Fcliente%2Fagendar%3Fhandoff%3DAbc-1')
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  expect(apiClient.post).not.toHaveBeenCalledWith('/api/totem/booking-handoffs/claim', expect.anything())
})

test('admin login never claims a handoff, even with a handoff-shaped returnUrl', async () => {
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  await renderLogin('admin', '/login?returnUrl=%2Fcliente%2Fagendar%3Fhandoff%3DAbc-1')
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  expect(apiClient.post).not.toHaveBeenCalledWith('/api/totem/booking-handoffs/claim', expect.anything())
})

test('customer login does not claim when the returnUrl has no handoff', async () => {
  vi.mocked(apiClient.post).mockResolvedValue(undefined)
  await renderLogin('customer', '/cliente/login?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx')
  await waitFor(() => expect(apiClient.get).toHaveBeenCalled())
  expect(apiClient.post).not.toHaveBeenCalledWith('/api/totem/booking-handoffs/claim', expect.anything())
})

test('sober look: no decorative icons, textual show/hide password control', async () => {
  const { container } = await renderLogin('customer')
  // the only <svg> allowed anywhere is none — no envelope / lock / shield / eye / arrow icons
  expect(container.querySelectorAll('svg')).toHaveLength(0)
  const toggle = screen.getByRole('button', { name: /mostrar/i })
  fireEvent.click(toggle)
  expect(screen.getByRole('button', { name: /ocultar/i })).toBeInTheDocument()
  expect(screen.getByLabelText(/senha/i)).toHaveAttribute('type', 'text')
})
