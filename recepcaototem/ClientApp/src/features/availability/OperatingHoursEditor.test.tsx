import { fireEvent, render, screen, within } from '@testing-library/react'
import { expect, test, vi } from 'vitest'
import type { OperatingHoursDto } from '../../api/modules'
import { OperatingHoursEditor } from './OperatingHoursEditor'

const WEEK = ['MONDAY', 'TUESDAY', 'WEDNESDAY', 'THURSDAY', 'FRIDAY', 'SATURDAY', 'SUNDAY']
const week = (fn: (i: number) => { opensAt: string, closesAt: string }[]) =>
  WEEK.map((dayOfWeek, i) => ({ dayOfWeek, intervals: fn(i) }))

const unconfigured: OperatingHoursDto = { configured: false, days: week(() => []), concurrencyToken: null }
const configured: OperatingHoursDto = {
  configured: true,
  days: week((i) => (i < 5 ? [{ opensAt: '09:00', closesAt: '17:00' }] : [])),
  concurrencyToken: 'v1',
}

test('unconfigured seeds 7 open days at 08:30-18:30 and does not save until confirmed', () => {
  const onSave = vi.fn()
  render(<OperatingHoursEditor value={unconfigured} pending={false} onSave={onSave} />)
  expect(screen.getByText(/ainda não foi configurado/i)).toBeInTheDocument()
  expect(screen.getAllByLabelText('Abertura')).toHaveLength(7)
  expect(screen.getAllByLabelText('Abertura')[0]).toHaveValue('08:30')
  expect(screen.getAllByLabelText('Fechamento')[0]).toHaveValue('18:30')
  expect(onSave).not.toHaveBeenCalled()
})

test('save sends 7 days with HH:mm and empty intervals for closed days', () => {
  const onSave = vi.fn()
  render(<OperatingHoursEditor value={configured} pending={false} onSave={onSave} />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  fireEvent.click(within(monday).getByRole('button', { name: /adicionar período/i }))
  fireEvent.change(within(monday).getAllByLabelText('Abertura')[1], { target: { value: '18:00' } })
  fireEvent.change(within(monday).getAllByLabelText('Fechamento')[1], { target: { value: '20:00' } })
  fireEvent.click(screen.getByRole('button', { name: /salvar horário/i }))
  const payload = onSave.mock.calls[0][0]
  expect(payload).toHaveLength(7)
  expect(payload[0]).toEqual({
    dayOfWeek: 'MONDAY',
    intervals: [{ opensAt: '09:00', closesAt: '17:00' }, { opensAt: '18:00', closesAt: '20:00' }],
  })
  expect(payload[6]).toEqual({ dayOfWeek: 'SUNDAY', intervals: [] })
})

test('apply-to-all sets every open day to the top pair', () => {
  render(<OperatingHoursEditor value={configured} pending={false} onSave={vi.fn()} />)
  fireEvent.change(screen.getByLabelText('Aplicar abertura'), { target: { value: '10:00' } })
  fireEvent.change(screen.getByLabelText('Aplicar fechamento'), { target: { value: '16:00' } })
  fireEvent.click(screen.getByRole('button', { name: /aplicar a todos os dias/i }))
  expect(within(screen.getByTestId('wpe-day-MONDAY')).getAllByLabelText('Abertura')[0]).toHaveValue('10:00')
  expect(within(screen.getByTestId('wpe-day-TUESDAY')).getAllByLabelText('Fechamento')[0]).toHaveValue('16:00')
})

test('save is disabled until a change is made and while a range is invalid', () => {
  render(<OperatingHoursEditor value={configured} pending={false} onSave={vi.fn()} />)
  const save = screen.getByRole('button', { name: /salvar horário/i })
  expect(save).toBeDisabled()
  const monday = screen.getByTestId('wpe-day-MONDAY')
  fireEvent.change(within(monday).getAllByLabelText('Fechamento')[0], { target: { value: '18:00' } })
  expect(save).toBeEnabled()
  fireEvent.change(within(monday).getAllByLabelText('Fechamento')[0], { target: { value: '08:00' } })
  expect(save).toBeDisabled()
})

test('a closed day can be reopened and is then saved with a period', () => {
  const onSave = vi.fn()
  render(<OperatingHoursEditor value={configured} pending={false} onSave={onSave} />)
  fireEvent.click(within(screen.getByTestId('wpe-day-SATURDAY')).getByRole('switch'))
  fireEvent.click(screen.getByRole('button', { name: /salvar horário/i }))
  const payload = onSave.mock.calls[0][0]
  expect(payload[5]).toEqual({ dayOfWeek: 'SATURDAY', intervals: [{ opensAt: '08:30', closesAt: '18:30' }] })
})
