import { expect, test } from 'vitest'
import { amenityLabel, areaLabel, bathroomLabel, capacityChip, capacityLabel, categoryLabel } from './roomFeatures'

test('a missing value produces no label at all, never a zero', () => {
  expect(categoryLabel(null)).toBeNull()
  expect(capacityLabel(null, null)).toBeNull()
  expect(capacityChip(null, 8)).toBeNull()
  expect(areaLabel(null)).toBeNull()
  expect(bathroomLabel(null)).toBeNull()
})

test('a capacity reads as a range, or as one number when it is fixed', () => {
  expect(capacityLabel(4, 8)).toBe('4 a 8 pessoas')
  expect(capacityLabel(4, 4)).toBe('4 pessoas')
  expect(capacityLabel(4, null)).toBe('4 pessoas')
  expect(capacityChip(4, 8)).toBe('4p-8p')
  expect(capacityChip(4, 4)).toBe('4p')
})

test('an area drops a pointless decimal but keeps a real one', () => {
  expect(areaLabel(25)).toBe('25m²')
  expect(areaLabel(25.5)).toBe('25,5m²')
})

test('bathrooms agree in number, and none is a statement rather than a blank', () => {
  expect(bathroomLabel(0)).toBe('Sem banheiro')
  expect(bathroomLabel(1)).toBe('1 Banheiro')
  expect(bathroomLabel(2)).toBe('2 Banheiros')
})

test('codes become the words the building uses', () => {
  expect(categoryLabel('CONSULTORIO')).toBe('Consultório')
  expect(amenityLabel('CLIMATIZADA')).toBe('Climatizada')
  expect(amenityLabel('WIFI')).toBe('Wi-Fi')
})

// A backend that gains a code this build has not shipped yet must not blank the chip or
// throw — the raw code is ugly but honest.
test('an unknown code falls back to itself', () => {
  expect(categoryLabel('SAUNA' as never)).toBe('SAUNA')
  expect(amenityLabel('SAUNA' as never)).toBe('SAUNA')
})
