import { expect, test } from 'vitest'
import { overlappingIndexes, parseHm, periodsHaveErrors, rangeError } from './timeRange'

test('parseHm accepts HH:mm and rejects anything else', () => {
  expect(parseHm('08:30')).toBe(510)
  expect(parseHm('00:00')).toBe(0)
  expect(parseHm('23:59')).toBe(1439)
  expect(parseHm('8:30')).toBeNull()
  expect(parseHm('08:30:00')).toBeNull()
  expect(parseHm('')).toBeNull()
  expect(parseHm('24:00')).toBeNull()
})

test('rangeError flags empty, unparseable and inverted ranges', () => {
  expect(rangeError({ start: '09:00', end: '12:00' })).toBe(false)
  expect(rangeError({ start: '12:00', end: '12:00' })).toBe(true)
  expect(rangeError({ start: '13:00', end: '12:00' })).toBe(true)
  expect(rangeError({ start: '', end: '12:00' })).toBe(true)
})

test('overlappingIndexes finds every period that overlaps another', () => {
  const periods = [
    { start: '09:00', end: '12:00' },
    { start: '11:30', end: '13:00' },
    { start: '14:00', end: '15:00' },
  ]
  expect([...overlappingIndexes(periods)].sort()).toEqual([0, 1])
  expect([...overlappingIndexes([{ start: '09:00', end: '10:00' }, { start: '10:00', end: '11:00' }])]).toEqual([])
})

test('periodsHaveErrors combines range and overlap checks', () => {
  expect(periodsHaveErrors([{ start: '09:00', end: '12:00' }])).toBe(false)
  expect(periodsHaveErrors([{ start: '09:00', end: '12:00' }, { start: '11:00', end: '13:00' }])).toBe(true)
  expect(periodsHaveErrors([{ start: '12:00', end: '09:00' }])).toBe(true)
})
