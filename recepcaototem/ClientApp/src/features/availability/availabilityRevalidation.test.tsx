import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, expect, test, vi } from 'vitest'
import { apiClient } from '../../api/client'
import { professionalAvailabilityApi } from '../../api/modules'
import { SessionProvider } from '../../auth/SessionProvider'
import { ProtectedRoute } from '../../components/ProtectedRoute'
import { ProfessionalAvailability } from '../../pages/professional/ProfessionalAvailability'

vi.mock('../../api/client', async (original) => ({
  ...await original<typeof import('../../api/client')>(),
  apiClient: { get: vi.fn(), post: vi.fn() },
}))
vi.mock('../../api/modules', async (original) => ({
  ...await original<typeof import('../../api/modules')>(),
  professionalAvailabilityApi: { get: vi.fn(), update: vi.fn(), listExceptions: vi.fn(), createException: vi.fn(), updateException: vi.fn(), deleteException: vi.fn() },
}))

const days = ['MONDAY', 'TUESDAY', 'WEDNESDAY', 'THURSDAY', 'FRIDAY', 'SATURDAY', 'SUNDAY'].map((dayOfWeek) => ({
  dayOfWeek,
  intervals: dayOfWeek === 'MONDAY' ? [{ startTime: '09:00', endTime: '12:00' }] : [],
}))
const availability = { mode: 'CUSTOM' as const, days, effectiveDays: days, concurrencyToken: 'tok-1', existingReservationsOutsideAvailabilityCount: 0 }
const identity = { userId: 'u1', displayName: 'Pro', email: 'pro@lumis.test', roles: ['PROFISSIONAL'], mustChangePassword: false }

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(apiClient.get).mockResolvedValue(identity)
  vi.mocked(professionalAvailabilityApi.get).mockResolvedValue(availability)
  vi.mocked(professionalAvailabilityApi.listExceptions).mockResolvedValue([])
})

test('a window focus revalidation keeps the availability editor mounted with unsaved edits', async () => {
  render(<MemoryRouter initialEntries={['/profissional/disponibilidade']}>
    <SessionProvider>
      <Routes>
        <Route element={<ProtectedRoute allowedRoles={['PROFISSIONAL']} />}>
          <Route path="/profissional/disponibilidade" element={<ProfessionalAvailability />} />
        </Route>
      </Routes>
    </SessionProvider>
  </MemoryRouter>)

  await screen.findByRole('button', { name: /salvar disponibilidade/i })
  fireEvent.change(screen.getAllByLabelText('Início')[0], { target: { value: '10:15' } })
  expect(screen.getAllByLabelText('Início')[0]).toHaveValue('10:15')

  const before = vi.mocked(apiClient.get).mock.calls.length
  fireEvent(window, new Event('focus'))
  await waitFor(() => expect(vi.mocked(apiClient.get).mock.calls.length).toBeGreaterThan(before))

  await waitFor(() => expect(screen.getAllByLabelText('Início')[0]).toHaveValue('10:15'))
  expect(professionalAvailabilityApi.get).toHaveBeenCalledTimes(1)
})
