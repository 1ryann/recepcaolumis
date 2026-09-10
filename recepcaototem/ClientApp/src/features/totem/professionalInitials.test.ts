import { expect, test } from 'vitest'
import { professionalInitials } from './professionalInitials'

test('two words -> first letter of first and last, uppercased', () => {
  expect(professionalInitials('Dra. Helena Smoke')).toBe('HS')
  expect(professionalInitials('ana souza')).toBe('AS')
})
test('single word -> one letter', () => {
  expect(professionalInitials('Beatriz')).toBe('B')
})
test('empty / whitespace -> stable placeholder', () => {
  expect(professionalInitials('')).toBe('?')
  expect(professionalInitials('   ')).toBe('?')
})
