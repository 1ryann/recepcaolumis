import { expect, test } from 'vitest'
import { formatBrl, parseRoomRate, ROOM_RATE_MAXIMUM } from './money'

test.each([
  ['0', 0], ['0,01', 0.01], ['100', 100], ['100,5', 100.5], ['100,50', 100.5],
  ['1.234,56', 1234.56], ['9999999999999,99', 9999999999999.99],
])('parses strict Brazilian room rate %s', (text, expected) => {
  expect(parseRoomRate(text)).toBe(expected)
})

test.each([
  '', ' ', '-1', '01', '1.23', '1,234.56', '1.23,45', '100,555', 'R$ 100,00',
  '1e3', 'NaN', 'Infinity', '99999999999999,99', '12.34,56',
])('rejects invalid room rate %s', text => {
  expect(parseRoomRate(text)).toBeNull()
})

test('uses the conservative application maximum', () => {
  expect(ROOM_RATE_MAXIMUM).toBe(9999999999999.99)
  expect(parseRoomRate('9999999999999,99')).toBe(ROOM_RATE_MAXIMUM)
  expect(parseRoomRate('10000000000000,00')).toBeNull()
})

test('formats display values as Brazilian real', () => {
  const formatted = formatBrl(1234.56)
  expect(formatted).toContain('R$')
  expect(formatted).toContain('1.234,56')
})

test.each([0, 0.01, 0.10, 100.99, 9999999999999.99])(
  'round-trips approved JSON number %s without currency strings or transformation', value => {
    const body = { hourlyRate: value, dailyRate: value }
    const wire = JSON.stringify(body)
    expect(wire).not.toContain('R$')
    const received = JSON.parse(wire) as typeof body
    expect(typeof received.hourlyRate).toBe('number')
    expect(received).toEqual(body)
    expect(JSON.stringify(received)).toBe(wire)
  },
)
