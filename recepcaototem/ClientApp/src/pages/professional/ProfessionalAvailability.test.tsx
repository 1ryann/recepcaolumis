import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, expect, test, vi } from 'vitest'
import { ApiError } from '../../api/client'
import { professionalAvailabilityApi } from '../../api/modules'
import { ProfessionalAvailability } from './ProfessionalAvailability'

vi.mock('../../api/modules', async (original) => ({ ...await original<typeof import('../../api/modules')>(), professionalAvailabilityApi: { get: vi.fn(), update: vi.fn(), listExceptions: vi.fn(), createException: vi.fn(), updateException: vi.fn(), deleteException: vi.fn() } }))

const day = (dayOfWeek: string, intervals: { startTime: string, endTime: string }[] = []) => ({ dayOfWeek, intervals })
const buildingDays = ['MONDAY', 'TUESDAY', 'WEDNESDAY', 'THURSDAY', 'FRIDAY', 'SATURDAY', 'SUNDAY']
  .map((d) => day(d, [{ startTime: '08:00', endTime: '18:00' }]))
const availability = { mode: 'CUSTOM' as const, days: [day('MONDAY', [{ startTime: '09:00', endTime: '12:00' }]), day('TUESDAY'), day('WEDNESDAY'), day('THURSDAY'), day('FRIDAY'), day('SATURDAY'), day('SUNDAY')], effectiveDays: [day('MONDAY', [{ startTime: '09:00', endTime: '12:00' }])], globalDays: buildingDays, concurrencyToken: 'v1', existingReservationsOutsideAvailabilityCount: 0 }

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(professionalAvailabilityApi.get).mockResolvedValue(availability)
  vi.mocked(professionalAvailabilityApi.listExceptions).mockResolvedValue([])
})

test('loads the real availability and saves custom intervals with the current token', async () => {
  vi.mocked(professionalAvailabilityApi.update).mockResolvedValue({ ...availability, concurrencyToken: 'v2' })
  render(<MemoryRouter><ProfessionalAvailability /></MemoryRouter>)
  expect(await screen.findByText('Minha disponibilidade')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /salvar disponibilidade/i }))
  await waitFor(() => expect(professionalAvailabilityApi.update).toHaveBeenCalledWith(expect.objectContaining({ mode: 'CUSTOM', concurrencyToken: 'v1', days: expect.any(Array) })))
})

test('reloads after a stale concurrency response with an actionable message', async () => {
  vi.mocked(professionalAvailabilityApi.update).mockRejectedValueOnce(new ApiError(409, 'RESOURCE_MODIFIED', 'stale'))
  render(<MemoryRouter><ProfessionalAvailability /></MemoryRouter>)
  await screen.findByText('Minha disponibilidade')
  fireEvent.click(screen.getByRole('button', { name: /salvar disponibilidade/i }))
  expect(await screen.findByText(/alterada em outra sessão/i)).toBeInTheDocument()
  expect(professionalAvailabilityApi.get).toHaveBeenCalledTimes(2)
})

test('renders an outside-reservation warning without exposing customer data', async () => {
  vi.mocked(professionalAvailabilityApi.get).mockResolvedValueOnce({ ...availability, existingReservationsOutsideAvailabilityCount: 3 })
  render(<MemoryRouter><ProfessionalAvailability /></MemoryRouter>)
  expect(await screen.findByText(/3 agendamento/)).toBeInTheDocument()
  expect(screen.queryByText(/customer|telefone|email/i)).not.toBeInTheDocument()
})
