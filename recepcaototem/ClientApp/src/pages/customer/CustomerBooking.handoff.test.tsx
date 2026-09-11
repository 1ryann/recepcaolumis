import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { customerApi, totemApi, type CustomerProfessionalDto, type ResolveHandoffDto } from '../../api/modules'
import { CustomerBooking } from './CustomerBooking'

// `useNavigate` is spied so we can assert the ?handoff= token is dropped from the
// visible URL via `navigate('/cliente/agendar', { replace: true })` after a
// successful resolve. Every other react-router export stays real.
const navigateSpy = vi.fn()
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => navigateSpy,
}))
vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  customerApi: { professionals: vi.fn(), availability: vi.fn(), resolveHandoff: vi.fn(), createReservation: vi.fn() },
  totemApi: { claimHandoff: vi.fn() },
}))

const people: CustomerProfessionalDto[] = [
  { id: 'p1', name: 'Ana Souza', profession: 'Fisioterapia', description: null },
  { id: 'p2', name: 'Bruno Lima', profession: 'Psicologia', description: null },
]
const resolved: ResolveHandoffDto = {
  handoffId: 'h1',
  professionalId: 'p2',
  professionalName: 'Bruno Lima',
  profession: 'Psicologia',
  expiresAt: '2026-09-10T10:00:00Z',
}
const slot = { startAt: '2026-09-10T10:00:00Z', endAt: '2026-09-10T11:00:00Z' }
const reservation = { id: 'r1' }

function renderAt(entry: string) {
  return render(<MemoryRouter initialEntries={[entry]}><Routes>
    <Route path="/cliente/agendar" element={<CustomerBooking />} />
  </Routes></MemoryRouter>)
}

beforeEach(() => {
  vi.mocked(customerApi.professionals).mockResolvedValue(people)
  vi.mocked(customerApi.availability).mockResolvedValue([slot])
  vi.mocked(customerApi.createReservation).mockResolvedValue(reservation as any)
  vi.mocked(customerApi.resolveHandoff).mockResolvedValue(resolved)
  vi.mocked(totemApi.claimHandoff).mockResolvedValue({ status: 'CLAIMED', expiresAt: '2026-09-10T10:00:00Z' })
})
afterEach(() => vi.clearAllMocks())

test('claims and resolves the handoff on mount, and preselects the resolved professional over ?professionalId', async () => {
  renderAt('/cliente/agendar?handoff=TOKEN&professionalId=p1')
  await waitFor(() => expect(totemApi.claimHandoff).toHaveBeenCalledWith('TOKEN'))
  await waitFor(() => expect(customerApi.resolveHandoff).toHaveBeenCalledWith('TOKEN'))
  const select = await screen.findByLabelText(/profissional/i)
  await waitFor(() => expect(select).toHaveValue('p2'))
})

test('drops ?handoff= from the address bar after a successful resolve', async () => {
  renderAt('/cliente/agendar?handoff=TOKEN')
  await waitFor(() => expect(navigateSpy).toHaveBeenCalledWith('/cliente/agendar', { replace: true }))
})

test('includes the handoff token when confirming a reservation', async () => {
  const { container } = renderAt('/cliente/agendar?handoff=TOKEN')
  const select = await screen.findByLabelText(/profissional/i)
  await waitFor(() => expect(select).toHaveValue('p2'))
  await waitFor(() => expect(container.querySelector('.customer-slot')).toBeInTheDocument())
  fireEvent.click(container.querySelector('.customer-slot')!)
  fireEvent.click(screen.getByRole('button', { name: /confirmar agendamento/i }))
  await waitFor(() => expect(customerApi.createReservation).toHaveBeenCalledWith({
    professionalId: 'p2',
    startAt: slot.startAt,
    endAt: slot.endAt,
    handoffToken: 'TOKEN',
  }))
})

test('shows the expiry message and falls back to normal professional selection when resolve fails', async () => {
  vi.mocked(customerApi.resolveHandoff).mockRejectedValue(new Error('expired'))
  renderAt('/cliente/agendar?handoff=TOKEN&professionalId=p2')
  expect(await screen.findByText('Este convite expirou. Você pode escolher o profissional normalmente.')).toBeInTheDocument()
  const select = await screen.findByLabelText(/profissional/i)
  await waitFor(() => expect(select).toHaveValue('p2'))
  expect(customerApi.professionals).toHaveBeenCalled()
})

test('never claims/resolves and omits handoffToken when no ?handoff= is present', async () => {
  const { container } = renderAt('/cliente/agendar?professionalId=p1')
  const select = await screen.findByLabelText(/profissional/i)
  await waitFor(() => expect(select).toHaveValue('p1'))
  await waitFor(() => expect(container.querySelector('.customer-slot')).toBeInTheDocument())
  fireEvent.click(container.querySelector('.customer-slot')!)
  fireEvent.click(screen.getByRole('button', { name: /confirmar agendamento/i }))
  await waitFor(() => expect(customerApi.createReservation).toHaveBeenCalled())
  expect(totemApi.claimHandoff).not.toHaveBeenCalled()
  expect(customerApi.resolveHandoff).not.toHaveBeenCalled()
  const call = vi.mocked(customerApi.createReservation).mock.calls[0][0]
  expect(call).not.toHaveProperty('handoffToken')
})
