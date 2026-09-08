import { Save } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import type { OperatingHoursDayDto, OperatingHoursDto } from '../../api/modules'
import { AVAILABILITY_DAYS, findDayLabel, toTimeInputValue } from './availabilityFormat'
import { periodsHaveErrors } from './timeRange'
import { type DayDraft, newPeriod, WeeklyPeriodsEditor } from './WeeklyPeriodsEditor'

const DEFAULT_PERIOD = { start: '08:30', end: '18:30' }

type OperatingHoursEditorProps = {
  value: OperatingHoursDto
  pending: boolean
  onSave: (days: OperatingHoursDayDto[]) => void | Promise<void>
}

function seedDays(value: OperatingHoursDto): DayDraft[] {
  return AVAILABILITY_DAYS.map(({ value: dayOfWeek }) => {
    if (!value.configured) {
      return { dayOfWeek, open: true, periods: [newPeriod(DEFAULT_PERIOD.start, DEFAULT_PERIOD.end)] }
    }
    const intervals = value.days.find((day) => day.dayOfWeek.toUpperCase() === dayOfWeek)?.intervals ?? []
    return {
      dayOfWeek,
      open: intervals.length > 0,
      periods: intervals.map((interval) => newPeriod(toTimeInputValue(interval.opensAt), toTimeInputValue(interval.closesAt))),
    }
  })
}

const signature = (days: DayDraft[]) => JSON.stringify(
  days.map((day) => ({ d: day.dayOfWeek, o: day.open, p: day.periods.map((period) => [period.start, period.end]) })),
)

function toPayload(days: DayDraft[]): OperatingHoursDayDto[] {
  return AVAILABILITY_DAYS.map(({ value: dayOfWeek }) => {
    const day = days.find((candidate) => candidate.dayOfWeek === dayOfWeek)
    return {
      dayOfWeek,
      intervals: day?.open
        ? day.periods.map((period) => ({ opensAt: period.start, closesAt: period.end }))
        : [],
    }
  })
}

export function OperatingHoursEditor(props: OperatingHoursEditorProps) {
  return <OperatingHoursEditorInner key={props.value.concurrencyToken ?? 'new'} {...props} />
}

function OperatingHoursEditorInner({ value, pending, onSave }: OperatingHoursEditorProps) {
  const seed = useMemo(() => seedDays(value), [value])
  const seedSignature = useMemo(() => signature(seed), [seed])
  const [days, setDays] = useState<DayDraft[]>(seed)
  const [bulkStart, setBulkStart] = useState(DEFAULT_PERIOD.start)
  const [bulkEnd, setBulkEnd] = useState(DEFAULT_PERIOD.end)

  const dirty = signature(days) !== seedSignature
  const hasErrors = days.some((day) => day.open && periodsHaveErrors(day.periods))

  useEffect(() => {
    if (!dirty) return
    const guard = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = '' }
    window.addEventListener('beforeunload', guard)
    return () => window.removeEventListener('beforeunload', guard)
  }, [dirty])

  const applyToAll = () => {
    const wouldReplaceMultiple = days.some((day) => day.open && day.periods.length > 1)
    if (wouldReplaceMultiple && !window.confirm('Isso substitui os períodos de todos os dias abertos por um único período. Continuar?')) return
    setDays((current) => current.map((day) => day.open
      ? { ...day, periods: [newPeriod(bulkStart, bulkEnd)] }
      : day))
  }

  const save = () => {
    if (pending || !dirty || hasErrors) return
    void onSave(toPayload(days))
  }

  return <div className="operating-hours-editor">
    {!value.configured && <div className="wpe-suggestion" role="status">
      O horário de funcionamento ainda não foi configurado — esta sugestão de 08:30–18:30 só é salva quando você confirmar.
    </div>}
    <div className="oh-bulk-bar">
      <div className="oh-bulk-copy"><strong>Aplicar um horário único</strong><span>Preenche todos os dias abertos de uma vez.</span></div>
      <div className="oh-bulk-fields">
        <input className="time-input" type="time" step={60} aria-label="Aplicar abertura"
          value={bulkStart} onChange={(event) => setBulkStart(event.target.value)} />
        <span className="wpe-dash">–</span>
        <input className="time-input" type="time" step={60} aria-label="Aplicar fechamento"
          value={bulkEnd} onChange={(event) => setBulkEnd(event.target.value)} />
        <button type="button" className="secondary-button" disabled={pending} onClick={applyToAll}>
          Aplicar a todos os dias
        </button>
      </div>
    </div>
    <WeeklyPeriodsEditor
      days={days}
      disabled={pending}
      startLabel="Abertura"
      endLabel="Fechamento"
      closedLabel="Fechado"
      defaultPeriod={DEFAULT_PERIOD}
      dayLabel={findDayLabel}
      onChange={setDays}
    />
    <div className="oh-save-bar">
      <span>{dirty
        ? <button type="button" className="wpe-add" onClick={() => setDays(seed)}>Descartar alterações</button>
        : hasErrors ? 'Revise os períodos destacados antes de salvar.' : null}</span>
      <button type="button" className="primary-button" disabled={pending || !dirty || hasErrors} onClick={save}>
        <Save size={17} /> {pending ? 'Salvando…' : 'Salvar horário'}
      </button>
    </div>
  </div>
}
