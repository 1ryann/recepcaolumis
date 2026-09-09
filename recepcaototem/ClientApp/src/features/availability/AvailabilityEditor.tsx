import { AlertTriangle, CalendarClock, Check, Clock3, Plus } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import type { AvailabilityDayDto, AvailabilityExceptionDto, ProfessionalAvailabilityDto } from '../../api/modules'
import { findDayLabel, normalizeAvailabilityDays, toTimeInputValue } from './availabilityFormat'
import { periodsHaveErrors } from './timeRange'
import { type DayDraft, newPeriod, WeeklyPeriodsEditor } from './WeeklyPeriodsEditor'

export type AvailabilityDraft = {
  mode: 'INHERIT_GLOBAL' | 'CUSTOM'
  days: { dayOfWeek: string, intervals: { startTime: string, endTime: string }[] }[]
}

type AvailabilityEditorProps = {
  value: ProfessionalAvailabilityDto
  draft?: AvailabilityDraft
  pending: boolean
  readOnly?: boolean
  onChange: (value: AvailabilityDraft) => void
  onSave: (value: AvailabilityDraft) => void | Promise<void>
}

function toDraft(value: ProfessionalAvailabilityDto): AvailabilityDraft {
  return { mode: value.mode, days: normalizeAvailabilityDays(value.days) }
}

function seedDayDrafts(draft: AvailabilityDraft): DayDraft[] {
  return draft.days.map((day) => ({
    dayOfWeek: day.dayOfWeek,
    open: day.intervals.length > 0,
    periods: day.intervals.map((interval) => newPeriod(toTimeInputValue(interval.startTime), toTimeInputValue(interval.endTime))),
  }))
}

function daysToDraft(mode: AvailabilityDraft['mode'], days: DayDraft[]): AvailabilityDraft {
  return {
    mode,
    days: days.map((day) => ({
      dayOfWeek: day.dayOfWeek,
      intervals: day.open ? day.periods.map((period) => ({ startTime: period.start, endTime: period.end })) : [],
    })),
  }
}

// Remount (fresh local state) whenever the server record changes — a save or a
// conflict reload — while keeping identity stable across a background revalidation.
export function AvailabilityEditor(props: AvailabilityEditorProps) {
  return <AvailabilityEditorInner key={props.value.concurrencyToken} {...props} />
}

