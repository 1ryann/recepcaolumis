import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, expect, test, vi } from 'vitest'
import { customerApi, type ReservationDto } from '../../api/modules'
import { CustomerReservationDetail } from './CustomerReservationDetail'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  customerApi: { reservation: vi.fn(), issueCheckInToken: vi.fn() },
}))

vi.mock('qrcode', () => ({
  default: { toDataURL: vi.fn(() => Promise.resolve('data:image/png;base64,AAA')) },
}))

const approvedReservation: ReservationDto = {
  id: 'abc',
  roomId: 'room-1',
  roomName: 'Sala 1',
  professionalId: 'prof-1',
  professionalName: 'Ana Souza',
  originalReservationId: null,
  kind: 'NEW',
  status: 'APPROVED',
  startAt: '2026-09-15T13:00:00Z',
  endAt: '2026-09-15T14:00:00Z',
  requestedAt: '2026-09-10T09:00:00Z',
  decidedAt: '2026-09-10T10:00:00Z',
  rejectionReason: null,
  customerName: null,
  createdAt: '2026-09-10T09:00:00Z',
  updatedAt: '2026-09-10T10:00:00Z',
  concurrencyToken: 'v1',
}

function renderDetail() {
  return render(
    <MemoryRouter initialEntries={['/cliente/agendamentos/abc']}>
      <Routes>
        <Route path="/cliente/agendamentos/:id" element={<CustomerReservationDetail />} />
      </Routes>
    </MemoryRouter>,
  )
}

async function issueFromDetail() {
  renderDetail()
  fireEvent.click(await screen.findByRole('button', { name: /gerar qr code/i }))
  return screen.findByAltText('QR Code de check-in')
}

beforeEach(() => {
  vi.mocked(customerApi.reservation).mockResolvedValue(approvedReservation)
  vi.mocked(customerApi.issueCheckInToken).mockResolvedValue({ token: 'strong-token', manualCode: '004821', expiresAt: '2026-09-15T14:00:00Z' })
})

test('shows the 6-digit code next to the QR after "Gerar QR Code"', async () => {
  vi.mocked(customerApi.issueCheckInToken).mockResolvedValue({ token: 'strong-token', manualCode: '004821', expiresAt: '2026-09-15T14:00:00Z' })
  renderDetail()
  fireEvent.click(await screen.findByRole('button', { name: /gerar qr code/i }))
  expect(await screen.findByAltText('QR Code de check-in')).toBeInTheDocument()
  expect(screen.getByText('004821')).toBeInTheDocument()               // leading zero preserved
  expect(screen.getByText('Use este código no Totem.')).toBeInTheDocument()
})

test('the manual code is never written to web storage', async () => {
  const setItem = vi.spyOn(Storage.prototype, 'setItem')
  await issueFromDetail()
  expect(setItem).not.toHaveBeenCalledWith(expect.anything(), expect.stringContaining('004821'))
})
