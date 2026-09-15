import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, test, vi } from 'vitest'
import { ApiError } from '../api/client'
import { totemRoomApi, type PublicRoomDetailDto, type RoomRentalInquiryResultDto } from '../api/modules'
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
  <MemoryRouter initialEntries={[`/totem/salas/${id}`]}>
    <Routes>
      <Route path="/totem/salas/:id" element={<TotemRoomDetail />} />
    </Routes>
  </MemoryRouter>,
)

const room: PublicRoomDetailDto = {
  id: 'r1',
  name: 'Sala Alfa',
  description: 'Ampla e bem iluminada.',
  availability: 'AVAILABLE_NOW',
  availableFrom: null,
  photoUrls: ['https://cdn.example/alfa-1.jpg', 'https://cdn.example/alfa-2.jpg'],
}
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

test('renders photoUrls in the exact order received with a working thumbnail selector', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  const main = await screen.findByAltText('Foto da sala Sala Alfa')
  expect(main).toHaveAttribute('src', room.photoUrls[0])
  const thumbs = screen.getAllByRole('button', { name: /ver foto/i })
  expect(thumbs).toHaveLength(2)
  fireEvent.click(thumbs[1])
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toHaveAttribute('src', room.photoUrls[1])
})

test('a main photo load failure swaps it for the fallback without losing the thumbnail strip', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  const main = await screen.findByAltText('Foto da sala Sala Alfa')
  fireEvent.error(main)
  expect(screen.getByTestId('totem-room-detail-fallback')).toBeInTheDocument()
  expect(screen.getAllByRole('button', { name: /ver foto/i })).toHaveLength(2)
})

test('a thumbnail photo load failure swaps just that thumbnail for a fallback', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  await screen.findByAltText('Foto da sala Sala Alfa')
  const thumbs = screen.getAllByRole('button', { name: /ver foto/i })
  fireEvent.error(thumbs[1].querySelector('img')!)
  expect(screen.getByTestId('totem-room-detail-thumb-fallback')).toBeInTheDocument()
  // The main photo (a different image) is unaffected by the thumbnail's own failure.
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toBeInTheDocument()
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

test('"Tenho interesse" opens the inquiry form with the three required fields and an optional note', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  fireEvent.click(await screen.findByRole('button', { name: 'Tenho interesse' }))
  expect(screen.getByLabelText('Nome')).toBeInTheDocument()
  expect(screen.getByLabelText('WhatsApp')).toBeInTheDocument()
  expect(screen.getByLabelText('Profissão/Empresa')).toBeInTheDocument()
  expect(screen.getByLabelText(/observação/i)).toBeInTheDocument()
})

test('required fields block submission client-side without ever calling the API', async () => {
  vi.mocked(totemRoomApi.detail).mockResolvedValue(room)
  renderAt()
  fireEvent.click(await screen.findByRole('button', { name: 'Tenho interesse' }))
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  expect(await screen.findByRole('alert')).toBeInTheDocument()
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

test('a successful submit sends exactly four fields and navigates with the exact nav state', async () => {
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
    fullName: 'Ana', whatsApp: '69993182032', professionOrCompany: 'Fisioterapeuta', note: null,
  })
})
