// Brazilian WhatsApp / phone input helpers.
//
// The backend is the source of truth: `WhatsAppNormalizer` accepts a national number
// of 10 (landline) or 11 (mobile) digits — digits-only or a formatted `(DD) NNNNN-NNNN`
// string — and persists it as E.164 (`+55…`). The form keeps ONLY the digits as its
// value; the mask below is presentation, never a domain value, and we send the bare
// digits to the API.

const MAX_DIGITS = 11

/** Keep only 0-9, capped at the 11-digit domain maximum. */
export function whatsAppDigits(value: string): string {
  return value.replace(/\D/g, '').slice(0, MAX_DIGITS)
}

/** Progressive Brazilian mask: `(DD) NNNN-NNNN` (10) or `(DD) NNNNN-NNNN` (11). */
export function formatBrazilWhatsApp(value: string): string {
  const d = whatsAppDigits(value)
  if (d.length === 0) return ''
  if (d.length <= 2) return `(${d}`
  if (d.length <= 6) return `(${d.slice(0, 2)}) ${d.slice(2)}`
  if (d.length <= 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`
  return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`
}

/** True when the number has a valid digit count for the domain (DDD + 8 or 9). */
export function isCompleteWhatsApp(value: string): boolean {
  const n = whatsAppDigits(value).length
  return n === 10 || n === 11
}

/**
 * Caret position, in characters, that keeps the same number of digits to its left
 * after the value is reformatted. Lets the field reformat on every keystroke without
 * the cursor jumping to the end when editing in the middle.
 */
export function caretAfterFormat(digitsBeforeCaret: number, formatted: string): number {
  let pos = 0
  let seen = 0
  while (pos < formatted.length && seen < digitsBeforeCaret) {
    if (/\d/.test(formatted[pos])) seen++
    pos++
  }
  return pos
}

/** Display a stored E.164 Brazilian number (`+5569999999999`) as `(69) 99999-9999`. */
export function displayWhatsApp(value: string): string {
  const brazil = value.match(/^\+55(\d{2})(\d{4,5})(\d{4})$/)
  return brazil ? `(${brazil[1]}) ${brazil[2]}-${brazil[3]}` : value
}
