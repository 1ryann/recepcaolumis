// When a room frees up. The backend sends a bare "YYYY-MM-DD" — the civil day after the
// current contract's occupancy ends — and deliberately no time of day: a contract ends on
// a date, and promising "amanhã às 09:00" to a visitor would be inventing a precision that
// hand-over, cleaning and inspection do not have.
//
// "YYYY-MM-DD" is never fed to `new Date`, which would read it as UTC midnight and can
// roll to the previous day once converted to local time. Dates are compared as strings,
// which for this format is the same as comparing the days.

export const BUILDING_TIME_ZONE = 'America/Porto_Velho'

/** Today in the building's timezone, as "YYYY-MM-DD" — the kiosk and a visitor's phone
 *  in another state must agree on what "hoje" means. */
export function buildingToday(now: Date = new Date()): string {
  // en-CA formats as YYYY-MM-DD, which is what the wire format already is.
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: BUILDING_TIME_ZONE, year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(now)
}

function addDays(isoDate: string, days: number): string {
  const [year, month, day] = isoDate.split('-').map(Number)
  const shifted = new Date(Date.UTC(year, month - 1, day + days))
  return shifted.toISOString().slice(0, 10)
}

export function dateLabel(isoDate: string): string {
  const [year, month, day] = isoDate.split('-')
  return `${day}/${month}/${year}`
}

/** "hoje", "amanhã", or "em 22/09/2026". */
export function availabilityLabel(isoDate: string, today: string = buildingToday()): string {
  if (isoDate <= today) return 'hoje'
  if (isoDate === addDays(today, 1)) return 'amanhã'
  return `em ${dateLabel(isoDate)}`
}
