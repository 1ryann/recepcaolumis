import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { ApiError } from '../api/client'
import { totemApi } from '../api/modules'
import { useQrScanner } from '../features/totem/useQrScanner'
import { TotemCheckIn } from './TotemCheckIn'

vi.mock('../features/totem/useQrScanner')
vi.mock('../api/modules', async (original) => ({
  ...await original<typeof import('../api/modules')>(),
  totemApi: { resolveCheckIn: vi.fn(), confirmCheckIn: vi.fn() },
}))

let decode: (raw: string) => void = () => {}
const scanner = { videoRef: { current: null }, state: 'idle', start: vi.fn(), stop: vi.fn() }

const preview = (eligible: boolean) => ({
  professional: 'Dra. Ana', room: 'Sala 2', startAt: '2026-09-15T13:00:00Z', endAt: '2026-09-15T14:00:00Z', eligible,
})

beforeEach(() => {
  vi.clearAllMocks()
  scanner.state = 'idle'
  scanner.start = vi.fn()
  scanner.stop = vi.fn()
  vi.mocked(useQrScanner).mockImplementation((callback: (raw: string) => void) => {
    decode = callback
    return scanner as unknown as ReturnType<typeof useQrScanner>
  })
  vi.mocked(totemApi.confirmCheckIn).mockResolvedValue({ visitId: 'v1', status: 'WAITING' })
})

afterEach(() => vi.useRealTimers())

const renderPage = () => render(<MemoryRouter><TotemCheckIn /></MemoryRouter>)

// Like renderPage() but also mounts a /totem sink, so navigation away from the
// check-in route can be asserted by the presence of "ENTRY".
const renderWithTotem = () => render(
  <MemoryRouter initialEntries={['/totem/check-in']}>
    <Routes>
      <Route path="/totem/check-in" element={<TotemCheckIn />} />
      <Route path="/totem" element={<div>ENTRY</div>} />
    </Routes>
  </MemoryRouter>,
)

test('renders the trimmed kiosk shell: brand and a live clock', () => {
  renderPage()
  expect(screen.getByRole('img', { name: 'LUMIS' })).toBeInTheDocument()
  expect(screen.getByText(/^\d{2}:\d{2}$/)).toBeInTheDocument()
  expect(screen.queryByText(/faça seu check-in de forma simples e rápida/i)).not.toBeInTheDocument()
})

test('the discrete Voltar control returns to /totem', async () => {
  renderWithTotem()
  fireEvent.click(screen.getByRole('button', { name: /voltar/i }))
  expect(await screen.findByText('ENTRY')).toBeInTheDocument()
})

test('kiosk chrome: discrete topbar with Voltar / LUMIS / clock, centred stage', () => {
  renderPage()
  expect(screen.getByRole('button', { name: /voltar/i })).toBeInTheDocument()
  expect(screen.getByRole('img', { name: 'LUMIS' })).toBeInTheDocument()
  expect(screen.getByText(/^\d{2}:\d{2}$/)).toBeInTheDocument()
  expect(screen.getByText('CHECK-IN')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /confirme sua chegada/i })).toBeInTheDocument()
})

test('code tab uses the 6-box control and still validates leading zeros', async () => {
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(false))
  const { container } = renderPage()
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  expect(container.querySelectorAll('.totem-code-cell')).toHaveLength(6)
  const input = screen.getByLabelText(/código de 6 dígitos/i)
  fireEvent.change(input, { target: { value: '004729' } })
  fireEvent.click(screen.getByRole('button', { name: /confirmar|validar/i }))
  await waitFor(() => expect(totemApi.resolveCheckIn).toHaveBeenCalledWith('004729'))
})

test('each check-in option is a large target that explains what it does', () => {
  renderPage()
  expect(screen.getByText('Use a câmera para ler seu código')).toBeInTheDocument()
  expect(screen.getByText('Digite o código de 6 dígitos da sua reserva')).toBeInTheDocument()
})

test('after a successful check-in the auto-return lands on /totem', async () => {
  vi.useFakeTimers()
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(true))
  renderWithTotem()
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  fireEvent.change(screen.getByLabelText(/código de 6 dígitos/i), { target: { value: '004821' } })
  fireEvent.click(screen.getByRole('button', { name: /validar agendamento/i }))
  await act(async () => { await vi.advanceTimersByTimeAsync(0) })
  fireEvent.click(screen.getByRole('button', { name: /confirmar chegada/i }))
  await act(async () => { await vi.advanceTimersByTimeAsync(0) })
  expect(screen.getByRole('dialog')).toHaveTextContent(/chegada registrada/i)
  await act(async () => { await vi.advanceTimersByTimeAsync(12_000) })
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(screen.getByText('ENTRY')).toBeInTheDocument()
})

test('after a successful check-in the kiosk returns to /totem (auto + Concluir)', async () => {
  vi.useFakeTimers()
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(true))
  renderWithTotem()
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  fireEvent.change(screen.getByLabelText(/código de 6 dígitos/i), { target: { value: '004821' } })
  fireEvent.click(screen.getByRole('button', { name: /validar agendamento/i }))
  await act(async () => { await vi.advanceTimersByTimeAsync(0) })
  fireEvent.click(screen.getByRole('button', { name: /confirmar chegada/i }))
  await act(async () => { await vi.advanceTimersByTimeAsync(0) })
  expect(screen.getByRole('dialog')).toBeInTheDocument()
  await act(async () => { await vi.advanceTimersByTimeAsync(12_000) })
  expect(screen.getByText('ENTRY')).toBeInTheDocument()   // auto-return landed on /totem
})

