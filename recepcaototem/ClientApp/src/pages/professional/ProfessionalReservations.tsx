import { CalendarDays } from 'lucide-react'
import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { professionalReservationsApi, type PagedResponse, type ReservationDto, type ReservationStatus } from '../../api/modules'
import { EmptyState, PageHeader, StatusBadge } from '../../components/PageElements'
import { ProfessionalFilterBar } from '../../components/ProfessionalFilterBar'
import { Modal } from '../../components/Modal'

const pageSize = 20
const empty: PagedResponse<ReservationDto> = { items: [], page: 1, pageSize, totalCount: 0 }
const statusLabels: Record<ReservationStatus, string> = {
  PENDING: 'Pendente', APPROVED: 'Aprovada', REJECTED: 'Recusada', CANCELLED: 'Cancelada',
}
const statusTones: Record<ReservationStatus, 'pending' | 'approved' | 'rejected' | 'cancelled'> = {
  PENDING: 'pending', APPROVED: 'approved', REJECTED: 'rejected', CANCELLED: 'cancelled',
}
const emptyForm = { startAt: '', endAt: '' }

const reservationLocalToIso = (value: string) => {
  const local = value.length === 16 ? `${value}:00` : value
  return new Date(`${local}-04:00`).toISOString()
}
const toInputDate = (value?: string | null) => value
  ? new Date(value).toLocaleString('sv-SE', { timeZone: 'America/Porto_Velho' }).replace(' ', 'T').slice(0, 16)
  : ''
const formatDate = (value: string) => new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'short', timeZone: 'America/Porto_Velho',
}).format(new Date(value))
const formatTime = (value: string) => new Intl.DateTimeFormat('pt-BR', {
  timeStyle: 'short', timeZone: 'America/Porto_Velho',
}).format(new Date(value))

export function ProfessionalReservations() {
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState('')
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState<ReservationStatus | 'all'>('all')
  const [rescheduling, setRescheduling] = useState<ReservationDto | null>(null)
  const [form, setForm] = useState(emptyForm)
  const [cancelling, setCancelling] = useState<ReservationDto | null>(null)
  const [saving, setSaving] = useState(false)

  const load = useCallback(async (signal?: AbortSignal) => {
    setError('')
    setRefreshing(!loading)
    try {
      const response = await professionalReservationsApi.list({ status, page, pageSize }, signal)
      setResult(response)
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError')
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as reservas.')
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
  }, [page, status, loading])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

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
  const openReschedule = (reservation: ReservationDto) => {
    setRescheduling(reservation)
    setForm({ startAt: toInputDate(reservation.startAt), endAt: toInputDate(reservation.endAt) })
  }
  const submitReschedule = async (event: FormEvent) => {
    event.preventDefault()
    if (!rescheduling) return
    setSaving(true)
    setError('')
    try {
      const updated = await professionalReservationsApi.requestReschedule(rescheduling.id, {
        startAt: reservationLocalToIso(form.startAt), endAt: reservationLocalToIso(form.endAt),
        concurrencyToken: rescheduling.concurrencyToken,
      })
      upsert(updated)
      setRescheduling(null)
    } catch (reason) {
      await resolveFailure(reason)
    } finally {
      setSaving(false)
    }
  }
  const confirmCancel = async () => {
    if (!cancelling) return
    setSaving(true)
    try {
      const updated = await professionalReservationsApi.requestCancellation(cancelling.id, cancelling.concurrencyToken)
      upsert(updated)
      setCancelling(null)
    } catch (reason) {
      await resolveFailure(reason)
    } finally {
      setSaving(false)
    }
  }
  const pages = Math.max(1, Math.ceil(result.totalCount / pageSize))
  const canReschedule = (reservation: ReservationDto) => reservation.status === 'PENDING' || reservation.status === 'APPROVED'
  const canCancel = (reservation: ReservationDto) => reservation.status !== 'REJECTED' && reservation.status !== 'CANCELLED'

  return <div className="page-enter">
    <PageHeader eyebrow="Agenda" title="Reservas" description="Acompanhe e gerencie suas solicitações de reserva." />
    <section className="panel table-panel">
      <div className="table-toolbar">
        <ProfessionalFilterBar>
          <select className="field-input compact-select" value={status} aria-label="Status das reservas"
            onChange={event => { setStatus(event.target.value as ReservationStatus | 'all'); setPage(1) }}>
            <option value="all">Todos os status</option>
            {Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
          <span>{result.totalCount} reservas</span>
        </ProfessionalFilterBar>
      </div>
      {loading ? <div className="empty-state" role="status">Carregando reservas…</div>
        : error && result.items.length === 0 ? <EmptyState><p>{error}</p><button className="secondary-button" onClick={() => void load()}>Tentar novamente</button></EmptyState>
          : result.items.length === 0 ? <EmptyState>Nenhuma reserva encontrada.</EmptyState>
            : <div className="table-scroll"><table className="data-table"><thead><tr>
              <th>Sala</th><th>Data</th><th>Horário</th><th>Status</th><th>Ações</th>
            </tr></thead><tbody>{result.items.map(reservation => <tr key={reservation.id}>
              <td><strong>{reservation.roomName}</strong></td>
              <td><span className="date-cell"><CalendarDays size={16} />{formatDate(reservation.startAt)}</span></td>
              <td>{formatTime(reservation.startAt)} — {formatTime(reservation.endAt)}</td>
              <td><StatusBadge tone={statusTones[reservation.status]} label={statusLabels[reservation.status]} /></td>
              <td><div className="room-admin-actions">
                {canReschedule(reservation) && <button className="secondary-button" aria-label={`Solicitar remarcação de ${reservation.roomName}`} onClick={() => openReschedule(reservation)}>Solicitar remarcação</button>}
                {canCancel(reservation) && <button className="ghost-button" aria-label={`Solicitar cancelamento de ${reservation.roomName}`} onClick={() => setCancelling(reservation)}>Solicitar cancelamento</button>}
              </div></td>
            </tr>)}</tbody></table></div>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}
      {refreshing && <p className="list-refreshing" role="status">Atualizando lista…</p>}
      {result.totalCount > pageSize && <div className="pagination"><button className="secondary-button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>

    <Modal open={rescheduling !== null} onClose={() => setRescheduling(null)} title="Solicitar remarcação" subtitle="Informe o novo período de uso.">
      <form className="simple-form" onSubmit={submitReschedule}>
        <label className="field-label">Início<input className="field-input" required type="datetime-local" value={form.startAt} onChange={event => setForm(current => ({ ...current, startAt: event.target.value }))} /></label>
        <label className="field-label">Fim<input className="field-input" required type="datetime-local" value={form.endAt} onChange={event => setForm(current => ({ ...current, endAt: event.target.value }))} /></label>
        <div className="modal-actions"><button className="ghost-button" type="button" onClick={() => setRescheduling(null)}>Cancelar</button><button className="primary-button" disabled={saving} type="submit">{saving ? 'Enviando…' : 'Confirmar remarcação'}</button></div>
      </form>
    </Modal>
    <Modal open={cancelling !== null} onClose={() => setCancelling(null)} title="Solicitar cancelamento" subtitle="Deseja realmente solicitar o cancelamento desta reserva?">
      <div className="modal-actions"><button className="ghost-button" type="button" onClick={() => setCancelling(null)}>Voltar</button><button className="primary-button" disabled={saving} onClick={() => void confirmCancel()}>{saving ? 'Enviando…' : 'Confirmar'}</button></div>
    </Modal>
  </div>
}
