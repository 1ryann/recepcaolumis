import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import { customerApi, type CustomerProfessionalDto } from '../../api/modules'
import { CustomerBooking } from './CustomerBooking'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  customerApi: { professionals: vi.fn(), availability: vi.fn() },
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