test('defaults to the scan segment; choosing manual reveals the code field and stops the camera', () => {
  renderPage()
  expect(screen.getByRole('tab', { name: /escanear qr/i })).toHaveAttribute('aria-selected', 'true')
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  expect(screen.getByLabelText(/código de 6 dígitos/i)).toBeInTheDocument()
  expect(scanner.stop).toHaveBeenCalled()
})

test('a denied camera falls back to the manual segment', async () => {
  scanner.state = 'denied'
  renderPage()
  await waitFor(() => expect(screen.getByRole('tab', { name: /digitar código/i })).toHaveAttribute('aria-selected', 'true'))
  expect(screen.getByLabelText(/código de 6 dígitos/i)).toBeInTheDocument()
})

test('a decoded URL is normalized and resolves the appointment', async () => {
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(false))
  renderPage()
  decode('https://lumis.app/totem/check-in?token=ABC-9')
  await waitFor(() => expect(totemApi.resolveCheckIn).toHaveBeenCalledWith('ABC-9'))
  expect(await screen.findByText('Dra. Ana')).toBeInTheDocument()
})

test('an eligible QR scan confirms arrival automatically and opens the modal', async () => {
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(true))
  renderPage()
  decode('ABC-1')
  await waitFor(() => expect(totemApi.confirmCheckIn).toHaveBeenCalledWith('ABC-1'))
  expect(await screen.findByRole('dialog')).toHaveTextContent(/chegada registrada/i)
})

test('a not-yet-eligible QR scan does not auto-confirm and keeps the manual button', async () => {
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(false))
  renderPage()
  decode('ABC-2')
  expect(await screen.findByText('Dra. Ana')).toBeInTheDocument()
  expect(totemApi.confirmCheckIn).not.toHaveBeenCalled()
  expect(screen.getByRole('button', { name: /confirmar chegada/i })).toBeInTheDocument()
})

test('manual entry still needs the explicit confirm click, then opens the modal', async () => {
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(true))
  renderPage()
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  fireEvent.change(screen.getByLabelText(/código de 6 dígitos/i), { target: { value: '004821' } })
  fireEvent.click(screen.getByRole('button', { name: /validar agendamento/i }))
  expect(await screen.findByText('Dra. Ana')).toBeInTheDocument()
  expect(totemApi.confirmCheckIn).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: /confirmar chegada/i }))
  await waitFor(() => expect(totemApi.confirmCheckIn).toHaveBeenCalledWith('004821'))
  expect(await screen.findByRole('dialog')).toHaveTextContent(/chegada registrada/i)
})

test('concluding the modal returns the kiosk to /totem', async () => {
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(true))
  renderWithTotem()
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  fireEvent.change(screen.getByLabelText(/código de 6 dígitos/i), { target: { value: '004821' } })
  fireEvent.click(screen.getByRole('button', { name: /validar agendamento/i }))
  fireEvent.click(await screen.findByRole('button', { name: /confirmar chegada/i }))
  fireEvent.click(await screen.findByRole('button', { name: /concluir/i }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(await screen.findByText('ENTRY')).toBeInTheDocument()
})

test('leaving the page stops the camera', () => {
  const view = renderPage()
  view.unmount()
  expect(scanner.stop).toHaveBeenCalled()
})

const openManual = () => {
  renderPage()
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  return screen.getByLabelText(/código de 6 dígitos/i)
}

test('the manual segment offers a 6-digit numeric input', () => {
  const input = openManual()
  expect(input).toHaveAttribute('inputmode', 'numeric')
})

test('the manual input ignores letters and keeps only the digits', () => {
  const input = openManual()
  fireEvent.change(input, { target: { value: 'a1b2c3' } })
  expect(input).toHaveValue('123')
})

test('pasting a full code keeps its leading zeros', () => {
  const input = openManual()
  fireEvent.change(input, { target: { value: '004821' } })
  expect(input).toHaveValue('004821')
})

test('"Validar agendamento" is disabled below 6 digits and enabled at 6', () => {
  const input = openManual()
  fireEvent.change(input, { target: { value: '00482' } })
  expect(screen.getByRole('button', { name: /validar agendamento/i })).toBeDisabled()
  fireEvent.change(input, { target: { value: '004821' } })
  expect(screen.getByRole('button', { name: /validar agendamento/i })).not.toBeDisabled()
})

test('submitting six digits resolves the check-in by code', async () => {
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(false))
  const input = openManual()
  fireEvent.change(input, { target: { value: '004821' } })
  fireEvent.submit(input.closest('form') as HTMLFormElement)
  await waitFor(() => expect(totemApi.resolveCheckIn).toHaveBeenCalledWith('004821'))
})

test('a rejected manual code shows the generic validation message', async () => {
  vi.mocked(totemApi.resolveCheckIn).mockRejectedValue(new ApiError(400, 'INVALID_CHECK_IN', ''))
  const input = openManual()
  fireEvent.change(input, { target: { value: '004821' } })
  fireEvent.submit(input.closest('form') as HTMLFormElement)
  expect(await screen.findByText('Não foi possível validar este código.')).toBeInTheDocument()
})
