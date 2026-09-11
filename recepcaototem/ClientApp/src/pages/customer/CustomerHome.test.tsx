import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { customerApi, type CustomerProfileDto, type ReservationDto } from '../../api/modules'
import { ApiError } from '../../api/client'
import { CustomerHome } from './CustomerHome'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  customerApi: { reservations: vi.fn(), issueCheckInToken: vi.fn() },
}))

vi.mock('qrcode', () => ({
  default: { toDataURL: vi.fn(() => Promise.resolve('data:image/png;base64,AAA')) },
}))

const profile: CustomerProfileDto = {
  id: 'c1', name: 'Marina Alves', phone: '11999990000', isActive: true, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
}

function makeReservation(overrides: Partial<ReservationDto>): ReservationDto {
  return {
    id: 'r1',
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
    createdAt: '2026-09-10T09:00:00Z',
    updatedAt: '2026-09-10T10:00:00Z',
    concurrencyToken: 'v1',
    ...overrides,
  }
}

const upcomingApproved = makeReservation({ id: 'upcoming-1', professionalName: 'Ana Souza', startAt: '2026-09-15T13:00:00Z', endAt: '2026-09-15T14:00:00Z' })
const pastReservation = makeReservation({ id: 'past-1', professionalName: 'Bruno Lima', status: 'APPROVED', startAt: '2026-09-01T09:00:00Z', endAt: '2026-09-01T10:00:00Z' })

function Wrapper() {
  return <Outlet context={{ profile, firstName: 'Marina' }} />
}

function renderHome() {
  return render(
    <MemoryRouter initialEntries={['/cliente']}>
      <Routes>
        <Route element={<Wrapper />}>
          <Route path="/cliente" element={<CustomerHome />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

let dateNowSpy: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  dateNowSpy = vi.spyOn(Date, 'now').mockReturnValue(new Date('2026-09-15T10:00:00Z').getTime())
  vi.mocked(customerApi.reservations).mockResolvedValue({ items: [upcomingApproved, pastReservation], page: 1, pageSize: 50, totalCount: 2 })
  vi.mocked(customerApi.issueCheckInToken).mockResolvedValue({ token: 'strong-token', manualCode: '004821', expiresAt: '2026-09-15T14:00:00Z' })
})

afterEach(() => {
  dateNowSpy.mockRestore()
})

test('shows the next approved upcoming appointment with a link to its details', async () => {
  renderHome()
  const heading = await screen.findByText('Próximo atendimento')
  const card = heading.closest('section') as HTMLElement
  expect(within(card).getByText('Ana Souza')).toBeInTheDocument()
  const link = within(card).getByRole('link', { name: /ver detalhes/i })
  expect(link).toHaveAttribute('href', '/cliente/agendamentos/upcoming-1')
})

test('shows an empty state instead of fake data when there is no next appointment', async () => {
  vi.mocked(customerApi.reservations).mockResolvedValue({ items: [pastReservation], page: 1, pageSize: 50, totalCount: 1 })
  renderHome()
  expect(await screen.findByText(/nenhum atendimento agendado/i)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /gerar qr code/i })).not.toBeInTheDocument()
})

test('generates a real QR code and manual code from the API, never a hardcoded one', async () => {
  renderHome()
  const button = await screen.findByRole('button', { name: /gerar qr code/i })
  fireEvent.click(button)
  expect(await screen.findByAltText(/QR/i)).toBeInTheDocument()
  expect(customerApi.issueCheckInToken).toHaveBeenCalledWith('upcoming-1')
  expect(screen.getByText('0 0 4 8 2 1')).toBeInTheDocument()
  expect(screen.queryByText('4 7 2 9 1 6')).not.toBeInTheDocument()
  expect(screen.queryByText('472916')).not.toBeInTheDocument()
})

test('shows the not-yet-eligible message from CHECK_IN_NOT_ELIGIBLE instead of a QR', async () => {
  vi.mocked(customerApi.issueCheckInToken).mockRejectedValue(new ApiError(409, 'CHECK_IN_NOT_ELIGIBLE', 'not eligible'))
  renderHome()
  const button = await screen.findByRole('button', { name: /gerar qr code/i })
  fireEvent.click(button)
  expect(await screen.findByText(/Disponível 1 h antes do horário/i)).toBeInTheDocument()
  expect(screen.queryByAltText(/QR/i)).not.toBeInTheDocument()
})

test('lists a past reservation under the history section', async () => {
  renderHome()
  expect(await screen.findByText('Histórico recente')).toBeInTheDocument()
  expect(await screen.findByText('Bruno Lima')).toBeInTheDocument()
})

test('links to /cliente/agendar for a new appointment', async () => {
  renderHome()
  const link = await screen.findByRole('link', { name: /novo agendamento/i })
  expect(link).toHaveAttribute('href', '/cliente/agendar')
})

test('does not render any notifications block', async () => {
  renderHome()
  await screen.findByText('Próximo atendimento')
  expect(screen.queryByText(/notifica/i)).not.toBeInTheDocument()
})
