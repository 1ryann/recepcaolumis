import { Ban, CalendarClock, CalendarDays, Check, Eye, Plus, X } from 'lucide-react'
import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import {
  professionalReservationsApi, professionalsApi, reservationsApi, roomsApi,
  type PagedResponse, type ProfessionalDto, type ReservationDto, type ReservationStatus, type RoomDto,
} from '../../api/modules'
import { useSession } from '../../auth/SessionProvider'
import { EmptyState, PageHeader, countLabel } from '../../components/PageElements'
import { Modal } from '../../components/Modal'

const pageSize = 20
const empty: PagedResponse<ReservationDto> = { items: [], page: 1, pageSize, totalCount: 0 }
const statusLabels: Record<ReservationStatus, string> = {
  PENDING: 'Pendente', APPROVED: 'Aprovada', REJECTED: 'Recusada', CANCELLED: 'Cancelada',
}
const kindLabels = { NEW: 'Nova reserva', RESCHEDULE: 'Remarcação', CANCELLATION: 'Cancelamento' }
const emptyForm = { roomId: '', professionalId: '', startAt: '', endAt: '' }

export const reservationLocalToIso = (value: string) => {
  const local = value.length === 16 ? `${value}:00` : value
  return new Date(`${local}-04:00`).toISOString()
}
const toInputDate = (value?: string | null) => value
  ? new Date(value).toLocaleString('sv-SE', { timeZone: 'America/Porto_Velho' }).replace(' ', 'T').slice(0, 16)
  : ''
const formatDate = (value: string) => new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Porto_Velho',
}).format(new Date(value))
const formatTime = (value: string) => new Intl.DateTimeFormat('pt-BR', {
  timeStyle: 'short', timeZone: 'America/Porto_Velho',
}).format(new Date(value))
// A same-day slot reads "09/09/2026, 14:00 – 15:00" instead of repeating the date.
const formatPeriod = (start: string, end: string) => formatDate(start).slice(0, 10) === formatDate(end).slice(0, 10)
  ? `${formatDate(start)} – ${formatTime(end)}`
  : `${formatDate(start)} — ${formatDate(end)}`

