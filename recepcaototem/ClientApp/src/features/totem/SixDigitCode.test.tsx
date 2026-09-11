import { fireEvent, render, screen } from '@testing-library/react'
import { expect, test, vi } from 'vitest'
import { SixDigitCode } from './SixDigitCode'

test('exposes a single control labelled "código de 6 dígitos"', () => {
  render(<SixDigitCode value="" onChange={vi.fn()} />)
  const input = screen.getByLabelText(/código de 6 dígitos/i)
  expect(input).toHaveAttribute('inputmode', 'numeric')
  expect(input).toHaveAttribute('autocomplete', 'one-time-code')
})

test('keeps only up to 6 digits, preserves leading zeros', () => {
  const onChange = vi.fn()
  render(<SixDigitCode value="" onChange={onChange} />)
  fireEvent.change(screen.getByLabelText(/código de 6 dígitos/i), { target: { value: '0a0b1c2d3e9' } })
  expect(onChange).toHaveBeenLastCalledWith('001239')
})

test('renders 6 cells reflecting the value', () => {
  const { container } = render(<SixDigitCode value="0429" onChange={vi.fn()} />)
  const cells = container.querySelectorAll('.totem-code-cell')
  expect(cells).toHaveLength(6)
  expect(cells[0].textContent).toBe('0')
  expect(cells[3].textContent).toBe('9')
  expect(cells[4].textContent).toBe('')
})

test('Enter submits only when complete', () => {
  const onSubmit = vi.fn()
  const { rerender } = render(<SixDigitCode value="12345" onChange={vi.fn()} onSubmit={onSubmit} />)
  fireEvent.keyDown(screen.getByLabelText(/código de 6 dígitos/i), { key: 'Enter' })
  expect(onSubmit).not.toHaveBeenCalled()
  rerender(<SixDigitCode value="123456" onChange={vi.fn()} onSubmit={onSubmit} />)
  fireEvent.keyDown(screen.getByLabelText(/código de 6 dígitos/i), { key: 'Enter' })
  expect(onSubmit).toHaveBeenCalledTimes(1)
})
