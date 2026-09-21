import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import { ApiError } from '../../api/client'
import { customerApi, whatsAppOptInApi, type CustomerProfessionalDto } from '../../api/modules'
import { CustomerBooking } from './CustomerBooking'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  customerApi: { professionals: vi.fn(), availability: vi.fn(), createReservation: vi.fn() },
  whatsAppOptInApi: { customer: vi.fn() },
}))

const people: CustomerProfessionalDto[] = [
  { id: 'p1', name: 'Ana Souza', profession: 'Fisioterapia', description: null },
  { id: 'p2', name: 'Bruno Lima', profession: 'Psicologia', description: null },
]

function renderAt(entry: string) {
  return render(<MemoryRouter initialEntries={[entry]}><Routes>
    <Route path="/cliente/agendar" element={<CustomerBooking />} />
  </Routes></MemoryRouter>)
}

beforeEach(() => {
  vi.mocked(customerApi.professionals).mockResolvedValue(people)
  vi.mocked(customerApi.availability).mockResolvedValue([])
  vi.mocked(whatsAppOptInApi.customer).mockResolvedValue({ status: 'GRANTED', changedAt: '2026-09-19T12:00:00Z', source: 'CUSTOMER_PORTAL', textVersion: 'whatsapp-operacional-v1' })
})

test('preselects the professional from ?professionalId when it exists', async () => {
  renderAt('/cliente/agendar?professionalId=p2')
  const select = await screen.findByLabelText(/profissional/i)
  expect(select).toHaveValue('p2')
})

test('falls back to the first professional when ?professionalId is unknown', async () => {
  renderAt('/cliente/agendar?professionalId=zzz')
  const select = await screen.findByLabelText(/profissional/i)
  expect(select).toHaveValue('p1')
})

test('falls back to the first professional when no ?professionalId is present', async () => {
  renderAt('/cliente/agendar')
  const select = await screen.findByLabelText(/profissional/i)
  expect(select).toHaveValue('p1')
})

test('keeps the professional select editable', async () => {
  renderAt('/cliente/agendar')
  const select = await screen.findByLabelText(/profissional/i)
  expect(select).toHaveValue('p1')
  fireEvent.change(select, { target: { value: 'p2' } })
  expect(select).toHaveValue('p2')
})

test('offers the unticked WhatsApp opt-in only while the customer has not opted in, and sends it when ticked', async () => {
  vi.mocked(whatsAppOptInApi.customer).mockResolvedValue({ status: 'NOT_RECORDED', changedAt: null, source: null, textVersion: null })
  vi.mocked(customerApi.availability).mockResolvedValue([{ startAt: '2026-09-21T14:00:00Z', endAt: '2026-09-21T15:00:00Z' } as never])
  vi.mocked(customerApi.createReservation).mockResolvedValue({ id: 'r1' } as never)
  renderAt('/cliente/agendar')

  const box = await screen.findByRole('checkbox')
  expect(box).not.toBeChecked()
  fireEvent.click(box)
  fireEvent.click(await screen.findByRole('button', { name: /até/ }))
  fireEvent.click(screen.getByRole('button', { name: /confirmar agendamento/i }))

  await vi.waitFor(() => expect(customerApi.createReservation).toHaveBeenCalled())
  expect(vi.mocked(customerApi.createReservation).mock.calls[0][0]).toMatchObject({ whatsAppOptIn: true })
})

test('does not ask again when the customer already opted in', async () => {
  renderAt('/cliente/agendar')
  await screen.findByLabelText(/profissional/i)
  await vi.waitFor(() => expect(whatsAppOptInApi.customer).toHaveBeenCalled())
  expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
})

// A slot that starts while the page sits open is refused as SLOT_IN_THE_PAST. It is a 409 like a slot someone else
// just took, but "acabou de ser ocupado" would be false: nobody took it, it went by. The server's own message says so.
test('a slot that has already started is explained as such, not as taken by someone else', async () => {
  vi.mocked(customerApi.availability).mockResolvedValue([{ startAt: '2026-09-21T14:00:00Z', endAt: '2026-09-21T15:00:00Z' } as never])
  vi.mocked(customerApi.createReservation).mockRejectedValue(
    new ApiError(409, 'SLOT_IN_THE_PAST', 'Esse horário já passou. Escolha um horário a partir de agora.'))
  renderAt('/cliente/agendar')

  fireEvent.click(await screen.findByRole('button', { name: /até/ }))
  fireEvent.click(screen.getByRole('button', { name: /confirmar agendamento/i }))

  expect(await screen.findByText('Esse horário já passou. Escolha um horário a partir de agora.')).toBeInTheDocument()
  expect(screen.queryByText(/acabou de ser ocupado/i)).not.toBeInTheDocument()
})
