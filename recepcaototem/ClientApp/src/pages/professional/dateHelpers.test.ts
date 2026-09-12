import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { todayZoneDateKey, zoneDateKey, zoneDayBoundary, zoneLocalToIso } from './dateHelpers'

afterEach(() => {
  vi.useRealTimers()
})

test('todayZoneDateKey uses the zone calendar day, not the UTC day, near a midnight boundary', () => {
  // 2026-09-12T23:30 in America/Porto_Velho (UTC-4) is 2026-09-13T03:30Z: the
  // UTC calendar day has already rolled over to the 13th, but the zone's own
  // wall clock is still on the 12th.
  vi.setSystemTime(new Date('2026-09-13T03:30:00Z'))
  expect(todayZoneDateKey()).toBe('2026-09-12')
})

test('zoneDateKey groups a late-evening instant under the zone calendar day, not the UTC day', () => {
  // 2026-09-12T23:30 local (Porto Velho, UTC-4) is 2026-09-13T03:30Z.
  expect(zoneDateKey(new Date('2026-09-13T03:30:00Z'))).toBe('2026-09-12')
})

test('zoneDayBoundary derives the start/end instants for a date from the named zone, not a hardcoded offset', () => {
  expect(zoneDayBoundary('2026-09-12', false)).toBe('2026-09-12T04:00:00.000Z')
  expect(zoneDayBoundary('2026-09-12', true)).toBe('2026-09-13T03:59:59.000Z')
})

test('zoneLocalToIso converts a datetime-local wall-clock value in the zone to UTC', () => {
  expect(zoneLocalToIso('2026-09-12T13:00')).toBe('2026-09-12T17:00:00.000Z')
})

test('ProfessionalVisits "Encerrados hoje" window stays on the correct day at 23:30 local time', () => {
  vi.setSystemTime(new Date('2026-09-13T03:30:00Z')) // 2026-09-12 23:30 in Porto Velho
  const dateKey = todayZoneDateKey()
  const from = zoneDayBoundary(dateKey, false)
  const to = zoneDayBoundary(dateKey, true)
  expect(dateKey).toBe('2026-09-12')
  expect(from).toBe('2026-09-12T04:00:00.000Z')
  expect(to).toBe('2026-09-13T03:59:59.000Z')
})
