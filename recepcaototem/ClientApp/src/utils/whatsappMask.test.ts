import { expect, test } from 'vitest'
import { displayWhatsApp, formatBrazilWhatsApp, isCompleteWhatsApp, whatsAppDigits } from './whatsappMask'

test('formats 11 typed digits as (DD) NNNNN-NNNN', () => {
  expect(formatBrazilWhatsApp('69993182032')).toBe('(69) 99318-2032')
})

test('formats 10 digits as (DD) NNNN-NNNN', () => {
  expect(formatBrazilWhatsApp('6933182032')).toBe('(69) 3318-2032')
})

test('strips every non-digit character', () => {
  expect(whatsAppDigits('a6b9c!9 9-3_1(8)2032')).toBe('69993182032')
  expect(formatBrazilWhatsApp('(69) 99318-2032')).toBe('(69) 99318-2032')
})

test('accepts pasted formatted values and spaced values', () => {
  expect(whatsAppDigits('(69) 99318-2032')).toBe('69993182032')
  expect(whatsAppDigits('69 99318 2032')).toBe('69993182032')
  expect(whatsAppDigits('69993182032')).toBe('69993182032')
})

test('caps at 11 digits (the domain maximum)', () => {
  expect(whatsAppDigits('699931820329999')).toBe('69993182032')
  expect(formatBrazilWhatsApp('699931820329999')).toBe('(69) 99318-2032')
})

test('shows a partial mask while typing without crashing', () => {
  expect(formatBrazilWhatsApp('')).toBe('')
  expect(formatBrazilWhatsApp('6')).toBe('(6')
  expect(formatBrazilWhatsApp('69')).toBe('(69')
  expect(formatBrazilWhatsApp('6993')).toBe('(69) 93')
  expect(formatBrazilWhatsApp('699318')).toBe('(69) 9318')
})

test('isCompleteWhatsApp is true only for 10 or 11 digits', () => {
  expect(isCompleteWhatsApp('699931')).toBe(false)
  expect(isCompleteWhatsApp('699318203')).toBe(false)
  expect(isCompleteWhatsApp('6993182032')).toBe(true)
  expect(isCompleteWhatsApp('69993182032')).toBe(true)
  expect(isCompleteWhatsApp('(69) 99318-2032')).toBe(true)
  expect(isCompleteWhatsApp('')).toBe(false)
})

test('the placeholder of a deleted customer is not shown as a phone number', () => {
  expect(displayWhatsApp('DEL9f3e1a2b4c5d6')).toBe('—')
  expect(displayWhatsApp('+5569999538007')).toBe('(69) 99953-8007')
})
