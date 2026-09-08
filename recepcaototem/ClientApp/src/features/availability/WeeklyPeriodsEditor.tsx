import { Plus, Trash2 } from 'lucide-react'
import { TimeField } from './TimeField'
import { overlappingIndexes, rangeError } from './timeRange'

export type PeriodDraft = { id: string, start: string, end: string }
export type DayDraft = { dayOfWeek: string, open: boolean, periods: PeriodDraft[] }

let fallbackId = 0
export function newPeriod(start: string, end: string): PeriodDraft {
  const id = typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `p${(fallbackId += 1)}`
  return { id, start, end }
}

type WeeklyPeriodsEditorProps = {
  days: DayDraft[]
  disabled?: boolean
  startLabel: string
  endLabel: string
  closedLabel: string
  addPeriodLabel?: string
  defaultPeriod: { start: string, end: string }
  dayLabel: (dayOfWeek: string) => string
  hintForDay?: (dayOfWeek: string) => string | null
  gridRangeForDay?: (dayOfWeek: string) => { min: string, max: string } | null
  onChange: (days: DayDraft[]) => void
}

export function WeeklyPeriodsEditor({
  days, disabled = false, startLabel, endLabel, closedLabel,
  addPeriodLabel = 'Adicionar período', defaultPeriod, dayLabel, hintForDay, gridRangeForDay, onChange,
}: WeeklyPeriodsEditorProps) {
  const patchDay = (dayOfWeek: string, patch: (day: DayDraft) => DayDraft) =>
    onChange(days.map((day) => (day.dayOfWeek === dayOfWeek ? patch(day) : day)))

  const toggleDay = (dayOfWeek: string) => patchDay(dayOfWeek, (day) => day.open
    ? { ...day, open: false, periods: [] }
    : { ...day, open: true, periods: [newPeriod(defaultPeriod.start, defaultPeriod.end)] })

  const addPeriod = (dayOfWeek: string) => patchDay(dayOfWeek, (day) => ({
    ...day, periods: [...day.periods, newPeriod(defaultPeriod.start, defaultPeriod.end)],
  }))

  const removePeriod = (dayOfWeek: string, id: string) => patchDay(dayOfWeek, (day) => ({
    ...day, periods: day.periods.filter((period) => period.id !== id),
  }))

  const setField = (dayOfWeek: string, id: string, field: 'start' | 'end', value: string) =>
    patchDay(dayOfWeek, (day) => ({
      ...day,
      periods: day.periods.map((period) => (period.id === id ? { ...period, [field]: value } : period)),
    }))

  return <div className="wpe-days">
    {days.map((day) => {
      const hint = hintForDay?.(day.dayOfWeek) ?? null
      const gridRange = gridRangeForDay?.(day.dayOfWeek) ?? null
      const overlaps = overlappingIndexes(day.periods)
      return <div className="wpe-day" data-testid={`wpe-day-${day.dayOfWeek}`} key={day.dayOfWeek}>
        <div className="wpe-day-head">
          <div>
            <strong>{dayLabel(day.dayOfWeek)}</strong>
            {hint && <div className="wpe-hint">{hint}</div>}
          </div>
          <button
            type="button"
            role="switch"
            aria-checked={day.open}
            aria-label={`${day.open ? 'Fechar' : 'Abrir'} ${dayLabel(day.dayOfWeek)}`}
            className={`wpe-switch ${day.open ? 'is-on' : ''}`}
            disabled={disabled}
            onClick={() => toggleDay(day.dayOfWeek)}
          >
            <span className="wpe-switch-track" aria-hidden="true"><i /></span>
          </button>
        </div>
        {day.open ? <div className="wpe-periods">
          {day.periods.map((period, index) => {
            const invalid = rangeError(period)
            const overlapping = overlaps.has(index)
            return <div className="wpe-period-row" key={period.id}>
              <label>{startLabel}<TimeField
                label={startLabel} value={period.start} disabled={disabled}
                min={gridRange?.min} max={gridRange?.max}
                onChange={(next) => setField(day.dayOfWeek, period.id, 'start', next)}
              /></label>
              <span className="wpe-dash">–</span>
              <label>{endLabel}<TimeField
                label={endLabel} value={period.end} disabled={disabled}
                min={gridRange?.min} max={gridRange?.max}
                onChange={(next) => setField(day.dayOfWeek, period.id, 'end', next)}
              /></label>
              <button
                type="button" className="icon-button"
                aria-label={`Remover período de ${dayLabel(day.dayOfWeek)}`}
                disabled={disabled || day.periods.length <= 1}
                onClick={() => removePeriod(day.dayOfWeek, period.id)}
              ><Trash2 size={15} /></button>
              {invalid && <small className="wpe-period-error">O início deve ser anterior ao fim.</small>}
              {!invalid && overlapping && <small className="wpe-period-error">Os períodos não podem se sobrepor.</small>}
            </div>
          })}
          <button type="button" className="wpe-add" disabled={disabled} onClick={() => addPeriod(day.dayOfWeek)}>
            <Plus size={14} /> {addPeriodLabel}
          </button>
        </div> : <span className="wpe-closed">{closedLabel}</span>}
      </div>
    })}
  </div>
}