function AvailabilityEditorInner({ value, draft: draftValue, pending, readOnly = false, onChange, onSave }: AvailabilityEditorProps) {
  const initial = draftValue ?? toDraft(value)
  const [mode, setMode] = useState<AvailabilityDraft['mode']>(initial.mode)
  const [days, setDays] = useState<DayDraft[]>(() => seedDayDrafts(initial))

  const pushUp = (nextMode: AvailabilityDraft['mode'], nextDays: DayDraft[]) => {
    if (!readOnly) onChange(daysToDraft(nextMode, nextDays))
  }
  const handleDays = (nextDays: DayDraft[]) => { setDays(nextDays); pushUp(mode, nextDays) }
  const changeMode = (nextMode: AvailabilityDraft['mode']) => { setMode(nextMode); pushUp(nextMode, days) }

  const hasErrors = days.some((day) => day.open && periodsHaveErrors(day.periods))
  const modeDescription = mode === 'INHERIT_GLOBAL'
    ? 'Você está utilizando o horário de funcionamento do estabelecimento.'
    : 'Sua agenda personalizada será cruzada com o horário do estabelecimento.'

  const effectiveDays = normalizeAvailabilityDays(value.effectiveDays)
  // The establishment's raw operating hours per weekday — a fixed reference for the
  // "Estabelecimento:" hint and the time-grid bounds. Never the professional's own
  // (clamped) schedule: that is effectiveDays.
  const globalDays = normalizeAvailabilityDays(value.globalDays)
  const buildingWindow = (dayOfWeek: string) => globalDays.find((candidate) => candidate.dayOfWeek === dayOfWeek)
  const hintForDay = (dayOfWeek: string) => {
    const day = buildingWindow(dayOfWeek)
    if (!day || day.intervals.length === 0) return 'Estabelecimento: sem atendimento'
    return `Estabelecimento: ${day.intervals.map((interval) => `${toTimeInputValue(interval.startTime)}–${toTimeInputValue(interval.endTime)}`).join(' · ')}`
  }
  const gridRangeForDay = (dayOfWeek: string) => {
    const day = buildingWindow(dayOfWeek)
    if (!day || day.intervals.length === 0) return null
    const starts = day.intervals.map((interval) => toTimeInputValue(interval.startTime)).sort()
    const ends = day.intervals.map((interval) => toTimeInputValue(interval.endTime)).sort()
    return { min: starts[0], max: ends[ends.length - 1] }
  }

  return <section className="availability-editor panel">
    <div className="availability-editor-heading"><div><span className="eyebrow">Agenda semanal</span><h2>Minha disponibilidade</h2><p>{modeDescription}</p></div><CalendarClock size={23} /></div>
    <fieldset className="availability-mode" disabled={readOnly || pending}>
      <legend>Modo de disponibilidade</legend>
      <label><input type="radio" name="availability-mode" value="INHERIT_GLOBAL" checked={mode === 'INHERIT_GLOBAL'} onChange={() => changeMode('INHERIT_GLOBAL')} /> Usar horário do estabelecimento</label>
      <label><input type="radio" name="availability-mode" value="CUSTOM" checked={mode === 'CUSTOM'} onChange={() => changeMode('CUSTOM')} /> Usar horário personalizado</label>
    </fieldset>
    {mode === 'INHERIT_GLOBAL' && <div className="availability-effective"><div className="availability-subheading"><div><strong>Agenda efetiva</strong><span>O horário do estabelecimento vale enquanto este modo estiver ativo.</span></div><Clock3 size={18} /></div><div className="availability-day-list">{effectiveDays.map((day) => <div className="availability-day-row" key={`effective-${day.dayOfWeek}`}><strong>{findDayLabel(day.dayOfWeek)}</strong><span>{day.intervals.length ? day.intervals.map((interval) => `${toTimeInputValue(interval.startTime)} – ${toTimeInputValue(interval.endTime)}`).join(' · ') : 'Não atende'}</span></div>)}</div><p className="availability-preserved">Os horários personalizados continuam preservados e reaparecem quando você voltar ao modo personalizado.</p></div>}
    <div className="availability-days"><div className="availability-subheading"><div><strong>{mode === 'CUSTOM' ? 'Agenda personalizada' : 'Horários personalizados armazenados'}</strong><span>{mode === 'CUSTOM' ? 'Defina quando você recebe novos atendimentos. A referência do estabelecimento aparece em cada dia.' : 'Inativos neste modo, sem apagar sua configuração.'}</span></div></div>
      <WeeklyPeriodsEditor
        days={days}
        disabled={readOnly || pending || mode !== 'CUSTOM'}
        startLabel="Início"
        endLabel="Fim"
        closedLabel="Não atende"
        defaultPeriod={{ start: '09:00', end: '17:00' }}
        dayLabel={findDayLabel}
        hintForDay={mode === 'CUSTOM' ? hintForDay : undefined}
        gridRangeForDay={mode === 'CUSTOM' ? gridRangeForDay : undefined}
        onChange={handleDays}
      />
    </div>
    {value.existingReservationsOutsideAvailabilityCount > 0 && <div className="availability-warning" role="status"><AlertTriangle size={17} /><span>Você possui {value.existingReservationsOutsideAvailabilityCount} agendamento(s) já existente(s) fora da nova disponibilidade. Esses agendamentos foram mantidos.</span></div>}
    {!readOnly && <div className="availability-editor-actions"><span>{pending ? 'Salvando…' : hasErrors ? 'Revise os períodos antes de salvar.' : ''}</span><button className="primary-button" type="button" disabled={pending || hasErrors} onClick={() => void onSave(daysToDraft(mode, days))}>{pending ? 'Salvando…' : 'Salvar disponibilidade'} <Check size={16} /></button></div>}
  </section>
}

type ExceptionDraft = { date: string, allDay: boolean, startTime: string | null, endTime: string | null, reason: string }
type ExceptionsEditorProps = { exceptions: AvailabilityExceptionDto[], pending: boolean, onCreate: (input: ExceptionDraft) => void | Promise<void>, onUpdate: (id: string, input: ExceptionDraft & { concurrencyToken: string }) => void | Promise<void>, onDelete: (id: string, concurrencyToken: string) => void | Promise<void> }

const emptyException: ExceptionDraft = { date: '', allDay: true, startTime: null, endTime: null, reason: '' }

