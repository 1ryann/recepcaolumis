import { act, fireEvent, render, screen } from '@testing-library/react'
import QRCode from 'qrcode'
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { ThemeProvider } from '../theme/ThemeProvider'
import { IDLE_RETURN_MS, TotemRoomInterestSuccess } from './TotemRoomInterestSuccess'

// `qrcode` is mocked the same way TotemHandoff.test.tsx mocks it, so the QR effect
// resolves synchronously without a real canvas. Every timer stays fake so the idle
// auto-return can be driven exactly and `vi.getTimerCount()` can prove it is the ONLY
// timer (no polling/countdown, unlike TotemHandoff).
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

// A stand-in for the browser Back button, rendered on the destinations the success screen leaves to.
function BackProbe({ label }: { label: string }) {
  const navigate = useNavigate()
  return <div><span>{label}</span><button type="button" onClick={() => navigate(-1)}>history back</button></div>
}

// History starts as [room detail, success] — how the real flow arrives here — so a test can
// prove that leaving the success screen removes its entry (Back lands on the detail, not the QR).
function renderWithState(state: Record<string, unknown> | undefined) {
  return render(
    <ThemeProvider><MemoryRouter
      initialEntries={['/totem/salas/r1', { pathname: '/totem/salas/r1/interesse', state }]}
      initialIndex={1}
    >
      <Routes>
        <Route path="/totem/salas/:id/interesse" element={<TotemRoomInterestSuccess />} />
        <Route path="/totem/salas/:id" element={<div>detalhe da sala</div>} />
        <Route path="/totem/salas" element={<BackProbe label="catalogo" />} />
        <Route path="/totem" element={<BackProbe label="totem home" />} />
      </Routes>
    </MemoryRouter></ThemeProvider>,
  )
}

const heading = () => screen.queryByRole('heading', { name: 'Continue no WhatsApp' })
const advance = (ms: number) => act(() => { vi.advanceTimersByTime(ms) })

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

test('the idle window is 45 seconds and the screen says so, without a visual countdown', async () => {
  expect(IDLE_RETURN_MS).toBe(45_000)
  renderWithState(validState())
  expect(screen.getByText('Esta tela volta ao início após 45 segundos sem interação.')).toBeInTheDocument()
  await advance(10_000)
  // Static text: nothing on screen counts down.
  expect(screen.getByText('Esta tela volta ao início após 45 segundos sem interação.')).toBeInTheDocument()
})

test('returns to /totem after 45 s without interaction, not a moment earlier', async () => {
  renderWithState(validState())
  await flush()
  await advance(IDLE_RETURN_MS - 1)
  expect(heading()).toBeInTheDocument()
  await advance(1)
  expect(heading()).not.toBeInTheDocument()
  expect(screen.getByText('totem home')).toBeInTheDocument()
})

test.each([
  ['a touch/click (pointerdown)', () => fireEvent.pointerDown(document)],
  ['a key press', () => fireEvent.keyDown(document, { key: 'Tab' })],
  ['a touchstart', () => fireEvent.touchStart(document)],
])('%s restarts the idle timer instead of cutting the visitor off', async (_label, interact) => {
  renderWithState(validState())
  await flush()
  await advance(30_000)
  interact()
  await advance(IDLE_RETURN_MS - 1)
  expect(heading()).toBeInTheDocument()
  await advance(1)
  expect(screen.getByText('totem home')).toBeInTheDocument()
})

test('"Abrir WhatsApp" counts as interaction and restarts the idle timer', async () => {
  vi.stubGlobal('open', vi.fn())
  renderWithState(validState())
  await flush()
  await advance(40_000)
  fireEvent.click(screen.getByRole('button', { name: 'Abrir WhatsApp' }))
  await advance(IDLE_RETURN_MS - 1)
  expect(heading()).toBeInTheDocument()
  await advance(1)
  expect(screen.getByText('totem home')).toBeInTheDocument()
})

test('the auto-return replaces the success entry, so Back cannot bring the QR and visitor data back', async () => {
  renderWithState(validState())
  await flush()
  await advance(IDLE_RETURN_MS)
  expect(screen.getByText('totem home')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'history back' }))
  expect(screen.getByText('detalhe da sala')).toBeInTheDocument()
  expect(heading()).not.toBeInTheDocument()
  expect(screen.queryByAltText('QR Code para continuar no WhatsApp')).not.toBeInTheDocument()
})

test.each([
  ['Início', /^início$/i, 'totem home'],
  ['Voltar para salas', /voltar para salas/i, 'catalogo'],
  ['logo (Voltar ao início)', /voltar ao início/i, 'totem home'],
])('"%s" also replaces the success entry', async (_label, name, destination) => {
  renderWithState(validState())
  fireEvent.click(screen.getByRole('button', { name }))
  expect(screen.getByText(destination)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'history back' }))
  expect(screen.getByText('detalhe da sala')).toBeInTheDocument()
})

test('uses exactly one timer (the idle return), makes no network call, and clears it on leave', async () => {
  const fetchSpy = vi.fn()
  vi.stubGlobal('fetch', fetchSpy)
  renderWithState(validState())
  await flush()
  expect(vi.getTimerCount()).toBe(1)
  await advance(IDLE_RETURN_MS * 3)
  expect(fetchSpy).not.toHaveBeenCalled()
  // After leaving, the success screen's timer is gone — nothing fires again later.
  expect(screen.getByText('totem home')).toBeInTheDocument()
  expect(vi.getTimerCount()).toBe(0)
})

test('never writes inquiryId or anything else to localStorage/sessionStorage', async () => {
  const setItem = vi.spyOn(Storage.prototype, 'setItem')
  renderWithState(validState())
  await flush()
  expect(setItem).not.toHaveBeenCalled()
})
