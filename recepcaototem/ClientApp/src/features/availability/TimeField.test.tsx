import { fireEvent, render, screen, within } from '@testing-library/react'
import { useState } from 'react'
import { expect, test, vi } from 'vitest'
import { TimeField } from './TimeField'

function Harness({ min, max }: { min?: string, max?: string }) {
  const [value, setValue] = useState('09:00')
  return <TimeField value={value} onChange={setValue} label="Abertura" min={min} max={max} />
}

test('opens the grid on focus and lists 30-min options within [min, max]', () => {
  render(<Harness min="08:00" max="10:00" />)
  fireEvent.focus(screen.getByLabelText('Abertura'))
  const options = within(screen.getByRole('listbox')).getAllByRole('option').map((node) => node.textContent)
  expect(options).toEqual(['08:00', '08:30', '09:00', '09:30', '10:00'])
})

test('picking an option sets the value and closes the grid', () => {
  render(<Harness min="08:00" max="12:00" />)
  fireEvent.focus(screen.getByLabelText('Abertura'))
  fireEvent.click(within(screen.getByRole('listbox')).getByRole('option', { name: '10:30' }))
  expect(screen.getByLabelText('Abertura')).toHaveValue('10:30')
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
})

test('the current value is marked selected', () => {
  render(<Harness min="08:00" max="12:00" />)
  fireEvent.focus(screen.getByLabelText('Abertura'))
  expect(within(screen.getByRole('listbox')).getByRole('option', { name: '09:00' })).toHaveAttribute('aria-selected', 'true')
})

test('typing HH:mm updates the value', () => {
  render(<Harness />)
  fireEvent.change(screen.getByLabelText('Abertura'), { target: { value: '07:45' } })
  expect(screen.getByLabelText('Abertura')).toHaveValue('07:45')
})

test('Escape and outside click close the grid', () => {
  render(<div><Harness /><button type="button">fora</button></div>)
  const input = screen.getByLabelText('Abertura')
  fireEvent.focus(input)
  expect(screen.getByRole('listbox')).toBeInTheDocument()
  fireEvent.keyDown(input, { key: 'Escape' })
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  fireEvent.focus(input)
  fireEvent.mouseDown(screen.getByText('fora'))
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
})

test('disabled never opens the grid', () => {
  render(<TimeField value="09:00" onChange={vi.fn()} label="Abertura" disabled />)
  fireEvent.focus(screen.getByLabelText('Abertura'))
  fireEvent.click(screen.getByRole('button', { name: /escolher abertura/i }))
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
})
