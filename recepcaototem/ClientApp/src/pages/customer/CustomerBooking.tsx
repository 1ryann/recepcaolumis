import { ArrowLeft, ArrowRight, CalendarDays, Clock3, UserRound } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { customerApi, totemApi, type AvailabilitySlotDto, type CustomerProfessionalDto } from '../../api/modules'

function todayInputValue() {
  const now = new Date()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

function timeLabel(value: string) {
  return new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
}

export function CustomerBooking() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [professionals, setProfessionals] = useState<CustomerProfessionalDto[]>([])
  const [professionalId, setProfessionalId] = useState('')
  const [date, setDate] = useState(todayInputValue)
  const [durationMinutes, setDurationMinutes] = useState(60)
  const [slots, setSlots] = useState<AvailabilitySlotDto[]>([])
  const [selectedSlot, setSelectedSlot] = useState<AvailabilitySlotDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [checking, setChecking] = useState(false)
  const [error, setError] = useState('')
  const [handoffToken, setHandoffToken] = useState<string | null>(null)
  const [, setHandoffId] = useState<string | null>(null)
  const [handoffError, setHandoffError] = useState('')

  useEffect(() => {
    let cancelled = false
    const token = params.get('handoff')

    const loadProfessionals = (forcedProfessionalId: string | null) =>
      customerApi.professionals().then((items) => {
        if (cancelled) return
        setProfessionals(items)
        if (forcedProfessionalId) {
          setProfessionalId(forcedProfessionalId)
        } else {
          const preselect = params.get('professionalId')
          setProfessionalId(items.some((i) => i.id === preselect) ? preselect! : (items[0]?.id ?? ''))
        }
      }).catch(() => { if (!cancelled) setError('Não foi possível carregar os profissionais.') })
        .finally(() => { if (!cancelled) setLoading(false) })

    if (!token) {
      void loadProfessionals(null)
      return () => { cancelled = true }
    }

    void totemApi.claimHandoff(token).catch(() => {})
    customerApi.resolveHandoff(token)
      .then((resolved) => {
        if (cancelled) return
        setHandoffToken(token)
        setHandoffId(resolved.handoffId)
        navigate('/cliente/agendar', { replace: true })
        return loadProfessionals(resolved.professionalId)
      })
      .catch(() => {
        if (cancelled) return
        setHandoffError('Este convite expirou. Você pode escolher o profissional normalmente.')
        return loadProfessionals(null)
      })

    return () => { cancelled = true }
  }, [])

  useEffect(() => {
    if (!professionalId || !date) return
    setSelectedSlot(null)
    setSlots([])
    setChecking(true)
    setError('')
    customerApi.availability({ professionalId, date, durationMinutes })
      .then(setSlots)
      .catch((caught) => setError(caught instanceof ApiError && caught.status === 409 ? 'Esse horário não está disponível.' : 'Não foi possível consultar os horários.'))
      .finally(() => setChecking(false))
  }, [professionalId, date, durationMinutes])

  const professional = useMemo(() => professionals.find((item) => item.id === professionalId), [professionals, professionalId])
  const createReservation = async () => {
    if (!selectedSlot) return
    setChecking(true)
    setError('')
    try {
      const reservation = await customerApi.createReservation({
        professionalId,
        startAt: selectedSlot.startAt,
        endAt: selectedSlot.endAt,
        ...(handoffToken ? { handoffToken } : {}),
      })
      navigate(`/cliente/agendamentos/${reservation.id}`)
    } catch (caught) {
      if (handoffToken && caught instanceof ApiError && (caught.code === 'HANDOFF_EXPIRED' || caught.code === 'HANDOFF_ALREADY_USED')) {
        setHandoffToken(null)
        setHandoffError('Este convite expirou. Você pode escolher o profissional normalmente.')
      } else {
        setError(caught instanceof ApiError && caught.status === 409 ? 'Esse horário acabou de ser ocupado. Escolha outro.' : 'Não foi possível criar o agendamento.')
      }
    } finally { setChecking(false) }
  }

  return <section className="customer-section page-enter">
    <Link className="customer-back-link" to="/cliente"><ArrowLeft size={16} /> Voltar para a minha área</Link>
    <div className="customer-section-heading customer-booking-heading"><div><span className="eyebrow">Novo agendamento</span><h1>Escolha seu horário.</h1><p>Selecione um profissional e encontre um momento tranquilo para o seu atendimento.</p></div></div>
    {error && <div className="form-error" role="alert">{error}</div>}
    {handoffError && <div className="form-error" role="alert">{handoffError}</div>}
    {loading ? <div className="customer-loading" role="status">Carregando profissionais…</div> : <>
      <div className="customer-booking-controls panel">
        <label className="field-label"><span><UserRound size={15} /> Profissional</span><select className="field-input" value={professionalId} onChange={(event) => setProfessionalId(event.target.value)}>{professionals.map((item) => <option key={item.id} value={item.id}>{item.name} · {item.profession}</option>)}</select></label>
        <label className="field-label"><span><CalendarDays size={15} /> Data</span><input className="field-input" type="date" min={todayInputValue()} value={date} onChange={(event) => setDate(event.target.value)} /></label>
        <label className="field-label"><span><Clock3 size={15} /> Duração</span><select className="field-input" value={durationMinutes} onChange={(event) => setDurationMinutes(Number(event.target.value))}><option value={15}>15 minutos</option><option value={30}>30 minutos</option><option value={45}>45 minutos</option><option value={60}>1 hora</option><option value={90}>1h30</option><option value={120}>2 horas</option></select></label>
      </div>
      {professional && <div className="customer-booking-professional panel"><div className="customer-booking-avatar"><UserRound size={21} /></div><div><strong>{professional.name}</strong><span>{professional.profession}</span>{professional.description && <p>{professional.description}</p>}</div></div>}
      <div className="customer-slots panel"><div className="panel-header"><div><h2>Horários disponíveis</h2><p>{checking ? 'Consultando disponibilidade…' : 'Escolha um horário para continuar.'}</p></div></div>{!checking && slots.length === 0 && <div className="customer-empty"><Clock3 size={23} /><strong>Nenhum horário disponível</strong><span>Tente outra data ou duração.</span></div>}{checking && <div className="customer-loading" role="status">Buscando horários…</div>}{!checking && slots.length > 0 && <div className="customer-slot-grid">{slots.map((slot) => <button key={slot.startAt} type="button" className={`customer-slot ${selectedSlot?.startAt === slot.startAt ? 'is-selected' : ''}`} onClick={() => setSelectedSlot(slot)}>{timeLabel(slot.startAt)}<small>até {timeLabel(slot.endAt)}</small></button>)}</div>}</div>
      <div className="customer-booking-footer"><span>{selectedSlot ? `${timeLabel(selectedSlot.startAt)} · ${durationMinutes} min` : 'Nenhum horário selecionado'}</span><button className="primary-button" type="button" disabled={!selectedSlot || checking} onClick={createReservation}>{checking ? 'Confirmando…' : 'Confirmar agendamento'} <ArrowRight size={17} /></button></div>
    </>}
  </section>
}
