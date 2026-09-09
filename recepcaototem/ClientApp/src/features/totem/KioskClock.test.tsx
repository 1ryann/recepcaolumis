import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { KioskClock } from './KioskClock'

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

test('shows the time as HH:mm and a weekday + date line', () => {
  render(<KioskClock />)
  expect(screen.getByText(/^\d{2}:\d{2}$/)).toBeInTheDocument()
  // pt-BR weekday names all end in "-feira" or are "sábado"/"domingo"
  expect(screen.getByText(/-feira|sábado|domingo/i)).toBeInTheDocument()
})

test('clears its interval on unmount — no timer leak on a long-lived kiosk', () => {
  const clearSpy = vi.spyOn(globalThis, 'clearInterval')
  const view = render(<KioskClock />)
  expect(vi.getTimerCount()).toBe(1)
  view.unmount()
  expect(clearSpy).toHaveBeenCalled()
  expect(vi.getTimerCount()).toBe(0)
})
