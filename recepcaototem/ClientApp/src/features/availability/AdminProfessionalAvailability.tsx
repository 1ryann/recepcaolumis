import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { adminProfessionalAvailabilityApi, type AvailabilityExceptionDto, type ProfessionalAvailabilityDto } from '../../api/modules'
import { AvailabilityEditor, ExceptionsEditor, type AvailabilityDraft } from './AvailabilityEditor'

type Props = { professionalId: string, professionalName: string }

const messageFor = (reason: unknown) => reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED'
  ? 'Esta disponibilidade foi alterada em outra sessão. Atualizamos os dados para você.'
  : reason instanceof Error ? reason.message : 'Não foi possível carregar a disponibilidade.'

export function AdminProfessionalAvailability({ professionalId, professionalName }: Props) {
  const [availability, setAvailability] = useState<ProfessionalAvailabilityDto | null>(null)
  const [draft, setDraft] = useState<AvailabilityDraft | null>(null)
  const [exceptions, setExceptions] = useState<AvailabilityExceptionDto[]>([])
  const [loading, setLoading] = useState(true)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const load = useCallback(async () => {
    setError('')
    const [schedule, rows] = await Promise.all([
      adminProfessionalAvailabilityApi.get(professionalId),
      adminProfessionalAvailabilityApi.listExceptions(professionalId),
    ])
    setAvailability(schedule)
    setDraft({ mode: schedule.mode, days: schedule.days.map((day) => ({ dayOfWeek: day.dayOfWeek, intervals: day.intervals.map((interval) => ({ startTime: interval.startTime.slice(0, 5), endTime: interval.endTime.slice(0, 5) })) })) })
    setExceptions(rows)
  }, [professionalId])

  useEffect(() => { void load().catch((reason) => setError(messageFor(reason))).finally(() => setLoading(false)) }, [load])

  const save = async (next: AvailabilityDraft) => {
    if (!availability) return
    setPending(true); setError(''); setNotice('')
    try {
      const updated = await adminProfessionalAvailabilityApi.update(professionalId, { mode: next.mode, days: next.mode === 'CUSTOM' ? next.days : undefined, concurrencyToken: availability.concurrencyToken })
      setAvailability(updated); setDraft({ mode: updated.mode, days: updated.days.map((day) => ({ dayOfWeek: day.dayOfWeek, intervals: day.intervals.map((interval) => ({ startTime: interval.startTime.slice(0, 5), endTime: interval.endTime.slice(0, 5) })) })) })
      setNotice(updated.existingReservationsOutsideAvailabilityCount > 0 ? `Você possui ${updated.existingReservationsOutsideAvailabilityCount} agendamento(s) já existente(s) fora da nova disponibilidade. Esses agendamentos foram mantidos.` : 'Disponibilidade atualizada.')
    } catch (reason) { if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') { await load(); setError('Esta disponibilidade foi alterada em outra sessão. Atualizamos os dados para você.') } else setError(messageFor(reason)) }
    finally { setPending(false) }
  }
  const createException = async (input: { date: string, allDay: boolean, startTime: string | null, endTime: string | null, reason: string }) => {
    setPending(true); setError('')
    try { const created = await adminProfessionalAvailabilityApi.createException(professionalId, { ...input, reason: input.reason.trim() || null }); setExceptions((current) => [...current, created]) }
    catch (reason) { setError(messageFor(reason)) }
    finally { setPending(false) }
  }
  const updateException = async (id: string, input: { date: string, allDay: boolean, startTime: string | null, endTime: string | null, reason: string, concurrencyToken: string }) => {
    setPending(true); setError('')
    try { const updated = await adminProfessionalAvailabilityApi.updateException(professionalId, id, { ...input, reason: input.reason.trim() || null }); setExceptions((current) => current.map((item) => item.id === id ? updated : item)) }
    catch (reason) { if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') { await load(); setError('Esta disponibilidade foi alterada em outra sessão. Atualizamos os dados para você.') } else setError(messageFor(reason)) }
    finally { setPending(false) }
  }
  const deleteException = async (id: string, concurrencyToken: string) => {
    setPending(true); setError('')
    try { await adminProfessionalAvailabilityApi.deleteException(professionalId, id, concurrencyToken); setExceptions((current) => current.filter((item) => item.id !== id)) }
    catch (reason) { if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') { await load(); setError('Esta disponibilidade foi alterada em outra sessão. Atualizamos os dados para você.') } else setError(messageFor(reason)) }
    finally { setPending(false) }
  }

  if (loading) return <div className="professional-loading" role="status">Carregando disponibilidade…</div>
  if (!availability || !draft) return <div className="form-error" role="alert">{error || 'Disponibilidade indisponível.'}</div>
  return <div className="admin-availability-section"><div><span className="page-eyebrow">Agenda profissional</span><h3>Disponibilidade de {professionalName}</h3><p className="admin-availability-copy">A alteração entra em vigor para novos agendamentos e preserva reservas existentes.</p></div>{error && <div className="form-error" role="alert">{error}</div>}{notice && <div className="availability-success" role="status">{notice}</div>}<AvailabilityEditor value={availability} draft={draft} pending={pending} onChange={setDraft} onSave={save} /><ExceptionsEditor exceptions={exceptions} pending={pending} onCreate={createException} onUpdate={updateException} onDelete={deleteException} /></div>
}
