import type { AvailabilityDayDto } from '../../api/modules'

export const AVAILABILITY_DAYS = [
  { value: 'MONDAY', label: 'Segunda-feira' },
  { value: 'TUESDAY', label: 'Terça-feira' },
  { value: 'WEDNESDAY', label: 'Quarta-feira' },
  { value: 'THURSDAY', label: 'Quinta-feira' },
  { value: 'FRIDAY', label: 'Sexta-feira' },
  { value: 'SATURDAY', label: 'Sábado' },
  { value: 'SUNDAY', label: 'Domingo' },
] as const

export function toTimeInputValue(value: string | null | undefined) {
  return value ? value.slice(0, 5) : ''
}

export function normalizeAvailabilityDays(input: AvailabilityDayDto[] | undefined | null) {
  return AVAILABILITY_DAYS.map(({ value }) => {
    const day = input?.find((candidate) => candidate.dayOfWeek.toUpperCase() === value)
    return {
      dayOfWeek: value,
      intervals: (day?.intervals ?? []).map((interval) => ({
        startTime: toTimeInputValue(interval.startTime),
        endTime: toTimeInputValue(interval.endTime),
      })),
    }
  })
}

export function findDayLabel(dayOfWeek: string) {
  return AVAILABILITY_DAYS.find((day) => day.value === dayOfWeek.toUpperCase())?.label ?? dayOfWeek
}
