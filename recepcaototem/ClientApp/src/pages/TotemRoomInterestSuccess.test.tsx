import { act, fireEvent, render, screen } from '@testing-library/react'
import QRCode from 'qrcode'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { TotemRoomInterestSuccess } from './TotemRoomInterestSuccess'

// `qrcode` is mocked the same way TotemHandoff.test.tsx mocks it, so the QR effect
// resolves synchronously without a real canvas. Every timer stays fake so
// `vi.getTimerCount()` can prove this screen sets none (no polling/countdown/auto-return,
// unlike TotemHandoff).
vi.mock('qrcode', () => ({
  default: { toDataURL: vi.fn().mockResolvedValue('data:image/png;base64,QR') },
}))

beforeEach(() => {
  vi.useFakeTimers()
})
afterEach(() => {
  vi.runOnlyPendingTimers()
  vi.useRealTimers()
  vi.clearAllMocks()
  vi.unstubAllGlobals()
})

const flush = () => act(async () => { await Promise.resolve() })

const validState = () => ({
  roomName: 'Sala Alfa',
  whatsappUrl: 'https://wa.me/5569?text=ola',
  presentedAvailabilityLabel: 'Disponível agora',
})

function renderWithState(state: Record<string, unknown> | undefined) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: '/totem/salas/r1/interesse', state }]}>
      <Routes>
        <Route path="/totem/salas/:id/interesse" element={<TotemRoomInterestSuccess />} />
        <Route path="/totem/salas" element={<div>catalogo</div>} />
        <Route path="/totem" element={<div>totem home</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

test('renders the approved heading, hint, room and availability, generates the exact QR and opens WhatsApp', async () => {
  const openSpy = vi.fn()
  vi.stubGlobal('open', openSpy)
  renderWithState(validState())
  expect(screen.getByRole('heading', { name: 'Continue no WhatsApp' })).toBeInTheDocument()
  expect(screen.getByText('Escaneie para continuar no seu celular')).toBeInTheDocument()
  expect(screen.getByText('Sala Alfa')).toBeInTheDocument()
  expect(screen.getByText('Disponível agora')).toBeInTheDocument()
  await flush()
  expect(QRCode.toDataURL).toHaveBeenCalledWith(
    'https://wa.me/5569?text=ola',
    expect.objectContaining({ width: 320 }),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Abrir WhatsApp' }))
  expect(openSpy).toHaveBeenCalledWith('https://wa.me/5569?text=ola', '_blank', 'noopener,noreferrer')
  expect(vi.getTimerCount()).toBe(0)
})

test('missing navigation state redirects to /totem/salas without calling QRCode', async () => {
  renderWithState(undefined)
  expect(screen.getByText('catalogo')).toBeInTheDocument()
  expect(QRCode.toDataURL).not.toHaveBeenCalled()
})

test('an invalid whatsappUrl host redirects instead of rendering the QR or wiring the button', async () => {
  renderWithState({ ...validState(), whatsappUrl: 'https://evil.example/wa.me' })
  expect(screen.getByText('catalogo')).toBeInTheDocument()
  expect(QRCode.toDataURL).not.toHaveBeenCalled()
})

test('an invalid whatsappUrl protocol (http) redirects instead of rendering the QR', async () => {
  renderWithState({ ...validState(), whatsappUrl: 'http://wa.me/5569' })
  expect(screen.getByText('catalogo')).toBeInTheDocument()
  expect(QRCode.toDataURL).not.toHaveBeenCalled()
})

test('a malformed whatsappUrl redirects instead of throwing', async () => {
  renderWithState({ ...validState(), whatsappUrl: 'not a url' })
  expect(screen.getByText('catalogo')).toBeInTheDocument()
})

test('QR generation failure still leaves "Abrir WhatsApp" usable', async () => {
  vi.mocked(QRCode.toDataURL).mockRejectedValueOnce(new Error('canvas unsupported'))
  const openSpy = vi.fn()
  vi.stubGlobal('open', openSpy)
  renderWithState(validState())
  await flush()
  fireEvent.click(screen.getByRole('button', { name: 'Abrir WhatsApp' }))
  expect(openSpy).toHaveBeenCalledWith('https://wa.me/5569?text=ola', '_blank', 'noopener,noreferrer')
})

test('"Voltar para salas" returns to the catalog', async () => {
  renderWithState(validState())
  fireEvent.click(screen.getByRole('button', { name: /voltar para salas/i }))
  expect(screen.getByText('catalogo')).toBeInTheDocument()
})

test('the secondary "Início" action returns to /totem', async () => {
  renderWithState(validState())
  fireEvent.click(screen.getByRole('button', { name: /^início$/i }))
  expect(screen.getByText('totem home')).toBeInTheDocument()
})

test('sets zero timers across the whole screen — no polling, countdown or auto-return', async () => {
  renderWithState(validState())
  await flush()
  expect(vi.getTimerCount()).toBe(0)
})

test('never writes inquiryId or anything else to localStorage/sessionStorage', async () => {
  const setItem = vi.spyOn(Storage.prototype, 'setItem')
  renderWithState(validState())
  await flush()
  expect(setItem).not.toHaveBeenCalled()
})
