import { expect, test } from 'vitest'
import { isComplete6, onlyDigits6 } from './digits6'
test('keeps only digits, max 6, leading zeros', () => {
  expect(onlyDigits6('a1b2c3d4')).toBe('1234')
  expect(onlyDigits6('123456789')).toBe('123456')
  expect(onlyDigits6('00 12 34')).toBe('001234')
})
test('isComplete6', () => {
  expect(isComplete6('001234')).toBe(true)
  expect(isComplete6('1234')).toBe(false)
})
