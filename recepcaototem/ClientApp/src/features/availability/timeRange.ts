export const HHMM_RE = /^([01]\d|2[0-3]):[0-5]\d$/

export type Period = { start: string, end: string }

export function parseHm(value: string): number | null {
  if (!HHMM_RE.test(value)) return null
  const [hours, minutes] = value.split(':').map(Number)
  return (hours * 60) + minutes
}

export function rangeError({ start, end }: Period): boolean {
  const from = parseHm(start)
  const to = parseHm(end)
  return from === null || to === null || from >= to
}

export function overlappingIndexes(periods: Period[]): Set<number> {
  const rows = periods
    .map((period, index) => ({ index, start: parseHm(period.start), end: parseHm(period.end) }))
    .filter((row): row is { index: number, start: number, end: number } =>
      row.start !== null && row.end !== null && row.start < row.end)
    .sort((a, b) => a.start - b.start)
  const hit = new Set<number>()
  for (let position = 1; position < rows.length; position += 1) {
    if (rows[position].start < rows[position - 1].end) {
      hit.add(rows[position].index)
      hit.add(rows[position - 1].index)
    }
  }
  return hit
}

export function periodsHaveErrors(periods: Period[]): boolean {
  return periods.some(rangeError) || overlappingIndexes(periods).size > 0
}
