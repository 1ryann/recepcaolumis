import { fireEvent, render, screen, within } from '@testing-library/react'
import { type ComponentProps, useState } from 'react'
import { expect, test, vi } from 'vitest'
import { type DayDraft, newPeriod, WeeklyPeriodsEditor } from './WeeklyPeriodsEditor'

const seed = (): DayDraft[] => [
  { dayOfWeek: 'MONDAY', open: true, periods: [newPeriod('08:30', '18:30')] },
  { dayOfWeek: 'TUESDAY', open: false, periods: [] },
]

function Harness(props: Partial<ComponentProps<typeof WeeklyPeriodsEditor>> = {}) {
  const [days, setDays] = useState<DayDraft[]>(seed)
  return <WeeklyPeriodsEditor
    days={days}
    onChange={setDays}
    startLabel="Abertura"
    endLabel="Fechamento"
    closedLabel="Fechado"
    defaultPeriod={{ start: '08:30', end: '18:30' }}
    dayLabel={(day) => day}
    {...props}
  />
}

test('reopening a closed day seeds the default period', () => {
  render(<Harness />)
  const tuesday = screen.getByTestId('wpe-day-TUESDAY')
  expect(within(tuesday).getByText('Fechado')).toBeInTheDocument()
  fireEvent.click(within(tuesday).getByRole('switch'))
  expect(within(tuesday).getAllByLabelText('Abertura')[0]).toHaveValue('08:30')
  expect(within(tuesday).getAllByLabelText('Fechamento')[0]).toHaveValue('18:30')
})

test('adding a period keeps the existing row mounted and its value', () => {
  render(<Harness />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  fireEvent.change(within(monday).getAllByLabelText('Abertura')[0], { target: { value: '09:15' } })
  fireEvent.click(within(monday).getByRole('button', { name: /adicionar período/i }))
  const opens = within(monday).getAllByLabelText('Abertura')
  expect(opens).toHaveLength(2)
  expect(opens[0]).toHaveValue('09:15')
})

test('an inverted range shows an inline error', () => {
  render(<Harness />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  fireEvent.change(within(monday).getAllByLabelText('Fechamento')[0], { target: { value: '07:00' } })
  expect(within(monday).getByText(/início deve ser anterior ao fim/i)).toBeInTheDocument()
})

test('the time grid opens beside the field and picks a value', () => {
  render(<Harness gridRangeForDay={() => ({ min: '08:00', max: '12:00' })} />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  fireEvent.focus(within(monday).getAllByLabelText('Abertura')[0])
  const grid = within(monday).getAllByRole('listbox')[0]
  fireEvent.click(within(grid).getByRole('option', { name: '10:00' }))
  expect(within(monday).getAllByLabelText('Abertura')[0]).toHaveValue('10:00')
  expect(within(monday).queryAllByRole('listbox')).toHaveLength(0)
})

test('overlapping periods flag both rows', () => {
  render(<Harness />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  fireEvent.click(within(monday).getByRole('button', { name: /adicionar período/i }))
  const opens = within(monday).getAllByLabelText('Abertura')
  const closes = within(monday).getAllByLabelText('Fechamento')
  fireEvent.change(opens[0], { target: { value: '09:00' } })
  fireEvent.change(closes[0], { target: { value: '12:00' } })
  fireEvent.change(opens[1], { target: { value: '11:00' } })
  fireEvent.change(closes[1], { target: { value: '13:00' } })
  expect(within(monday).getAllByText(/não podem se sobrepor/i).length).toBe(2)
})

test('removing is blocked when a single period remains and works with more', () => {
  render(<Harness />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  expect(within(monday).getByRole('button', { name: /remover período/i })).toBeDisabled()
  fireEvent.click(within(monday).getByRole('button', { name: /adicionar período/i }))
  const removes = within(monday).getAllByRole('button', { name: /remover período/i })
  expect(removes[0]).toBeEnabled()
  fireEvent.click(removes[0])
  expect(within(monday).getAllByLabelText('Abertura')).toHaveLength(1)
})

test('closing a day hides its periods and reports it closed', () => {
  const onChange = vi.fn()
  render(<WeeklyPeriodsEditor
    days={seed()} onChange={onChange} startLabel="Abertura" endLabel="Fechamento"
    closedLabel="Fechado" defaultPeriod={{ start: '08:30', end: '18:30' }} dayLabel={(d) => d} />)
  fireEvent.click(within(screen.getByTestId('wpe-day-MONDAY')).getByRole('switch'))
  expect(onChange).toHaveBeenCalledWith([
    { dayOfWeek: 'MONDAY', open: false, periods: [] },
    { dayOfWeek: 'TUESDAY', open: false, periods: [] },
  ])
})

test('the per-day hint renders when provided', () => {
  render(<Harness hintForDay={(day) => (day === 'MONDAY' ? 'Estabelecimento: 08:30–18:30' : null)} />)
  expect(within(screen.getByTestId('wpe-day-MONDAY')).getByText('Estabelecimento: 08:30–18:30')).toBeInTheDocument()
  expect(within(screen.getByTestId('wpe-day-TUESDAY')).queryByText(/Estabelecimento/)).not.toBeInTheDocument()
})

test('disabled turns off every control', () => {
  render(<Harness disabled />)
  const monday = screen.getByTestId('wpe-day-MONDAY')
  expect(within(monday).getAllByLabelText('Abertura')[0]).toBeDisabled()
  expect(within(monday).getByRole('switch')).toBeDisabled()
  expect(within(monday).getByRole('button', { name: /adicionar período/i })).toBeDisabled()
})