export function Reservations() {
  const session = useSession()
  const roles = session.user?.roles ?? []
  const professionalMode = roles.includes('PROFISSIONAL') &&
    !roles.some(role => role === 'ADMINISTRADOR' || role === 'GERENTE')
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState('')
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState<ReservationStatus | 'all'>('all')
  const [roomFilter, setRoomFilter] = useState('')
  const [professionalFilter, setProfessionalFilter] = useState('')
  const [rooms, setRooms] = useState<RoomDto[]>([])
  const [professionals, setProfessionals] = useState<ProfessionalDto[]>([])
  const [form, setForm] = useState(emptyForm)
  const [formReservation, setFormReservation] = useState<ReservationDto | null | undefined>(undefined)
  const [detail, setDetail] = useState<ReservationDto | null>(null)
  const [rejecting, setRejecting] = useState<ReservationDto | null>(null)
  const [rejectionReason, setRejectionReason] = useState('')
  const [saving, setSaving] = useState(false)

  const load = useCallback(async (signal?: AbortSignal) => {
    setError('')
    setRefreshing(!loading)
    try {
      const response = professionalMode
        ? await professionalReservationsApi.list({ status, page, pageSize }, signal)
        : await reservationsApi.list({
            status, page, pageSize,
            roomId: roomFilter || undefined,
            professionalId: professionalFilter || undefined,
          }, signal)
      setResult(response)
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError')
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as reservas.')
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
  }, [page, professionalFilter, professionalMode, roomFilter, status])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  useEffect(() => {
    const controller = new AbortController()
    const query = { status: 'active' as const, page: 1, pageSize: 100 }
    void roomsApi.list(query, controller.signal).then(response => setRooms(response.items)).catch(() => undefined)
    if (!professionalMode)
      void professionalsApi.list(query, controller.signal).then(response => setProfessionals(response.items)).catch(() => undefined)
    return () => controller.abort()
  }, [professionalMode])

  const upsert = (reservation: ReservationDto) => setResult(current => ({
    ...current,
    items: current.items.map(item => item.id === reservation.id ? reservation : item),
  }))
  const resolveFailure = async (reason: unknown) => {
    if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') {
      await load()
      setError('Esta reserva foi alterada por outra operação. Recarregamos os dados para você tentar novamente.')
      return
    }
    setError(reason instanceof Error ? reason.message : 'Não foi possível concluir a operação.')
  }
  const openForm = (reservation: ReservationDto | null) => {
    setFormReservation(reservation)
    setForm(reservation ? {
      roomId: reservation.roomId,
      professionalId: reservation.professionalId,
      startAt: toInputDate(reservation.startAt),
      endAt: toInputDate(reservation.endAt),
    } : emptyForm)
  }
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setSaving(true)
    setError('')
    try {
      let saved: ReservationDto
      if (formReservation) {
        const input = {
          startAt: reservationLocalToIso(form.startAt), endAt: reservationLocalToIso(form.endAt),
          concurrencyToken: formReservation.concurrencyToken,
        }
        saved = professionalMode
          ? await professionalReservationsApi.requestReschedule(formReservation.id, input)
          : await reservationsApi.reschedule(formReservation.id, input)
      } else if (professionalMode) {
        saved = await professionalReservationsApi.create({
          roomId: form.roomId, startAt: reservationLocalToIso(form.startAt), endAt: reservationLocalToIso(form.endAt),
        })
      } else {
        saved = await reservationsApi.create({
          roomId: form.roomId, professionalId: form.professionalId,
          startAt: reservationLocalToIso(form.startAt), endAt: reservationLocalToIso(form.endAt),
        })
      }
      setResult(current => ({ ...current, items: [saved, ...current.items], totalCount: current.totalCount + 1 }))
      setFormReservation(undefined)
      if (!professionalMode && formReservation) await load()
    } catch (reason) {
      await resolveFailure(reason)
    } finally {
      setSaving(false)
    }
  }
  const approve = async (reservation: ReservationDto) => {
    try {
      await reservationsApi.approve(reservation.id, reservation.concurrencyToken)
      await load()
    }
    catch (reason) { await resolveFailure(reason) }
  }
  const cancel = async (reservation: ReservationDto) => {
    try {
      const updated = professionalMode
        ? await professionalReservationsApi.requestCancellation(reservation.id, reservation.concurrencyToken)
        : await reservationsApi.cancel(reservation.id, reservation.concurrencyToken)
      if (professionalMode)
        setResult(current => ({ ...current, items: [updated, ...current.items], totalCount: current.totalCount + 1 }))
      else upsert(updated)
    } catch (reason) { await resolveFailure(reason) }
  }
  const reject = async (event: FormEvent) => {
    event.preventDefault()
    if (!rejecting) return
    try {
      upsert(await reservationsApi.reject(rejecting.id, rejectionReason, rejecting.concurrencyToken))
      setRejecting(null)
      setRejectionReason('')
    } catch (reason) { await resolveFailure(reason) }
  }
  const showDetail = async (reservation: ReservationDto) => {
    try {
      setDetail(professionalMode
        ? await professionalReservationsApi.detail(reservation.id)
        : await reservationsApi.detail(reservation.id))
    } catch (reason) { await resolveFailure(reason) }
  }
  const pages = Math.max(1, Math.ceil(result.totalCount / pageSize))

  return <div className="page-enter">
    <PageHeader eyebrow="Agenda operacional" title="Reservas"
      description={professionalMode ? 'Solicite e acompanhe seus horários.' : 'Gerencie solicitações e horários das salas.'}
      action={<button className="primary-button" onClick={() => openForm(null)}>
        <Plus size={18} /> {professionalMode ? 'Solicitar reserva' : 'Nova reserva'}
      </button>} />
    <section className="panel table-panel">
      <div className="table-toolbar">
        <select className="field-input compact-select" value={status} aria-label="Status das reservas"
          onChange={event => { setStatus(event.target.value as ReservationStatus | 'all'); setPage(1) }}>
          <option value="all">Todos os status</option>
          {Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select>
        {!professionalMode && <>
          <select className="field-input compact-select" value={professionalFilter} aria-label="Filtrar por profissional"
            onChange={event => { setProfessionalFilter(event.target.value); setPage(1) }}>
            <option value="">Todos os profissionais</option>
            {professionals.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
          <select className="field-input compact-select" value={roomFilter} aria-label="Filtrar por sala"
            onChange={event => { setRoomFilter(event.target.value); setPage(1) }}>
            <option value="">Todas as salas</option>
            {rooms.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </>}
        <span>{countLabel(result.totalCount, 'reserva', 'reservas')}</span>
      </div>
      {loading ? <div className="empty-state" role="status">Carregando reservas…</div>
        : error && result.items.length === 0 ? <EmptyState><p>{error}</p><button className="secondary-button" onClick={() => void load()}>Tentar novamente</button></EmptyState>
          : result.items.length === 0 ? <EmptyState>Nenhuma reserva encontrada.</EmptyState>
            : <div className="table-scroll"><table className="data-table"><thead><tr>
              <th>Profissional</th><th>Sala</th><th>Período</th><th>Tipo</th><th>Status</th><th className="actions-column">Ações</th>
            </tr></thead><tbody>{result.items.map(reservation => <tr key={reservation.id}>
              <td><strong>{reservation.professionalName}</strong></td><td>{reservation.roomName}</td>
              <td><span className="date-cell"><CalendarDays size={16} />{formatPeriod(reservation.startAt, reservation.endAt)}</span></td>
              <td>{kindLabels[reservation.kind]}</td>
              <td><span className={`status-badge status-${reservation.status.toLowerCase()}`}><i />{statusLabels[reservation.status]}</span></td>
              <td><div className="row-actions">
                <button type="button" title="Detalhes" aria-label={`Detalhes da reserva de ${reservation.professionalName}`} onClick={() => void showDetail(reservation)}><Eye size={17} /></button>
                {!professionalMode && reservation.status === 'PENDING' && <>
                  <button type="button" title="Aprovar" aria-label={`Aprovar reserva de ${reservation.professionalName}`} onClick={() => void approve(reservation)}><Check size={17} /></button>
                  <button type="button" title="Recusar" aria-label={`Recusar reserva de ${reservation.professionalName}`} onClick={() => { setRejecting(reservation); setRejectionReason('') }}><X size={17} /></button>
                </>}
                {reservation.status === 'APPROVED' && reservation.kind !== 'CANCELLATION' && <>
                  <button type="button" title={professionalMode ? 'Solicitar remarcação' : 'Remarcar'} aria-label={`${professionalMode ? 'Solicitar remarcação de' : 'Remarcar reserva de'} ${reservation.roomName}`} onClick={() => openForm(reservation)}><CalendarClock size={17} /></button>
                  <button type="button" title={professionalMode ? 'Solicitar cancelamento' : 'Cancelar reserva'} aria-label={`${professionalMode ? 'Solicitar cancelamento de' : 'Cancelar reserva de'} ${reservation.roomName}`} onClick={() => void cancel(reservation)}><Ban size={17} /></button>
                </>}
              </div></td>
            </tr>)}</tbody></table></div>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}
      {refreshing && <p className="list-refreshing" role="status">Atualizando lista…</p>}
      {result.totalCount > pageSize && <div className="pagination"><button className="secondary-button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>

    <Modal open={formReservation !== undefined} onClose={() => setFormReservation(undefined)}
      title={formReservation ? (professionalMode ? 'Solicitar remarcação' : 'Remarcar reserva') : (professionalMode ? 'Solicitar reserva' : 'Nova reserva')}
      subtitle={formReservation ? 'Informe o novo período. A sala continua a mesma.' : 'Informe a sala e o período de uso.'}>
      <form className="simple-form" onSubmit={submit}>
        {!formReservation && <label className="field-label">Sala<select className="field-input" required value={form.roomId} onChange={event => setForm(current => ({ ...current, roomId: event.target.value }))}><option value="">Selecione</option>{rooms.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
        {!professionalMode && !formReservation && <label className="field-label">Profissional<select className="field-input" required value={form.professionalId} onChange={event => setForm(current => ({ ...current, professionalId: event.target.value }))}><option value="">Selecione</option>{professionals.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
        <label className="field-label">Início<input className="field-input" required type="datetime-local" value={form.startAt} onChange={event => setForm(current => ({ ...current, startAt: event.target.value }))} /></label>
        <label className="field-label">Fim<input className="field-input" required type="datetime-local" value={form.endAt} onChange={event => setForm(current => ({ ...current, endAt: event.target.value }))} /></label>
        <div className="modal-actions"><button className="ghost-button" type="button" onClick={() => setFormReservation(undefined)}>Cancelar</button><button className="primary-button" disabled={saving} type="submit">{saving ? 'Salvando…' : formReservation ? 'Confirmar remarcação' : 'Salvar reserva'}</button></div>
      </form>
    </Modal>
    <Modal open={rejecting !== null} onClose={() => setRejecting(null)} title="Recusar reserva">
      <form className="simple-form" onSubmit={reject}><label className="field-label">Motivo da recusa<textarea className="field-input" required maxLength={500} value={rejectionReason} onChange={event => setRejectionReason(event.target.value)} /></label><div className="modal-actions"><button className="ghost-button" type="button" onClick={() => setRejecting(null)}>Cancelar</button><button className="primary-button" type="submit">Confirmar recusa</button></div></form>
    </Modal>
    <Modal open={detail !== null} onClose={() => setDetail(null)} title="Detalhes da reserva">{detail && <dl className="room-rates"><div><dt>Profissional</dt><dd>{detail.professionalName}</dd></div><div><dt>Sala</dt><dd>{detail.roomName}</dd></div><div><dt>Período</dt><dd>{formatPeriod(detail.startAt, detail.endAt)}</dd></div><div><dt>Status</dt><dd>{statusLabels[detail.status]}</dd></div>{detail.rejectionReason && <div><dt>Motivo da recusa</dt><dd>{detail.rejectionReason}</dd></div>}</dl>}</Modal>
  </div>
}