export function ExceptionsEditor({ exceptions, pending, onCreate, onUpdate, onDelete }: ExceptionsEditorProps) {
  const [editing, setEditing] = useState<{ id: string | null, token?: string, value: ExceptionDraft }>({ id: null, value: emptyException })
  const [open, setOpen] = useState(false)
  useEffect(() => { if (!open) setEditing({ id: null, value: emptyException }) }, [open])
  const sorted = useMemo(() => [...exceptions].sort((a, b) => a.date.localeCompare(b.date)), [exceptions])
  const submit = async () => {
    if (!editing.value.date) return
    // Keep the editor contract as a string.  The API adapter is responsible for
    // converting an empty optional reason to null; passing null here would make
    // callers that trim the value throw before the request is sent.
    const input: ExceptionDraft = { ...editing.value, reason: editing.value.reason.trim(), startTime: editing.value.allDay ? null : editing.value.startTime, endTime: editing.value.allDay ? null : editing.value.endTime }
    if (editing.id && editing.token) await onUpdate(editing.id, { ...input, concurrencyToken: editing.token })
    else await onCreate(input)
    setOpen(false)
  }
  return <section className="exceptions-editor panel"><div className="availability-subheading"><div><strong>Indisponibilidades</strong><span>Reduza sua agenda em dias ou períodos específicos.</span></div><button className="secondary-button" type="button" onClick={() => { setEditing({ id: null, value: emptyException }); setOpen(true) }}><Plus size={16} /> Adicionar indisponibilidade</button></div>{sorted.length === 0 ? <div className="availability-empty">Nenhuma indisponibilidade cadastrada.</div> : <div className="exception-list">{sorted.map((exception) => <article className="exception-row" key={exception.id}><div className="exception-date"><strong>{new Date(`${exception.date}T12:00:00`).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })}</strong><span>{exception.allDay ? 'Dia inteiro' : `${toTimeInputValue(exception.startTime)} – ${toTimeInputValue(exception.endTime)}`}</span></div><div className="exception-reason">{exception.reason || 'Indisponibilidade sem motivo'}</div><div className="row-actions"><button type="button" onClick={() => { setEditing({ id: exception.id, token: exception.concurrencyToken, value: { date: exception.date, allDay: exception.allDay, startTime: exception.startTime ? toTimeInputValue(exception.startTime) : null, endTime: exception.endTime ? toTimeInputValue(exception.endTime) : null, reason: exception.reason ?? '' } }); setOpen(true) }}>Editar</button><button type="button" disabled={pending} onClick={() => void onDelete(exception.id, exception.concurrencyToken)}>Remover</button></div></article>)}</div>}{open && <div className="availability-exception-form"><div className="availability-form-title"><strong>{editing.id ? 'Editar indisponibilidade' : 'Nova indisponibilidade'}</strong><button className="icon-button" type="button" aria-label="Fechar" onClick={() => setOpen(false)}>×</button></div><label className="field-label">Data<input className="field-input" type="date" value={editing.value.date} onChange={(event) => setEditing((current) => ({ ...current, value: { ...current.value, date: event.target.value } }))} /></label><fieldset className="availability-mode"><legend>Tipo</legend><label><input type="radio" name="exception-type" checked={editing.value.allDay} onChange={() => setEditing((current) => ({ ...current, value: { ...current.value, allDay: true, startTime: null, endTime: null } }))} /> Dia inteiro</label><label><input type="radio" name="exception-type" checked={!editing.value.allDay} onChange={() => setEditing((current) => ({ ...current, value: { ...current.value, allDay: false, startTime: current.value.startTime ?? '08:00', endTime: current.value.endTime ?? '09:00' } }))} /> Horário específico</label></fieldset>{!editing.value.allDay && <div className="availability-interval-row"><label>Início<input aria-label="Início" type="time" value={editing.value.startTime ?? ''} onChange={(event) => setEditing((current) => ({ ...current, value: { ...current.value, startTime: event.target.value } }))} /></label><span className="availability-interval-dash">–</span><label>Fim<input aria-label="Fim" type="time" value={editing.value.endTime ?? ''} onChange={(event) => setEditing((current) => ({ ...current, value: { ...current.value, endTime: event.target.value } }))} /></label></div>}<label className="field-label">Motivo <span className="field-hint">opcional</span><input className="field-input" maxLength={300} value={editing.value.reason} onChange={(event) => setEditing((current) => ({ ...current, value: { ...current.value, reason: event.target.value } }))} /></label><div className="modal-actions"><button className="secondary-button" type="button" onClick={() => setOpen(false)}>Cancelar</button><button className="primary-button" type="button" disabled={pending || !editing.value.date} onClick={() => void submit()}>{pending ? 'Salvando…' : 'Salvar'}</button></div></div>}</section>
}
