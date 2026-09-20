import { ArrowRight, CalendarDays, CheckCircle2, Clock3, UserRound } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { rescheduleApi, type AvailabilitySlotDto, type RescheduleConfirmationDto, type RescheduleLinkDto } from '../api/modules'
import { LumisPageShell } from '../features/lumis/LumisPageShell'
import { LumisLogo } from '../theme/LumisLogo'

// `/reagendar/:token` — the page behind the URL button of the client_professional_cancelled
// template. The token is one-time, lives only in the URL, and is never rendered, stored or
// logged here: it is passed straight back to the public endpoints (resolve / slots / confirm).
// Times are always shown in the building's time zone, not the visitor's, because the
// appointment happens here.

const zone = 'America/Porto_Velho'
const timeLabel = (value: string) =>
  new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: zone })
const dateLabel = (value: string) =>
  new Date(value).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', timeZone: zone })

function dayInputValue(instant: Date) {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(instant)
  const value = (type: string) => parts.find((part) => part.type === type)!.value
  return `${value('year')}-${value('month')}-${value('day')}`
}

const invalidLink = 'Este link de reagendamento é inválido ou expirou. Entre em contato com a recepção.'

export function RescheduleLink() {
  const { token = '' } = useParams()
  const [link, setLink] = useState<RescheduleLinkDto | null>(null)
  const [date, setDate] = useState(() => dayInputValue(new Date()))
  const [slots, setSlots] = useState<AvailabilitySlotDto[]>([])
  const [selected, setSelected] = useState<AvailabilitySlotDto | null>(null)
  const [done, setDone] = useState<RescheduleConfirmationDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [checking, setChecking] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [dead, setDead] = useState(false)

  useEffect(() => {
    let cancelled = false
    rescheduleApi.resolve(token)
      .then((resolved) => { if (!cancelled) setLink(resolved) })
      .catch((caught) => {
        if (cancelled) return
        setDead(true)
        setError(caught instanceof ApiError && caught.status === 429
          ? 'Muitas tentativas. Aguarde um pouco e abra o link novamente.'
          : invalidLink)
      })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [token])

  useEffect(() => {
    if (!link || !date || done) return
    const controller = new AbortController()
    setSelected(null)
    setSlots([])
    setChecking(true)
    setError('')
    rescheduleApi.slots(token, date, controller.signal)
      .then((found) => setSlots(found))
      .catch((caught) => {
        if (controller.signal.aborted) return
        setError(caught instanceof ApiError && caught.code === 'INVALID_RESCHEDULE_LINK'
          ? invalidLink
          : 'Não foi possível consultar os horários. Tente novamente.')
      })
      .finally(() => { if (!controller.signal.aborted) setChecking(false) })
    return () => controller.abort()
  }, [link, date, token, done])

  const confirm = async () => {
    if (!selected) return
    setSaving(true)
    setError('')
    try {
      setDone(await rescheduleApi.confirm({ token, startAt: selected.startAt, endAt: selected.endAt }))
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 409) {
        setError('Esse horário acabou de ser ocupado. Escolha outro horário.')
      } else if (caught instanceof ApiError && caught.code === 'INVALID_RESCHEDULE_LINK') {
        setDead(true)
        setError(invalidLink)
      } else {
        setError('Não foi possível concluir o reagendamento. Tente novamente.')
      }
    } finally { setSaving(false) }
  }

  return (
    <LumisPageShell className="lumis-login">
      <main className="lumis-login-card lumis-auth-surface is-wide">
        <LumisLogo className="lumis-login-logo" alt="LUMIS" width={124} height={38} />
        <span className="lumis-login-eyebrow">REAGENDAMENTO</span>

        {done ? (
          <>
            <h1 className="lumis-login-title">Atendimento reagendado.</h1>
            <p className="lumis-login-text">
              <CheckCircle2 size={18} /> {dateLabel(done.startAt)} às {timeLabel(done.startAt)} com {done.professionalName} · {done.roomName}
            </p>
            <p className="lumis-login-secondary">Você receberá a confirmação no WhatsApp. Este link não pode ser usado novamente.</p>
          </>
        ) : (
          <>
            <h1 className="lumis-login-title">Escolha um novo horário.</h1>
            {loading && <div className="customer-loading" role="status">Abrindo seu link…</div>}
            {error && <div className="form-error" role="alert">{error}</div>}

            {link && !dead && (
              <>
                <p className="lumis-login-text">
                  <UserRound size={16} /> {link.professionalName} · o atendimento de {dateLabel(link.originalStartAt)} às{' '}
                  {timeLabel(link.originalStartAt)} foi cancelado por um imprevisto. A duração continua de {link.durationMinutes} minutos.
                </p>
                <p className="lumis-login-secondary">Este link vale até {dateLabel(link.expiresAt)} às {timeLabel(link.expiresAt)}.</p>

                <label className="field-label">
                  <span><CalendarDays size={15} /> Data</span>
                  <input className="field-input" type="date" min={dayInputValue(new Date())} value={date}
                    onChange={(event) => setDate(event.target.value)} />
                </label>

                <div className="customer-slots">
                  {checking && <div className="customer-loading" role="status">Buscando horários…</div>}
                  {!checking && slots.length === 0 && (
                    <div className="customer-empty"><Clock3 size={23} /><strong>Nenhum horário nesta data</strong><span>Escolha outro dia.</span></div>
                  )}
                  {!checking && slots.length > 0 && (
                    <div className="customer-slot-grid">
                      {slots.map((slot) => (
                        <button key={slot.startAt} type="button"
                          className={`customer-slot ${selected?.startAt === slot.startAt ? 'is-selected' : ''}`}
                          onClick={() => setSelected(slot)}>
                          {timeLabel(slot.startAt)}<small>até {timeLabel(slot.endAt)}</small>
                        </button>
                      ))}
                    </div>
                  )}
                </div>

                <button className="primary-button lumis-login-submit" type="button" disabled={!selected || saving} onClick={() => void confirm()}>
                  {saving ? <span className="spinner" /> : <>Confirmar novo horário <ArrowRight size={18} /></>}
                </button>
              </>
            )}
          </>
        )}
      </main>
    </LumisPageShell>
  )
}
