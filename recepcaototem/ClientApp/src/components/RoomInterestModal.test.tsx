import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { ApiError } from '../api/client'
import { totemRoomApi, type RoomRentalInquiryResultDto } from '../api/modules'
import { RoomInterestModal } from './RoomInterestModal'

vi.mock('../api/modules', async (orig) => ({
  ...(await orig<typeof import('../api/modules')>()),
  totemRoomApi: { list: vi.fn(), detail: vi.fn(), createInquiry: vi.fn() },
}))

afterEach(() => vi.clearAllMocks())

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

function renderModal(overrides: Partial<Parameters<typeof RoomInterestModal>[0]> = {}) {
  const onClose = vi.fn()
  const onSuccess = vi.fn()
  const utils = render(
    <RoomInterestModal
      open
      onClose={onClose}
      roomId="r1"
      roomName="Sala Alfa"
      onSuccess={onSuccess}
      {...overrides}
    />,
  )
  return { ...utils, onClose, onSuccess }
}

test('does not render when closed', () => {
  renderModal({ open: false })
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
})

test('renders the three text fields, the two date fields, and an optional note when open', () => {
  renderModal()
  expect(screen.getByLabelText('Nome')).toBeInTheDocument()
  expect(screen.getByLabelText('WhatsApp')).toBeInTheDocument()
  expect(screen.getByLabelText('Profissão/Empresa')).toBeInTheDocument()
  expect(screen.getByLabelText('Data de início desejada')).toHaveAttribute('type', 'date')
  expect(screen.getByLabelText('Data de término desejada')).toHaveAttribute('type', 'date')
  expect(screen.getByLabelText(/observação/i)).toBeInTheDocument()
})

test('clicking the X button closes the modal', () => {
  const { onClose } = renderModal()
  fireEvent.click(screen.getByRole('button', { name: 'Fechar' }))
  expect(onClose).toHaveBeenCalledTimes(1)
})

test('clicking the backdrop closes the modal', () => {
  const { onClose } = renderModal()
  fireEvent.mouseDown(screen.getByRole('dialog').parentElement as HTMLElement)
  expect(onClose).toHaveBeenCalledTimes(1)
})

test('pressing Escape closes the modal', () => {
  const { onClose } = renderModal()
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(onClose).toHaveBeenCalledTimes(1)
})

test('clicking Cancelar closes the modal', () => {
  const { onClose } = renderModal()
  fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))
  expect(onClose).toHaveBeenCalledTimes(1)
})

test('required fields block submission client-side without ever calling the API', () => {
  renderModal()
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  expect(screen.getByRole('alert')).toHaveTextContent(
    'Preencha nome, WhatsApp, profissão/empresa e as datas desejadas.',
  )
  expect(totemRoomApi.createInquiry).not.toHaveBeenCalled()
})

test('missing only the desired dates blocks submission with the same required-fields message', () => {
  renderModal()
  fireEvent.change(screen.getByLabelText('Nome'), { target: { value: 'Ana' } })
  fireEvent.change(screen.getByLabelText('WhatsApp'), { target: { value: '69993182032' } })
  fireEvent.change(screen.getByLabelText('Profissão/Empresa'), { target: { value: 'Fisioterapeuta' } })
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  expect(screen.getByRole('alert')).toHaveTextContent(
    'Preencha nome, WhatsApp, profissão/empresa e as datas desejadas.',
  )
  expect(totemRoomApi.createInquiry).not.toHaveBeenCalled()
})

test('an end date before the start date is blocked with a specific message and no API call', () => {
  renderModal()
  fillRequiredFields()
  fireEvent.change(screen.getByLabelText('Data de início desejada'), { target: { value: '2026-11-20' } })
  fireEvent.change(screen.getByLabelText('Data de término desejada'), { target: { value: '2026-11-10' } })
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  expect(screen.getByRole('alert')).toHaveTextContent('A data final não pode ser anterior à data inicial.')
  expect(totemRoomApi.createInquiry).not.toHaveBeenCalled()
})

test('an end date equal to the start date is accepted (same-day rental)', async () => {
  vi.mocked(totemRoomApi.createInquiry).mockResolvedValue(inquiryResult)
  const { onSuccess } = renderModal()
  fillRequiredFields()
  fireEvent.change(screen.getByLabelText('Data de início desejada'), { target: { value: '2026-11-10' } })
  fireEvent.change(screen.getByLabelText('Data de término desejada'), { target: { value: '2026-11-10' } })
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  await waitFor(() => expect(onSuccess).toHaveBeenCalled())
})

test('a 400 INVALID_ROOM_RENTAL_INQUIRY from the backend surfaces inline', async () => {
  vi.mocked(totemRoomApi.createInquiry).mockRejectedValue(
    new ApiError(400, 'INVALID_ROOM_RENTAL_INQUIRY', 'Os dados do interesse são inválidos.'),
  )
  const { onSuccess } = renderModal()
  fillRequiredFields()
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  expect(await screen.findByText('Os dados do interesse são inválidos.')).toBeInTheDocument()
  expect(onSuccess).not.toHaveBeenCalled()
})

test('submit is single-flight: a second click while pending does not call the API again', async () => {
  let resolveCreate: (value: RoomRentalInquiryResultDto) => void = () => {}
  vi.mocked(totemRoomApi.createInquiry).mockReturnValue(new Promise((resolve) => { resolveCreate = resolve }))
  const { onSuccess } = renderModal()
  fillRequiredFields()
  const submitButton = screen.getByRole('button', { name: 'Enviar interesse' })
  fireEvent.click(submitButton)
  fireEvent.click(submitButton)
  expect(totemRoomApi.createInquiry).toHaveBeenCalledTimes(1)
  resolveCreate(inquiryResult)
  await waitFor(() => expect(onSuccess).toHaveBeenCalled())
})

test('a successful submit sends exactly six fields to the backend in YYYY-MM-DD format', async () => {
  vi.mocked(totemRoomApi.createInquiry).mockResolvedValue(inquiryResult)
  renderModal()
  fillRequiredFields()
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  await waitFor(() => expect(totemRoomApi.createInquiry).toHaveBeenCalledWith('r1', {
    fullName: 'Ana',
    whatsApp: '69993182032',
    professionOrCompany: 'Fisioterapeuta',
    note: null,
    desiredStartDate: '2026-11-10',
    desiredEndDate: '2026-11-20',
  }))
})

test('a successful submit calls onSuccess with exactly the roomName/whatsappUrl/presentedAvailabilityLabel shape', async () => {
  vi.mocked(totemRoomApi.createInquiry).mockResolvedValue(inquiryResult)
  const { onSuccess } = renderModal()
  fillRequiredFields()
  fireEvent.click(screen.getByRole('button', { name: 'Enviar interesse' }))
  await waitFor(() => expect(onSuccess).toHaveBeenCalledWith({
    roomName: 'Sala Alfa',
    whatsappUrl: 'https://wa.me/5569?text=ola',
    presentedAvailabilityLabel: 'Disponível agora',
  }))
})

test('reopening the modal after a previous fill starts from a clean, empty form', () => {
  const { rerender } = renderModal()
  fillRequiredFields()
  rerender(
    <RoomInterestModal open={false} onClose={vi.fn()} roomId="r1" roomName="Sala Alfa" onSuccess={vi.fn()} />,
  )
  rerender(
    <RoomInterestModal open onClose={vi.fn()} roomId="r1" roomName="Sala Alfa" onSuccess={vi.fn()} />,
  )
  expect(screen.getByLabelText('Nome')).toHaveValue('')
  expect(screen.getByLabelText('Data de início desejada')).toHaveValue('')
})
