import { expect, test } from 'vitest'
import { normalizeToken } from './normalizeToken'

test('normalizeToken handles plain codes, whitespace and URLs', () => {
  expect(normalizeToken('  ABC-123  ')).toBe('ABC-123')
  expect(normalizeToken('https://lumis.app/totem/check-in?token=XYZ789')).toBe('XYZ789')
  expect(normalizeToken('https://lumis.app/c/QWE-456')).toBe('QWE-456')
  expect(normalizeToken('lumis://check-in?code=Z1')).toBe('Z1')
  expect(normalizeToken('​TOKEN\n')).toBe('TOKEN')
  expect(normalizeToken('not a url just text')).toBe('not a url just text')
})
