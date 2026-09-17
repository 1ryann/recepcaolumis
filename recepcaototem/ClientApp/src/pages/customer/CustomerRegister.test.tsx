import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { vi } from 'vitest'
import { customerApi } from '../../api/modules'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { CustomerRegister } from './CustomerRegister'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  customerApi: { register: vi.fn() },
}))

function LoginSink() {
  const location = useLocation()
  return <p data-testid="login-sink">{`search=${location.search}`}</p>
}

function renderAt(entry: string) {
  return render(<ThemeProvider><MemoryRouter initialEntries={[entry]}><Routes>
    <Route path="/cliente/cadastro" element={<CustomerRegister />} />
    <Route path="/cliente/login" element={<LoginSink />} />
  </Routes></MemoryRouter></ThemeProvider>)
}

function fillValidForm() {
  fireEvent.change(screen.getByLabelText('Nome completo'), { target: { value: 'Ana Souza' } })
  fireEvent.change(screen.getByLabelText('WhatsApp / telefone'), { target: { value: '69999999999' } })
  fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: 'ana@example.test' } })
  fireEvent.change(screen.getByLabelText(/^Senha/), { target: { value: 'Abcdef123456!' } })
  fireEvent.change(screen.getByLabelText(/^Confirmar senha/), { target: { value: 'Abcdef123456!' } })
}

test('propagates a safe returnUrl into the post-register /cliente/login navigation', async () => {
  vi.mocked(customerApi.register).mockResolvedValue({} as never)
  renderAt('/cliente/cadastro?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx')
  fillValidForm()
  fireEvent.click(screen.getByRole('button', { name: /criar minha conta/i }))
  const sink = await screen.findByTestId('login-sink')
  expect(sink.textContent).toBe('search=?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dx')
})

test('navigates to a plain /cliente/login when no returnUrl is present', async () => {
  vi.mocked(customerApi.register).mockResolvedValue({} as never)
  renderAt('/cliente/cadastro')
  fillValidForm()
  fireEvent.click(screen.getByRole('button', { name: /criar minha conta/i }))
  const sink = await screen.findByTestId('login-sink')
  expect(sink.textContent).toBe('search=')
})

test('drops an unsafe returnUrl and navigates to a plain /cliente/login', async () => {
  vi.mocked(customerApi.register).mockResolvedValue({} as never)
  renderAt('/cliente/cadastro?returnUrl=%2F%2Fevil.com')
  fillValidForm()
  fireEvent.click(screen.getByRole('button', { name: /criar minha conta/i }))
  const sink = await screen.findByTestId('login-sink')
  expect(sink.textContent).toBe('search=')
})
