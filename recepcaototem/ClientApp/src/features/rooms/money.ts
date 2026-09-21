export const ROOM_RATE_MAXIMUM = 9999999999999.99

const ungroupedRate = /^(?:0|[1-9]\d{0,12})(?:,\d{1,2})?$/
const groupedRate = /^[1-9]\d{0,2}(?:\.\d{3})+(?:,\d{1,2})?$/

export function parseRoomRate(input: string): number | null {
  const value = input.trim()
  if (!ungroupedRate.test(value) && !groupedRate.test(value)) return null
  const parsed = Number(value.replaceAll('.', '').replace(',', '.'))
  return Number.isFinite(parsed) && parsed >= 0 && parsed <= ROOM_RATE_MAXIMUM ? parsed : null
}

const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })

export function formatBrl(value: number) {
  return brl.format(value)
}

const brlWhole = new Intl.NumberFormat('pt-BR', {
  style: 'currency', currency: 'BRL', maximumFractionDigits: 0,
})

/**
 * The catalogue-card form: "R$ 3.100" for a round rent, "R$ 3.100,50" when the cents are
 * real. A card has room for three chips on one line only without the ",00" that every
 * whole rent carries, and dropping cents that are actually there would misquote a price.
 */
export function formatBrlCompact(value: number) {
  return Number.isInteger(value) ? brlWhole.format(value) : brl.format(value)
}
