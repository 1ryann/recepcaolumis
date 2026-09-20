import { aPublicRoomDetail } from '../test/roomFixtures'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, test, vi } from 'vitest'
import { ApiError } from '../api/client'
import { totemRoomApi, type PublicRoomDetailDto, type RoomRentalInquiryResultDto } from '../api/modules'
import { ThemeProvider } from '../theme/ThemeProvider'
import { TotemRoomDetail } from './TotemRoomDetail'

// `useNavigate` is spied so the post-submit navigation target and its exact nav-state
// payload can be asserted. `useParams` stays real via MemoryRouter/Routes with a
// `/totem/salas/:id` route, matching TotemRoomsCatalog.test.tsx's pattern.
const navigateSpy = vi.fn()
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => navigateSpy,
}))
vi.mock('../api/modules', async (orig) => ({
  ...(await orig<typeof import('../api/modules')>()),
  totemRoomApi: { list: vi.fn(), detail: vi.fn(), createInquiry: vi.fn() },
}))

afterEach(() => vi.clearAllMocks())

const renderAt = (id = 'r1') => render(
  <ThemeProvider><MemoryRouter initialEntries={[`/totem/salas/${id}`]}>
    <Routes>
      <Route path="/totem/salas/:id" element={<TotemRoomDetail />} />
    </Routes>
  </MemoryRouter></ThemeProvider>,
)

const room: PublicRoomDetailDto = aPublicRoomDetail({
  id: 'r1',
  name: 'Sala Alfa',
  description: 'Ampla e bem iluminada.',
  photoUrls: ['https://cdn.example/alfa-1.jpg', 'https://cdn.example/alfa-2.jpg'],
})
const roomSoon: PublicRoomDetailDto = {
  ...room,
  availability: 'AVAILABLE_SOON',
  availableFrom: '2026-11-20',
}
const inquiryResult: RoomRentalInquiryResultDto = {
  inquiryId: 'i1',
  whatsappUrl: 'https://wa.me/5569?text=ola',
  presentedAvailabilityLabel: 'Disponível agora',
}

function fillRequiredFields() {
  fireEvent.change(screen.getByLabelText('Nome'), { target: { value: 'Ana' } })
  fireEvent.change(screen.getByLabelText('WhatsApp'), { target: { value: '69993182032' } })
  fireEvent.change(screen.getByLabelText('Profissão/Empresa'), { target: { value: 'Fisioterapeuta' } })
  fireEvent.change(screen.getByLabelText('Data de início desejada'), { target: { value: '2026-11-10' } })
  fireEvent.change(screen.getByLabelText('Data de término desejada'), { target: { value: '2026-11-20' } })
}

test('loading shows a loading state before the detail resolves', () => {
  vi.mocked(totemRoomApi.detail).mockReturnValue(new Promise(() => {}))
  renderAt()
  expect(screen.getByTestId('totem-room-detail-loading')).toBeInTheDocument()
})

test('a 404 shows a not-found message instead of the generic error', async () => {
  vi.mocked(totemRoomApi.detail).mockRejectedValue(new ApiError(404, 'NOT_FOUND', 'Sala não encontrada.'))
  renderAt()
  expect(await screen.findByText(/não encontramos essa sala/i)).toBeInTheDocument()
  expect(screen.queryByText(/não foi possível carregar/i)).not.toBeInTheDocument()
})

test('a generic failure shows an error message with a working retry', async () => {
  vi.mocked(totemRoomApi.detail).mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(room)
  renderAt()
  expect(await screen.findByText(/não foi possível carregar/i)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))
  await screen.findByText('Sala Alfa')
})

// Detailed gallery behaviour (thumbnail switching, broken-image fallback, autoplay, the
// lightbox) lives in RoomPhotoGallery.test.tsx now that the gallery is its own component
// (Task 3, room-rental UX fixes) — this is just an integration smoke test confirming the
// page wires the room's photos into it correctly.
test('renders photoUrls in the exact order received with one thumbnail button per photo', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  const main = await screen.findByAltText('Foto da sala Sala Alfa')
  expect(main).toHaveAttribute('src', room.photoUrls[0])
  const thumbs = screen.getAllByRole('button', { name: /ver foto/i })
  expect(thumbs).toHaveLength(2)
})

test('never renders any tariff/price text', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  await screen.findByText('Sala Alfa')
  expect(screen.queryByText(/por hora|diária|R\$/i)).not.toBeInTheDocument()
})

test('shows "Disponível agora" for AVAILABLE_NOW', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  expect(await screen.findByText('Disponível agora')).toBeInTheDocument()
})

