import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { ApiError } from '../../api/client'
import { professionalIncidentsApi } from '../../api/modules'
import { ReportIncident } from './ReportIncident'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalIncidentsApi: { report: vi.fn() },
}))

const report = vi.mocked(professionalIncidentsApi.report)
afterEach(() => vi.clearAllMocks())

function openDialog(onReported = vi.fn()) {
  render(<ReportIncident onReported={onReported} />)
  fireEvent.click(screen.getByRole('button', { name: /Registrar imprevisto/ }))
  return onReported
}

test('reports the next appointment by default and tells how many customers were notified', async () => {
  report.mockResolvedValue({ exceptionId: 'e1', affectedReservationIds: ['r1'], presence: 'ABSENT' })
  const onReported = openDialog()

  expect(screen.getByRole('radio', { name: /Só o próximo atendimento/ })).toBeChecked()
  fireEvent.change(screen.getByLabelText('Motivo (opcional)'), { target: { value: '  Pneu furado  ' } })
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar imprevisto' }))

  expect(await screen.findByRole('status')).toHaveTextContent('1 atendimento foi cancelado e o cliente foi avisado por WhatsApp')
  expect(report).toHaveBeenCalledWith({ type: 'NEXT_APPOINTMENT', untilTime: null, reason: 'Pneu furado' })
  expect(onReported).toHaveBeenCalledOnce()
})

test('"until a time" asks for the time and sends it', async () => {
  report.mockResolvedValue({ exceptionId: 'e1', affectedReservationIds: ['r1', 'r2'], presence: 'ABSENT' })
  openDialog()

  fireEvent.click(screen.getByRole('radio', { name: /Até um horário/ }))
  fireEvent.change(screen.getByLabelText('Indisponível até'), { target: { value: '15:30' } })
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar imprevisto' }))

  expect(await screen.findByRole('status')).toHaveTextContent('2 atendimentos foram cancelados')
  expect(report).toHaveBeenCalledWith({ type: 'UNTIL_TIME', untilTime: '15:30', reason: null })
})

test('says so when no appointment today was affected', async () => {
  report.mockResolvedValue({ exceptionId: null, affectedReservationIds: [], presence: 'ABSENT' })
  openDialog()

  fireEvent.click(screen.getByRole('radio', { name: /O resto do dia/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar imprevisto' }))

  expect(await screen.findByRole('status')).toHaveTextContent('Nenhum atendimento de hoje foi afetado')
  expect(report).toHaveBeenCalledWith({ type: 'REST_OF_DAY', untilTime: null, reason: null })
})

test('shows the server validation message and keeps the form open', async () => {
  report.mockRejectedValue(new ApiError(400, 'INVALID_INCIDENT', 'O horário informado é inválido.'))
  const onReported = openDialog()

  fireEvent.click(screen.getByRole('radio', { name: /Até um horário/ }))
  fireEvent.change(screen.getByLabelText('Indisponível até'), { target: { value: '07:00' } })
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar imprevisto' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('O horário informado é inválido.')
  expect(screen.getByRole('button', { name: 'Confirmar imprevisto' })).toBeEnabled()
  expect(onReported).not.toHaveBeenCalled()
})
