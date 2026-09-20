import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import { ApiError } from '../api/client'
import { rescheduleApi } from '../api/modules'
import { ThemeProvider } from '../theme/ThemeProvider'
import { RescheduleLink } from './RescheduleLink'

vi.mock('../api/modules', async (orig) => ({
  ...(await orig<typeof import('../api/modules')>()),
  rescheduleApi: { resolve: vi.fn(), slots: vi.fn(), confirm: vi.fn() },
}))

const token = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'

const resolved = {
  professionalId: 'p-1',
  professionalName: 'Dra. Helena',
  originalStartAt: '2026-09-23T13:00:00+00:00',
  originalEndAt: '2026-09-23T14:00:00+00:00',
  durationMinutes: 60,
  expiresAt: '2026-09-25T13:00:00+00:00',
}

function renderAt(path = `/reagendar/${token}`) {
  return render(<ThemeProvider><MemoryRouter initialEntries={[path]}><Routes>
    <Route path="/reagendar/:token" element={<RescheduleLink />} />
  </Routes></MemoryRouter></ThemeProvider>)
}

beforeEach(() => {
  vi.mocked(rescheduleApi.resolve).mockReset().mockResolvedValue(resolved as never)
  vi.mocked(rescheduleApi.slots).mockReset().mockResolvedValue([
    { startAt: '2026-09-24T13:00:00+00:00', endAt: '2026-09-24T14:00:00+00:00' },
  ] as never)
  vi.mocked(rescheduleApi.confirm).mockReset()
})

test('resolves the link with the token from the URL and shows the cancelled appointment', async () => {
  renderAt()
  await screen.findByText(/Dra. Helena/)
  expect(rescheduleApi.resolve).toHaveBeenCalledWith(token)
})

test('an invalid or expired link shows a clear message and offers no slots', async () => {
  vi.mocked(rescheduleApi.resolve).mockRejectedValue(new ApiError(400, 'INVALID_RESCHEDULE_LINK', 'inválido'))
  renderAt()
  await screen.findByRole('alert')
  expect(screen.getByRole('alert').textContent).toMatch(/inválido|expirou/i)
  expect(rescheduleApi.slots).not.toHaveBeenCalled()
})

test('picking a slot confirms the reschedule with the same token and shows the new appointment', async () => {
  vi.mocked(rescheduleApi.confirm).mockResolvedValue({
    reservationId: 'r-9', startAt: '2026-09-24T13:00:00+00:00', endAt: '2026-09-24T14:00:00+00:00',
    professionalName: 'Dra. Helena', roomName: 'Sala 03',
  } as never)
  renderAt()
  const slot = await screen.findByRole('button', { name: /09:00/ })
  fireEvent.click(slot)
  fireEvent.click(screen.getByRole('button', { name: /confirmar/i }))
  await waitFor(() => expect(rescheduleApi.confirm).toHaveBeenCalledWith({
    token, startAt: '2026-09-24T13:00:00+00:00', endAt: '2026-09-24T14:00:00+00:00',
  }))
  await screen.findByText(/Sala 03/)
})

test('a slot taken meanwhile is reported without losing the link', async () => {
  vi.mocked(rescheduleApi.confirm).mockRejectedValue(new ApiError(409, 'ROOM_UNAVAILABLE', 'ocupado'))
  renderAt()
  fireEvent.click(await screen.findByRole('button', { name: /09:00/ }))
  fireEvent.click(screen.getByRole('button', { name: /confirmar/i }))
  await screen.findByRole('alert')
  expect(screen.getByRole('alert').textContent).toMatch(/ocupado|outro horário/i)
  expect(screen.getByRole('button', { name: /confirmar/i })).toBeInTheDocument()
})

test('never renders the raw token on the page', async () => {
  const { container } = renderAt()
  await screen.findByText(/Dra. Helena/)
  expect(container.innerHTML).not.toContain(token)
})
