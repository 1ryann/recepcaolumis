import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { ApiError } from '../../api/client'
import { adminProfessionalAvailabilityApi } from '../../api/modules'
import { AdminProfessionalAvailability } from './AdminProfessionalAvailability'

vi.mock('../../api/modules', async (original) => ({ ...await original<typeof import('../../api/modules')>(), adminProfessionalAvailabilityApi: { get: vi.fn(), update: vi.fn(), listExceptions: vi.fn(), createException: vi.fn(), updateException: vi.fn(), deleteException: vi.fn() } }))

const day = (dayOfWeek: string, intervals: { startTime: string, endTime: string }[] = []) => ({ dayOfWeek, intervals })
const buildingDays = ['MONDAY', 'TUESDAY', 'WEDNESDAY', 'THURSDAY', 'FRIDAY', 'SATURDAY', 'SUNDAY']
  .map((d) => day(d, [{ startTime: '08:00', endTime: '18:00' }]))
const availability = { mode: 'INHERIT_GLOBAL' as const, days: [day('MONDAY'), day('TUESDAY'), day('WEDNESDAY'), day('THURSDAY'), day('FRIDAY'), day('SATURDAY'), day('SUNDAY')], effectiveDays: [day('MONDAY', [{ startTime: '08:00', endTime: '18:00' }])], globalDays: buildingDays, concurrencyToken: 'v1', existingReservationsOutsideAvailabilityCount: 1 }

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(adminProfessionalAvailabilityApi.get).mockResolvedValue(availability)
  vi.mocked(adminProfessionalAvailabilityApi.listExceptions).mockResolvedValue([])
})

test('loads and saves the selected professional availability through Operations APIs', async () => {
  vi.mocked(adminProfessionalAvailabilityApi.update).mockResolvedValue({ ...availability, concurrencyToken: 'v2' })
  render(<AdminProfessionalAvailability professionalId="p-1" professionalName="Ana Silva" />)
  expect(await screen.findByText('Disponibilidade de Ana Silva')).toBeInTheDocument()
  expect(screen.getByText(/1 agendamento/)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /salvar disponibilidade/i }))
  await waitFor(() => expect(adminProfessionalAvailabilityApi.update).toHaveBeenCalledWith('p-1', expect.objectContaining({ mode: 'INHERIT_GLOBAL', concurrencyToken: 'v1' })))
})

test('reloads the selected professional after a stale response', async () => {
  vi.mocked(adminProfessionalAvailabilityApi.update).mockRejectedValueOnce(new ApiError(409, 'RESOURCE_MODIFIED', 'stale'))
  render(<AdminProfessionalAvailability professionalId="p-1" professionalName="Ana Silva" />)
  await screen.findByText('Disponibilidade de Ana Silva')
  fireEvent.click(screen.getByRole('button', { name: /salvar disponibilidade/i }))
  expect(await screen.findByText(/alterada em outra sessão/i)).toBeInTheDocument()
  expect(adminProfessionalAvailabilityApi.get).toHaveBeenCalledTimes(2)
})
