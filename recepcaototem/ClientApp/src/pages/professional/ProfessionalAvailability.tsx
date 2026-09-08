import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { professionalAvailabilityApi, type AvailabilityExceptionDto, type ProfessionalAvailabilityDto } from '../../api/modules'
import { PageHeader } from '../../components/PageElements'
import { AvailabilityEditor, ExceptionsEditor, type AvailabilityDraft } from '../../features/availability/AvailabilityEditor'

const errorMessage = (error: unknown) => {
  if (error instanceof ApiError) {
    if (error.code === 'RESOURCE_MODIFIED') return 'Esta disponibilidade foi alterada em outra sessão. Atualizamos os dados para você.'
    if (error.code === 'OPERATING_HOURS_NOT_CONFIGURED') return 'O horário de funcionamento ainda não foi configurado pela gestão.'
    if (error.code === 'PROFESSIONAL_UNAVAILABLE') return 'A agenda informada não pode ser utilizada neste momento.'
    return error.message
  }
  return 'Não foi possível carregar sua disponibilidade agora.'
}

export function ProfessionalAvailability() {
  const [availability, setAvailability] = useState<ProfessionalAvailabilityDto | null>(null)
  const [exceptions, setExceptions] = useState<AvailabilityExceptionDto[]>([])
  const [draft, setDraft] = useState<AvailabilityDraft | null>(null)
  const [loading, setLoading] = useState(true)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const load = useCallback(async (signal?: AbortSignal) => {
    setError('')
    const [schedule, exceptionRows] = await Promise.all([
      professionalAvailabilityApi.get(signal),
      professionalAvailabilityApi.listExceptions(signal),
    ])
    setAvailability(schedule)
    setDraft({ mode: schedule.mode, days: schedule.days.map((day) => ({ dayOfWeek: day.dayOfWeek, intervals: day.intervals.map((interval) => ({ startTime: interval.startTime.slice(0, 5), endTime: interval.endTime.slice(0, 5) })) })) })
    setExceptions(exceptionRows)
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal).catch((reason) => { if (!controller.signal.aborted) setError(errorMessage(reason)) }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [load])

  const reloadAfterConflict = async () => {
    try { await load(); setError('Esta disponibilidade foi alterada em outra sessão. Atualizamos os dados para você.') }
    catch (reason) { setError(errorMessage(reason)) }
  }

  const save = async (next: { mode: ProfessionalAvailabilityDto['mode'], days: ProfessionalAvailabilityDto['days'] }) => {
    if (!availability) return
    setPending(true); setError(''); setNotice('')
    try {
      const updated = await professionalAvailabilityApi.update({ mode: next.mode, days: next.mode === 'CUSTOM' ? next.days.map((day) => ({ dayOfWeek: day.dayOfWeek, intervals: day.intervals.map((interval) => ({ startTime: interval.startTime, endTime: interval.endTime })) })) : undefined, concurrencyToken: availability.concurrencyToken })
      setAvailability(updated); setDraft({ mode: updated.mode, days: updated.days.map((day) => ({ dayOfWeek: day.dayOfWeek, intervals: day.intervals.map((interval) => ({ startTime: interval.startTime.slice(0, 5), endTime: interval.endTime.slice(0, 5) })) })) });
      if (updated.existingReservationsOutsideAvailabilityCount > 0) setNotice(`Você possui ${updated.existingReservationsOutsideAvailabilityCount} agendamento(s) já existente(s) fora da nova disponibilidade. Esses agendamentos foram mantidos.`)
      else setNotice('Disponibilidade atualizada.')
    } catch (reason) { if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') await reloadAfterConflict(); else setError(errorMessage(reason)) }
    finally { setPending(false) }
  }

  const createException = async (input: { date: string, allDay: boolean, startTime: string | null, endTime: string | null, reason: string }) => {
    setPending(true); setError('')
    try { const created = await professionalAvailabilityApi.createException({ ...input, reason: input.reason.trim() || null }); setExceptions((current) => [...current, created]) }
    catch (reason) { setError(errorMessage(reason)) }
    finally { setPending(false) }
  }
  const updateException = async (id: string, input: { date: string, allDay: boolean, startTime: string | null, endTime: string | null, reason: string, concurrencyToken: string }) => {
    setPending(true); setError('')
    try { const updated = await professionalAvailabilityApi.updateException(id, { ...input, reason: input.reason.trim() || null }); setExceptions((current) => current.map((item) => item.id === id ? updated : item)) }
    catch (reason) { if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') await reloadAfterConflict(); else setError(errorMessage(reason)) }
    finally { setPending(false) }
  }
  const deleteException = async (id: string, concurrencyToken: string) => {
    setPending(true); setError('')
    try { await professionalAvailabilityApi.deleteException(id, concurrencyToken); setExceptions((current) => current.filter((item) => item.id !== id)) }
    catch (reason) { if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') await reloadAfterConflict(); else setError(errorMessage(reason)) }
    finally { setPending(false) }
  }

  return <section className="professional-section page-enter availability-page"><PageHeader eyebrow="Agenda" title="Minha disponibilidade" description="Defina quando novos clientes podem escolher um horário com você." />{error && <div className="form-error" role="alert">{error}</div>}{notice && <div className="availability-success" role="status">{notice}</div>}{loading ? <div className="professional-loading" role="status">Carregando disponibilidade…</div> : availability && draft ? <><AvailabilityEditor value={availability} draft={draft} pending={pending} onChange={(next) => setDraft(next)} onSave={(next) => void save(next)} /><ExceptionsEditor exceptions={exceptions} pending={pending} onCreate={createException} onUpdate={updateException} onDelete={deleteException} /></> : null}</section>
}
