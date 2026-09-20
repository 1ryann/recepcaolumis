import { expect, test } from 'vitest'
import { availabilityLabel, buildingToday, dateLabel } from './roomAvailability'

test('a bare date becomes a Brazilian date without ever going through new Date', () => {
  expect(dateLabel('2026-11-16')).toBe('16/11/2026')
  // The trap this guards: `new Date('2026-01-01')` is UTC midnight, which is 31/12 in
  // Porto Velho. Splitting the string cannot drift.
  expect(dateLabel('2026-01-01')).toBe('01/01/2026')
})

test('the near future reads as a word and the rest as a date', () => {
  expect(availabilityLabel('2026-09-20', '2026-09-20')).toBe('hoje')
  expect(availabilityLabel('2026-09-21', '2026-09-20')).toBe('amanhã')
  expect(availabilityLabel('2026-09-22', '2026-09-20')).toBe('em 22/09/2026')
})

test('tomorrow is still tomorrow across a month and a year boundary', () => {
  expect(availabilityLabel('2026-10-01', '2026-09-30')).toBe('amanhã')
  expect(availabilityLabel('2027-01-01', '2026-12-31')).toBe('amanhã')
})

// A room whose lease ended while nobody reloaded the page is free now, not overdue.
test('a date already past reads as today rather than as a stale date', () => {
  expect(availabilityLabel('2026-09-01', '2026-09-20')).toBe('hoje')
})

test('today is resolved in the building timezone, not the viewer one', () => {
  // 03:00 UTC on the 21st is still 23:00 on the 20th in Porto Velho (UTC-4). A visitor
  // in another timezone must be told the same day as the kiosk in the lobby.
  expect(buildingToday(new Date('2026-09-21T03:00:00Z'))).toBe('2026-09-20')
  expect(buildingToday(new Date('2026-09-21T05:00:00Z'))).toBe('2026-09-21')
})