test('shows the Soon label formatted without new Date for AVAILABLE_SOON', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(roomSoon)
  renderAt()
  expect(await screen.findByText('Disponível em breve — a partir de 20/11/2026')).toBeInTheDocument()
})

test('"Tenho interesse" opens the inquiry modal with the required fields, the two desired dates, and an optional note', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  fireEvent.click(await screen.findByRole('button', { name: 'Tenho interesse' }))
  expect(screen.getByRole('dialog')).toBeInTheDocument()
  expect(screen.getByLabelText('Nome')).toBeInTheDocument()
  expect(screen.getByLabelText('WhatsApp')).toBeInTheDocument()
  expect(screen.getByLabelText('Profissão/Empresa')).toBeInTheDocument()
  expect(screen.getByLabelText('Data de início desejada')).toHaveAttribute('type', 'date')
  expect(screen.getByLabelText('Data de término desejada')).toHaveAttribute('type', 'date')
  expect(screen.getByLabelText(/observação/i)).toBeInTheDocument()
})

test('the modal closes without navigating when the X button is clicked', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  fireEvent.click(await screen.findByRole('button', { name: 'Tenho interesse' }))
  fireEvent.click(screen.getByRole('button', { name: 'Fechar' }))
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(navigateSpy).not.toHaveBeenCalled()
})

test('the modal closes without navigating when Escape is pressed', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  fireEvent.click(await screen.findByRole('button', { name: 'Tenho interesse' }))
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(navigateSpy).not.toHaveBeenCalled()
})

test('required fields block submission client-side without ever calling the API', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  fireEvent.click(await screen.findByRole('button', { name: 'Tenho interesse' }))
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  expect(await screen.findByRole('alert')).toBeInTheDocument()
  expect(totemRoomApi.createInquiry).not.toHaveBeenCalled()
})

test('an end date before the start date is blocked client-side without ever calling the API', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  fireEvent.click(await screen.findByRole('button', { name: 'Tenho interesse' }))
  fillRequiredFields()
  fireEvent.change(screen.getByLabelText('Data de início desejada'), { target: { value: '2026-11-20' } })
  fireEvent.change(screen.getByLabelText('Data de término desejada'), { target: { value: '2026-11-10' } })
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  expect(await screen.findByText('A data final não pode ser anterior à data inicial.')).toBeInTheDocument()
  expect(totemRoomApi.createInquiry).not.toHaveBeenCalled()
})

test('a 400 INVALID_ROOM_RENTAL_INQUIRY from the backend surfaces inline', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  vi.mocked(totemRoomApi.createInquiry).mockRejectedValue(
    new ApiError(400, 'INVALID_ROOM_RENTAL_INQUIRY', 'Os dados do interesse são inválidos.'),
  )
  renderAt()
  fireEvent.click(await screen.findByRole('button', { name: 'Tenho interesse' }))
  fillRequiredFields()
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  expect(await screen.findByText('Os dados do interesse são inválidos.')).toBeInTheDocument()
  expect(navigateSpy).not.toHaveBeenCalled()
})

test('submit is single-flight: a second click while pending does not call the API again', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  let resolveCreate: (value: RoomRentalInquiryResultDto) => void = () => {}
  vi.mocked(totemRoomApi.createInquiry).mockReturnValue(new Promise((resolve) => { resolveCreate = resolve }))
  renderAt()
  fireEvent.click(await screen.findByRole('button', { name: 'Tenho interesse' }))
  fillRequiredFields()
  const submitButton = screen.getByRole('button', { name: 'Enviar interesse' })
  fireEvent.click(submitButton)
  fireEvent.click(submitButton)
  expect(totemRoomApi.createInquiry).toHaveBeenCalledTimes(1)
  resolveCreate(inquiryResult)
  await waitFor(() => expect(navigateSpy).toHaveBeenCalled())
})

test('a successful submit sends exactly six fields and navigates with the exact nav state (no dates included)', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  vi.mocked(totemRoomApi.createInquiry).mockResolvedValue(inquiryResult)
  renderAt()
  fireEvent.click(await screen.findByRole('button', { name: 'Tenho interesse' }))
  fillRequiredFields()
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  await waitFor(() => expect(navigateSpy).toHaveBeenCalledWith('/totem/salas/r1/interesse', {
    state: {
      roomName: 'Sala Alfa',
      whatsappUrl: 'https://wa.me/5569?text=ola',
      presentedAvailabilityLabel: 'Disponível agora',
    },
  }))
  expect(totemRoomApi.createInquiry).toHaveBeenCalledWith('r1', {
    fullName: 'Ana',
    whatsApp: '69993182032',
    professionOrCompany: 'Fisioterapeuta',
    note: null,
    desiredStartDate: '2026-11-10',
    desiredEndDate: '2026-11-20',
  })
  // The modal closes on success too — it should not still be showing the form underneath
  // the page that navigated away.
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
})

