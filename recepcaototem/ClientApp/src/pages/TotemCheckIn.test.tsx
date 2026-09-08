import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, expect, test, vi } from 'vitest'
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

const renderPage = () => render(<MemoryRouter><TotemCheckIn /></MemoryRouter>)

test('defaults to the scan segment; choosing manual reveals the code field and stops the camera', () => {
  renderPage()
  expect(screen.getByRole('tab', { name: /escanear qr/i })).toHaveAttribute('aria-selected', 'true')
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  expect(screen.getByLabelText(/código do qr code/i)).toBeInTheDocument()
  expect(scanner.stop).toHaveBeenCalled()
})

test('a denied camera falls back to the manual segment', async () => {
  scanner.state = 'denied'
  renderPage()
  await waitFor(() => expect(screen.getByRole('tab', { name: /digitar código/i })).toHaveAttribute('aria-selected', 'true'))
  expect(screen.getByLabelText(/código do qr code/i)).toBeInTheDocument()
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
  fireEvent.change(screen.getByLabelText(/código do qr code/i), { target: { value: 'MAN-1' } })
  fireEvent.click(screen.getByRole('button', { name: /validar agendamento/i }))
  expect(await screen.findByText('Dra. Ana')).toBeInTheDocument()
  expect(totemApi.confirmCheckIn).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: /confirmar chegada/i }))
  await waitFor(() => expect(totemApi.confirmCheckIn).toHaveBeenCalledWith('MAN-1'))
  expect(await screen.findByRole('dialog')).toHaveTextContent(/chegada registrada/i)
})

test('concluding the modal resets the totem to the scan segment', async () => {
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue(preview(true))
  renderPage()
  fireEvent.click(screen.getByRole('tab', { name: /digitar código/i }))
  fireEvent.change(screen.getByLabelText(/código do qr code/i), { target: { value: 'MAN-2' } })
  fireEvent.click(screen.getByRole('button', { name: /validar agendamento/i }))
  fireEvent.click(await screen.findByRole('button', { name: /confirmar chegada/i }))
  fireEvent.click(await screen.findByRole('button', { name: /concluir/i }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(screen.getByRole('tab', { name: /escanear qr/i })).toHaveAttribute('aria-selected', 'true')
  expect(screen.queryByText('Dra. Ana')).not.toBeInTheDocument()
})

test('leaving the page stops the camera', () => {
  const view = renderPage()
  view.unmount()
  expect(scanner.stop).toHaveBeenCalled()
})
