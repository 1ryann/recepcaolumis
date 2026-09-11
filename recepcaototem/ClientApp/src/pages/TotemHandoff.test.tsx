import { act, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { totemApi } from '../api/modules'
import { TotemHandoff } from './TotemHandoff'

// `qrcode` is mocked so the QR effect resolves synchronously (no canvas in jsdom) and the
// test output stays pristine. Every timer is faked: polling (2 s), the countdown tick, and
// the 6 s auto-return are driven with `vi.advanceTimersByTimeAsync`. The router is real —
// a `/totem` and a `/totem/profissionais` sink route observe every redirect.
vi.mock('qrcode', () => ({
  default: { toDataURL: vi.fn().mockResolvedValue('data:image/png;base64,QR') },
}))
vi.mock('../api/modules', async (orig) => ({
  ...(await orig<typeof import('../api/modules')>()),
  totemApi: { pollHandoff: vi.fn(), cancelHandoff: vi.fn(), createHandoff: vi.fn() },
}))
vi.stubGlobal('matchMedia', (q: string) => ({
  matches: false, media: q, addEventListener: vi.fn(), removeEventListener: vi.fn(),
  addListener: vi.fn(), removeListener: vi.fn(), onchange: null, dispatchEvent: vi.fn(),
}))

beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-09-10T12:00:00Z'))
})
afterEach(() => {
  vi.runOnlyPendingTimers()
  vi.useRealTimers()
  vi.clearAllMocks()
})

const future = () => new Date(Date.now() + 5 * 60_000).toISOString()
const baseState = () => ({
  handoffId: 'h1',
  handoffToken: 'H',
  statusToken: 'S',
  professionalId: 'p1',
  professionalName: 'Dra. Ana',
  profession: 'Fisioterapia',
  expiresAt: future(),
})

const advance = (ms: number) => act(async () => { await vi.advanceTimersByTimeAsync(ms) })
const clickAndFlush = (el: HTMLElement) => act(async () => {
  fireEvent.click(el)
  await Promise.resolve()
})

function renderWithState(state: Record<string, unknown> | undefined) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: '/totem/handoff', state }]}>
      <Routes>
        <Route path="/totem/handoff" element={<TotemHandoff />} />
        <Route path="/totem" element={<div>totem home</div>} />
        <Route path="/totem/profissionais" element={<div>totem profissionais</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

test('with no navigation state it redirects to /totem', () => {
  renderWithState(undefined)
  expect(screen.getByText('totem home')).toBeInTheDocument()
  expect(totemApi.pollHandoff).not.toHaveBeenCalled()
})

test('pending screen shows the prompt, professional, QR image and the cancel button', async () => {
  vi.mocked(totemApi.pollHandoff).mockResolvedValue({ status: 'PENDING', expiresAt: future() })
  renderWithState(baseState())
  expect(screen.getByText('Continue no seu celular')).toBeInTheDocument()
  expect(screen.getByText('Dra. Ana')).toBeInTheDocument()
  expect(screen.getByText('Fisioterapia')).toBeInTheDocument()
  await advance(0)
  expect(
    screen.getByAltText('QR Code para continuar o agendamento no seu celular'),
  ).toBeInTheDocument()
  expect(
    screen.getByRole('button', { name: /escolher outro profissional/i }),
  ).toBeInTheDocument()
})

test('the countdown is announced via a coarse aria-live=polite region, not every second', async () => {
  vi.mocked(totemApi.pollHandoff).mockResolvedValue({ status: 'PENDING', expiresAt: future() })
  renderWithState(baseState())
  await advance(0)
  const live = screen.getByText(/Tempo restante aproximado/i)
  expect(live).toHaveAttribute('aria-live', 'polite')
  expect(live).toHaveTextContent('Tempo restante aproximado 05:00')
  // 29s of 1s ticks stay within the same 30s bucket: the announced text must not change,
  // even though the visual (aria-hidden) ticker below it updates every second.
  await advance(29_000)
  expect(live).toHaveTextContent('Tempo restante aproximado 05:00')
  // Crossing the 30s boundary is the only time the announcement changes.
  await advance(1_000)
  expect(live).toHaveTextContent('Tempo restante aproximado 04:30')
})

test('polls, then on COMPLETED shows the done screen and auto-returns to /totem after 6s', async () => {
  vi.mocked(totemApi.pollHandoff)
    .mockResolvedValueOnce({ status: 'PENDING', expiresAt: future() })
    .mockResolvedValue({
      status: 'COMPLETED',
      professionalName: 'Dra. Ana',
      startAt: '2026-09-10T14:30:00Z',
      roomName: 'Sala 1',
    })
  renderWithState(baseState())
  await advance(2000)
  await advance(2000)
  expect(screen.getByText(/agendamento concluído/i)).toBeInTheDocument()
  expect(screen.getByText(/retornando ao início/i)).toBeInTheDocument()
  await advance(6000)
  expect(screen.getByText('totem home')).toBeInTheDocument()
})

test('EXPIRED stops polling and shows the two recovery buttons', async () => {
  vi.mocked(totemApi.pollHandoff).mockResolvedValue({ status: 'EXPIRED' })
  renderWithState(baseState())
  await advance(2000)
  expect(screen.getByText(/este qr code expirou/i)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /gerar novo qr/i })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /escolher outro profissional/i })).toBeInTheDocument()
})