// ── Surface, price and features ──────────────────────────────────────────────────────

const renderPublicAt = (id = 'r1') => render(
  <ThemeProvider><MemoryRouter initialEntries={[`/salas/${id}`]}>
    <Routes>
      <Route path="/salas/:id" element={<TotemRoomDetail />} />
    </Routes>
  </MemoryRouter></ThemeProvider>,
)

const describedRoom = aPublicRoomDetail({
  id: 'r1', name: 'Sala Premium', description: 'Espaço de alto padrão.',
  monthlyRate: 3100, hourlyRate: 45, dailyRate: 280,
  areaSquareMeters: 25, bathroomCount: 1, capacityMin: 4, capacityMax: 8,
  category: 'CONSULTORIO', amenities: ['CLIMATIZADA', 'MOBILIADA'],
  whatsappUrl: 'https://wa.me/5569999999999?text=Ol%C3%A1',
})

test('the monthly price leads and the per-use rates sit beneath it', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(describedRoom)
  renderPublicAt()
  await screen.findByText('Sala Premium')
  expect(screen.getByText(/3\.100,00/)).toBeInTheDocument()
  expect(screen.getByText('/mês')).toBeInTheDocument()
  expect(screen.getByText(/45,00\/hora · R\$\s?280,00\/dia/)).toBeInTheDocument()
})

test('the feature strip lists only what the room actually carries', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(describedRoom)
  renderPublicAt()
  await screen.findByText('Sala Premium')
  expect(screen.getByText('25m²')).toBeInTheDocument()
  expect(screen.getByText('1 Banheiro')).toBeInTheDocument()
  expect(screen.getByText('4 a 8 pessoas')).toBeInTheDocument()
  expect(screen.getByText('Climatizada')).toBeInTheDocument()
  expect(screen.getByText('Mobiliada')).toBeInTheDocument()
  // Not ticked, so not shown — the strip is not a checklist of absences.
  expect(screen.queryByText('Wi-Fi')).not.toBeInTheDocument()
})

// A room registered today, before anyone has priced or measured it, is an ordinary room.
test('an undescribed room renders without a price block or a feature strip', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(aPublicRoomDetail({ id: 'r1', name: 'Sala crua' }))
  renderPublicAt()
  await screen.findByText('Sala crua')
  expect(screen.queryByTestId('room-price')).not.toBeInTheDocument()
  expect(screen.queryByTestId('room-specs')).not.toBeInTheDocument()
  expect(screen.queryByText(/R\$/)).not.toBeInTheDocument()
  // Still fully usable: the inquiry is the point of the page.
  expect(screen.getByRole('button', { name: /tenho interesse/i })).toBeInTheDocument()
})

test('WhatsApp is offered in a browser and links to the URL the server built', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(describedRoom)
  renderPublicAt()
  await screen.findByText('Sala Premium')
  const link = screen.getByRole('link', { name: /falar pelo whatsapp/i })
  expect(link).toHaveAttribute('href', describedRoom.whatsappUrl)
  expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'))
})

// Tapping it on the kiosk would land the visitor on a WhatsApp they cannot use.
test('WhatsApp is not offered on the kiosk', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(describedRoom)
  renderAt()
  await screen.findByText('Sala Premium')
  expect(screen.queryByRole('link', { name: /falar pelo whatsapp/i })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: /tenho interesse/i })).toBeInTheDocument()
})

test('WhatsApp is not offered when the server configured no number', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue({ ...describedRoom, whatsappUrl: null })
  renderPublicAt()
  await screen.findByText('Sala Premium')
  expect(screen.queryByRole('link', { name: /falar pelo whatsapp/i })).not.toBeInTheDocument()
})

test('the kiosk clock shows on the kiosk and not in a browser', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(describedRoom)
  const kiosk = renderAt()
  await screen.findByText('Sala Premium')
  expect(kiosk.container.querySelector('.totem-clock')).not.toBeNull()
  kiosk.unmount()

  vi.mocked(totemRoomApi.detail).mockResolvedValue(describedRoom)
  const web = renderPublicAt()
  await screen.findByText('Sala Premium')
  expect(web.container.querySelector('.totem-clock')).toBeNull()
})
