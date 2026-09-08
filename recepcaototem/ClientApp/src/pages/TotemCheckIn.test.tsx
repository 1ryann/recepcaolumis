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

beforeEach(() => {
  vi.clearAllMocks()
  scanner.state = 'idle'
  scanner.start = vi.fn()
  scanner.stop = vi.fn()
  vi.mocked(useQrScanner).mockImplementation((callback: (raw: string) => void) => {
    decode = callback
    return scanner as unknown as ReturnType<typeof useQrScanner>
  })
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
  vi.mocked(totemApi.resolveCheckIn).mockResolvedValue({
    professional: 'Dra. Ana', room: 'Sala 2', startAt: '2026-09-15T13:00:00Z', endAt: '2026-09-15T14:00:00Z', eligible: true,
  })
  renderPage()
  decode('https://lumis.app/totem/check-in?token=ABC-9')
  await waitFor(() => expect(totemApi.resolveCheckIn).toHaveBeenCalledWith('ABC-9'))
  expect(await screen.findByText('Dra. Ana')).toBeInTheDocument()
})

test('leaving the page stops the camera', () => {
  const view = renderPage()
  view.unmount()
  expect(scanner.stop).toHaveBeenCalled()
})
