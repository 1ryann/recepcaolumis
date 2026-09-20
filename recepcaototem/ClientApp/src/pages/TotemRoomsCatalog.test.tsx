import { aPublicRoomCard } from '../test/roomFixtures'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { totemRoomApi, type PublicRoomCardDto } from '../api/modules'
import { ThemeProvider } from '../theme/ThemeProvider'
import { TotemRoomsCatalog } from './TotemRoomsCatalog'

// `useNavigate` is spied so navigation targets can be asserted directly — in particular
// that a card click reaches `/totem/salas/{id}` and both the logo and the empty/error
// "Voltar" affordances reach `/totem`. Every other react-router export stays real.
const navigateSpy = vi.fn()
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => navigateSpy,
}))
vi.mock('../api/modules', async (orig) => ({
  ...(await orig<typeof import('../api/modules')>()),
  totemRoomApi: { list: vi.fn(), detail: vi.fn(), createInquiry: vi.fn() },
}))

beforeEach(() => {})
afterEach(() => vi.clearAllMocks())

const renderAt = () => render(
  <ThemeProvider><MemoryRouter initialEntries={['/totem/salas']}>
    <Routes>
      <Route path="/totem/salas" element={<TotemRoomsCatalog />} />
    </Routes>
  </MemoryRouter></ThemeProvider>,
)

const roomNow: PublicRoomCardDto = aPublicRoomCard({
  id: 'r-now', name: 'Sala Alfa', description: 'Ampla e bem iluminada.',
  coverPhotoUrl: 'https://cdn.example/alfa.jpg', monthlyRate: 3100, capacityMin: 4, capacityMax: 8,
  category: 'CONSULTORIO',
})
const roomSoonLate: PublicRoomCardDto = aPublicRoomCard({
  id: 'r-soon-late', name: 'Sala Gama', availability: 'AVAILABLE_SOON', availableFrom: '2026-11-20',
})
const roomSoonEarly: PublicRoomCardDto = aPublicRoomCard({
  id: 'r-soon-early', name: 'Sala Beta', availability: 'AVAILABLE_SOON', availableFrom: '2026-11-16',
})

test('loading shows the kiosk layout with skeleton cards', () => {
  vi.mocked(totemRoomApi.list).mockReturnValue(new Promise(() => {}))
  renderAt()
  // The visible "Alugar sala" banner was dropped — the cards say what the page is. The
  // heading itself stays, off-screen, so the document still has one.
  const heading = screen.getByRole('heading', { level: 1 })
  expect(heading).toHaveClass('sr-only')
  expect(screen.getAllByTestId('totem-rooms-skeleton-card').length).toBeGreaterThan(0)
})

