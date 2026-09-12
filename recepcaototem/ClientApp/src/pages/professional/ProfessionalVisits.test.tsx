import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalVisitsApi } from '../../api/modules'
import { ProfessionalVisits } from './ProfessionalVisits'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalVisitsApi: { list: vi.fn(), detail: vi.fn(), start: vi.fn(), end: vi.fn(), cancel: vi.fn() },
}))

const waiting = {
  id: 'v1', professionalId: 'p1', professionalName: 'Maria', roomId: 'room1', roomName: 'Sala 1',
  reservationId: null, visitorName: 'João', status: 'WAITING' as const, arrivedAt: '2026-09-12T13:00:00Z',
  serviceStartedAt: null, endedAt: null, cancelledAt: null, createdAt: '2026-09-12T13:00:00Z',
  updatedAt: '2026-09-12T13:00:00Z', concurrencyToken: 'tok-1', history: [],
}

beforeEach(() => {
  vi.mocked(professionalVisitsApi.list).mockImplementation(async (query) => {
    if (query.status === 'WAITING') return { items: [waiting], page: 1, pageSize: 50, totalCount: 1 }
    return { items: [], page: 1, pageSize: 50, totalCount: 0 }
  })
})

test('shows João under Aguardando with an Iniciar atendimento action', async () => {
  render(<ProfessionalVisits />)
  expect(await screen.findByText('João')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /iniciar atendimento/i })).toBeInTheDocument()
})

test('Iniciar atendimento calls start with the concurrency token', async () => {
  vi.mocked(professionalVisitsApi.start).mockResolvedValue({ ...waiting, status: 'IN_SERVICE' })
  render(<ProfessionalVisits />)
  fireEvent.click(await screen.findByRole('button', { name: /iniciar atendimento/i }))
  await waitFor(() => expect(professionalVisitsApi.start).toHaveBeenCalledWith('v1', 'tok-1'))
})

test('"Encerrados hoje" queries the zone calendar day even at 23:30 local time, not the UTC day', async () => {
  // 2026-09-12T23:30 in America/Porto_Velho (UTC-4) is 2026-09-13T03:30Z: a
  // buggy UTC-based day computation would derive 2026-09-13 as "today" here.
  vi.setSystemTime(new Date('2026-09-13T03:30:00Z'))
  try {
    render(<ProfessionalVisits />)
    await waitFor(() => expect(professionalVisitsApi.list).toHaveBeenCalledWith(
      expect.objectContaining({ status: 'ENDED', from: '2026-09-12T04:00:00.000Z', to: '2026-09-13T03:59:59.000Z' }),
      expect.anything(),
    ))
  } finally {
    vi.useRealTimers()
  }
})
