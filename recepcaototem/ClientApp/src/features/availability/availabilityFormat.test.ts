import { describe, expect, test } from 'vitest'
import { normalizeAvailabilityDays, toTimeInputValue } from './availabilityFormat'

describe('availability formatting', () => {
  test('normalizes a backend seven-day collection for editing', () => {
    const days = normalizeAvailabilityDays([
      { dayOfWeek: 'MONDAY', intervals: [{ startTime: '08:00:00', endTime: '12:00:00' }] },
    ])
    expect(days).toHaveLength(7)
    expect(days[0]).toEqual({ dayOfWeek: 'MONDAY', intervals: [{ startTime: '08:00', endTime: '12:00' }] })
    expect(days[1].intervals).toEqual([])
  })

  test('keeps the browser time input at minute precision', () => {
    expect(toTimeInputValue('14:30:00')).toBe('14:30')
    expect(toTimeInputValue('09:15')).toBe('09:15')
  })
})