test('groups rooms into Now (first) and Soon (sorted by availableFrom then name), formatting the date without new Date', async () => {
  vi.mocked(totemRoomApi.list).mockResolvedValue([roomSoonLate, roomNow, roomSoonEarly])
  renderAt()

  const nowHeading = await screen.findByRole('heading', { name: 'Disponíveis agora' })
  const soonHeading = screen.getByRole('heading', { name: 'Disponíveis em breve' })
  // Now section must appear before Soon section in document order.
  expect(nowHeading.compareDocumentPosition(soonHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()

  expect(screen.getByText('Próxima disponibilidade: em 16/11/2026')).toBeInTheDocument()
  expect(screen.getByText('Próxima disponibilidade: em 20/11/2026')).toBeInTheDocument()

  const soonNames = screen.getAllByText(/Sala (Beta|Gama)/).map((el) => el.textContent)
  expect(soonNames).toEqual(['Sala Beta', 'Sala Gama'])
})

test('only renders a section heading for groups that have at least one room', async () => {
  vi.mocked(totemRoomApi.list).mockResolvedValue([roomNow])
  renderAt()
  await screen.findByRole('heading', { name: 'Disponíveis agora' })
  expect(screen.queryByRole('heading', { name: 'Disponíveis em breve' })).not.toBeInTheDocument()
})

// This test used to forbid every price on the catalogue. That decision was reversed — the
// monthly price is now advertised on the card. What it guards instead is the rule that
// survived: a room nobody has priced shows no price at all, never "R$ 0,00".
test('a priced room advertises its monthly price and an unpriced one shows none', async () => {
  vi.mocked(totemRoomApi.list).mockResolvedValue([roomNow, roomSoonEarly])
  renderAt()
  await screen.findByRole('heading', { name: 'Disponíveis agora' })

  // roomNow carries monthlyRate 3100; roomSoonEarly carries nothing at all.
  expect(screen.getAllByText(/\/mês/)).toHaveLength(1)
  expect(screen.getByText(/3\.100,00\/mês/)).toBeInTheDocument()
  // Anchored on the currency symbol: a loose /0,00/ also matches "3.100,00".
  expect(screen.queryByText(/R\$\s*0,00/)).not.toBeInTheDocument()

  // The per-hour and per-day rates belong to the detail page, not to a card.
  expect(screen.queryByText(/por hora|diária|\/hora|\/dia\b/i)).not.toBeInTheDocument()
})

test('an undescribed room shows its name and no empty chips', async () => {
  vi.mocked(totemRoomApi.list).mockResolvedValue([roomSoonEarly])
  renderAt()
  await screen.findByText('Sala Beta')
  expect(document.querySelectorAll('.room-chip')).toHaveLength(0)
})

test('a fully described room shows capacity, category and price as chips', async () => {
  vi.mocked(totemRoomApi.list).mockResolvedValue([roomNow])
  renderAt()
  await screen.findByText('Sala Alfa')
  expect(screen.getByText('4p-8p')).toBeInTheDocument()
  expect(screen.getByText('Consultório')).toBeInTheDocument()
})

test('a room with no coverPhotoUrl shows a visual fallback and still shows name/description', async () => {
  vi.mocked(totemRoomApi.list).mockResolvedValue([roomSoonEarly])
  renderAt()
  await screen.findByText('Sala Beta')
  expect(screen.getByTestId('totem-rooms-card-fallback')).toBeInTheDocument()
  expect(screen.queryByRole('img', { name: /sala beta/i })).not.toBeInTheDocument()
})

test('an image load failure swaps the photo for the fallback', async () => {
  vi.mocked(totemRoomApi.list).mockResolvedValue([roomNow])
  renderAt()
  const img = await screen.findByRole('img', { name: /sala alfa/i })
  fireEvent.error(img)
  expect(screen.getByTestId('totem-rooms-card-fallback')).toBeInTheDocument()
})

test('clicking a room card navigates to /totem/salas/{id}', async () => {
  vi.mocked(totemRoomApi.list).mockResolvedValue([roomNow])
  renderAt()
  fireEvent.click(await screen.findByText('Sala Alfa'))
  expect(navigateSpy).toHaveBeenCalledWith('/totem/salas/r-now')
})

test('the LUMIS logo navigates back to /totem', async () => {
  vi.mocked(totemRoomApi.list).mockResolvedValue([roomNow])
  renderAt()
  await screen.findByText('Sala Alfa')
  fireEvent.click(screen.getByRole('button', { name: /voltar ao início/i }))
  expect(navigateSpy).toHaveBeenCalledWith('/totem')
})

test('an AbortError is silently ignored and never surfaces as an error', async () => {
  const abortError = new DOMException('aborted', 'AbortError')
  vi.mocked(totemRoomApi.list).mockRejectedValue(abortError)
  renderAt()
  await waitFor(() => expect(totemRoomApi.list).toHaveBeenCalled())
  expect(screen.queryByText(/não foi possível carregar/i)).not.toBeInTheDocument()
})

test('empty -> message + Tentar novamente + Voltar', async () => {
  vi.mocked(totemRoomApi.list).mockResolvedValue([])
  renderAt()
  expect(await screen.findByText(/nenhuma sala disponível/i)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /tentar novamente/i })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /^voltar$/i }))
  expect(navigateSpy).toHaveBeenCalledWith('/totem')
})

test('error -> message + Tentar novamente (retries and recovers)', async () => {
  vi.mocked(totemRoomApi.list).mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce([roomNow])
  renderAt()
  expect(await screen.findByText(/não foi possível carregar/i)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))
  await screen.findByText('Sala Alfa')
})
