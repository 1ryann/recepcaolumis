// Shared timezone-aware date helpers for the professional area pages.
//
// The professional area always reasons about "today"/day boundaries in the
// building's own timezone (America/Porto_Velho), regardless of the
// professional's browser locale/timezone. All conversions here are derived
// from the named zone rather than a hardcoded UTC offset, so they stay
// correct even if the zone's offset ever changes.

export const PROFESSIONAL_TIME_ZONE = 'America/Porto_Velho'

/**
 * Returns the calendar date (YYYY-MM-DD) that `date` falls on in `zone`.
 *
 * Uses the 'sv-SE' locale, which formats dates in ISO order (YYYY-MM-DD HH:mm:ss),
 * so the zone's wall-clock date can be read directly with no further conversion.
 * This is the correct idiom: `new Date(date.toLocaleString(...))` re-parses the
 * zone's wall-clock text in the BROWSER's own timezone and shifts it again,
 * which silently produces the wrong calendar day near midnight.
 */
export function zoneDateKey(date: Date, zone: string = PROFESSIONAL_TIME_ZONE): string {
  return date.toLocaleString('sv-SE', { timeZone: zone }).slice(0, 10)
}

/** Returns today's calendar date (YYYY-MM-DD) in `zone`. */
export function todayZoneDateKey(zone: string = PROFESSIONAL_TIME_ZONE): string {
  return zoneDateKey(new Date(), zone)
}

function zoneOffsetMinutes(instant: Date, zone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: zone, hourCycle: 'h23',
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).formatToParts(instant)
  const map: Record<string, string> = {}
  for (const part of parts) if (part.type !== 'literal') map[part.type] = part.value
  const asUtc = Date.UTC(
    Number(map.year), Number(map.month) - 1, Number(map.day),
    Number(map.hour), Number(map.minute), Number(map.second),
  )
  return Math.round((asUtc - instant.getTime()) / 60000)
}

function formatOffset(minutes: number): string {
  const sign = minutes <= 0 ? '-' : '+'
  const abs = Math.abs(minutes)
  const hh = String(Math.floor(abs / 60)).padStart(2, '0')
  const mm = String(abs % 60).padStart(2, '0')
  return `${sign}${hh}:${mm}`
}

/** Returns the UTC offset (e.g. "-04:00") that `zone` observes at `instant`. */
export function zoneOffsetForInstant(instant: Date, zone: string = PROFESSIONAL_TIME_ZONE): string {
  return formatOffset(zoneOffsetMinutes(instant, zone))
}

/**
 * Converts a `datetime-local`-style wall-clock string (assumed to represent
 * `zone`'s local time) into a UTC ISO string.
 */
export function zoneLocalToIso(value: string, zone: string = PROFESSIONAL_TIME_ZONE): string {
  const normalized = value.length === 16 ? `${value}:00` : value
  const approx = new Date(`${normalized}Z`)
  const offset = zoneOffsetForInstant(approx, zone)
  return new Date(`${normalized}${offset}`).toISOString()
}

/**
 * Returns the UTC ISO instant for the start (or, if `end`, the last second)
 * of the given `dateKey` (YYYY-MM-DD) as observed in `zone`.
 */
export function zoneDayBoundary(dateKey: string, end: boolean, zone: string = PROFESSIONAL_TIME_ZONE): string {
  const approx = new Date(`${dateKey}T00:00:00Z`)
  const offset = zoneOffsetForInstant(approx, zone)
  return new Date(`${dateKey}T${end ? '23:59:59' : '00:00:00'}${offset}`).toISOString()
}