test('EXPIRED "Gerar novo QR" regenerates via createHandoff and returns to the pending screen', async () => {
  vi.mocked(totemApi.pollHandoff).mockResolvedValue({ status: 'EXPIRED' })
  vi.mocked(totemApi.createHandoff).mockResolvedValue({
    id: 'h2', handoffToken: 'H2', statusToken: 'S2', expiresAt: future(),
    professionalName: 'Dra. Ana', profession: 'Fisioterapia',
  })
  renderWithState(baseState())
  await advance(2000)
  await clickAndFlush(screen.getByRole('button', { name: /gerar novo qr/i }))
  expect(totemApi.createHandoff).toHaveBeenCalledWith('p1')
  expect(screen.getByText('Continue no seu celular')).toBeInTheDocument()
})

test('choose-another from the pending screen cancels the handoff and leaves via /totem/profissionais', async () => {
  vi.mocked(totemApi.pollHandoff).mockResolvedValue({ status: 'PENDING', expiresAt: future() })
  vi.mocked(totemApi.cancelHandoff).mockResolvedValue({ status: 'CANCELLED' })
  renderWithState(baseState())
  await clickAndFlush(screen.getByRole('button', { name: /escolher outro profissional/i }))
  expect(totemApi.cancelHandoff).toHaveBeenCalledWith('h1', 'S')
  expect(screen.getByText('totem profissionais')).toBeInTheDocument()
})

test('after 5 consecutive poll failures it shows Reconectando and a retry button', async () => {
  vi.mocked(totemApi.pollHandoff).mockRejectedValue(new Error('network'))
  renderWithState(baseState())
  for (let i = 0; i < 5; i++) await advance(2000)
  expect(screen.getByText(/reconectando/i)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /tentar novamente/i })).toBeInTheDocument()
})

test('never writes to localStorage/sessionStorage across a full cycle', async () => {
  const setItem = vi.spyOn(Storage.prototype, 'setItem')
  vi.mocked(totemApi.pollHandoff)
    .mockResolvedValueOnce({ status: 'PENDING', expiresAt: future() })
    .mockResolvedValue({
      status: 'COMPLETED',
      professionalName: 'Dra. Ana',
      startAt: '2026-09-10T14:30:00Z',
      roomName: null,
    })
  renderWithState(baseState())
  await advance(2000)
  await advance(2000)
  await advance(6000)
  expect(setItem).not.toHaveBeenCalled()
})
