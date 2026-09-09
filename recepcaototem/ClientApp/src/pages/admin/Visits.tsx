import { Clock3, Plus, UserRoundSearch } from 'lucide-react'
import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { professionalVisitsApi, professionalsApi, reservationsApi, roomsApi, visitsApi,
  type PagedResponse, type ProfessionalDto, type ReservationDto, type RoomDto,
  type VisitDto, type VisitStatus } from '../../api/modules'
import { useSession } from '../../auth/SessionProvider'
import { EmptyState, PageHeader } from '../../components/PageElements'
import { Modal } from '../../components/Modal'

const pageSize = 20
const empty: PagedResponse<VisitDto> = { items: [], page: 1, pageSize, totalCount: 0 }
const statusLabels: Record<VisitStatus, string> = {
  WAITING: 'Aguardando', IN_SERVICE: 'Em atendimento', ENDED: 'Encerrada', CANCELLED: 'Cancelada',
}
const statusClasses: Record<VisitStatus, string> = {
  WAITING: 'status-pending', IN_SERVICE: 'status-active', ENDED: 'status-inactive', CANCELLED: 'status-overdue',
}
const emptyForm = { visitorName: '', professionalId: '', roomId: '', reservationId: '' }
const localBoundary = (value: string, end = false) => value
  ? new Date(`${value}T${end ? '23:59:59' : '00:00:00'}-04:00`).toISOString()
  : undefined
const formatDate = (value: string) => new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Porto_Velho',
}).format(new Date(value))

export function Visits() {
  const roles = useSession().user?.roles ?? []
  const professionalMode = roles.includes('PROFISSIONAL') &&
    !roles.some(role => role === 'ADMINISTRADOR' || role === 'GERENTE')
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState<VisitStatus | 'all'>('all')
  const [professionalId, setProfessionalId] = useState('')
  const [roomId, setRoomId] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [professionals, setProfessionals] = useState<ProfessionalDto[]>([])
  const [rooms, setRooms] = useState<RoomDto[]>([])
  const [reservations, setReservations] = useState<ReservationDto[]>([])
  const [form, setForm] = useState(emptyForm)
  const [creating, setCreating] = useState(false)
  const [detail, setDetail] = useState<VisitDto | null>(null)
  const [correcting, setCorrecting] = useState<VisitDto | null>(null)
  const [correctionStatus, setCorrectionStatus] = useState<VisitStatus>('WAITING')
  const [correctionReason, setCorrectionReason] = useState('')
  const [saving, setSaving] = useState(false)

  const load = useCallback(async (signal?: AbortSignal) => {
    setError('')
    try {
      const time = { from: localBoundary(from), to: localBoundary(to, true) }
      const response = professionalMode
        ? await professionalVisitsApi.list({ status, ...time, page, pageSize }, signal)
        : await visitsApi.list({ status, ...time, page, pageSize,
            professionalId: professionalId || undefined, roomId: roomId || undefined }, signal)
      setResult(response)
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError')
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as visitas.')
    } finally { setLoading(false) }
  }, [from, page, professionalId, professionalMode, roomId, status, to])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  useEffect(() => {
    if (professionalMode) return
    const controller = new AbortController()
    const query = { status: 'active' as const, page: 1, pageSize: 100 }
    // An aborted fetch (unmount / StrictMode remount) rejects with an AbortError:
    // it is expected teardown, not an application failure, so swallow it. Surface
    // any other failure of these filter loads on the page.
    const onFilterError = (reason: unknown) => {
      if ((reason as DOMException | null)?.name === 'AbortError') return
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os filtros.')
    }
    void professionalsApi.list(query, controller.signal).then(value => setProfessionals(value.items)).catch(onFilterError)
    void roomsApi.list(query, controller.signal).then(value => setRooms(value.items)).catch(onFilterError)
    void reservationsApi.list({ status: 'APPROVED', page: 1, pageSize: 100 }, controller.signal)
      .then(value => setReservations(value.items)).catch(onFilterError)
    return () => controller.abort()
  }, [professionalMode])

  const upsert = (visit: VisitDto) => setResult(current => ({
    ...current, items: current.items.map(item => item.id === visit.id ? visit : item),
  }))
  const failure = async (reason: unknown) => {
    if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') {
      await load(); setError('A visita foi alterada por outra operação. A lista foi atualizada.'); return
    }
    setError(reason instanceof Error ? reason.message : 'Não foi possível concluir a operação.')
  }
  const transition = async (visit: VisitDto, action: 'start' | 'end' | 'cancel') => {
    try {
      const api = professionalMode ? professionalVisitsApi : visitsApi
      upsert(await api[action](visit.id, visit.concurrencyToken))
    } catch (reason) { await failure(reason) }
  }
  const showDetail = async (visit: VisitDto) => {
    try { setDetail(await (professionalMode ? professionalVisitsApi : visitsApi).detail(visit.id)) }
    catch (reason) { await failure(reason) }
  }
  const create = async (event: FormEvent) => {
    event.preventDefault(); setSaving(true)
    try {
      const created = await visitsApi.create({ visitorName: form.visitorName,
        professionalId: form.professionalId, roomId: form.roomId || null,
        reservationId: form.reservationId || null })
      setResult(current => ({ ...current, items: [created, ...current.items], totalCount: current.totalCount + 1 }))
      setCreating(false); setForm(emptyForm)
    } catch (reason) { await failure(reason) } finally { setSaving(false) }
  }
  const correct = async (event: FormEvent) => {
    event.preventDefault(); if (!correcting) return
    try {
      upsert(await visitsApi.correct(correcting.id, correctionStatus, correctionReason,
        correcting.concurrencyToken))
      setCorrecting(null); setCorrectionReason('')
    } catch (reason) { await failure(reason) }
  }
  const chooseReservation = (id: string) => {
    const reservation = reservations.find(item => item.id === id)
    setForm(current => ({ ...current, reservationId: id,
      professionalId: reservation?.professionalId ?? current.professionalId,
      roomId: reservation?.roomId ?? current.roomId }))
  }
  const pages = Math.max(1, Math.ceil(result.totalCount / pageSize))

  return <div className="page-enter">
    <PageHeader eyebrow="Fluxo operacional" title="Visitas"
      description={professionalMode ? 'Acompanhe e atualize seus atendimentos.' : 'Registre chegadas e acompanhe os atendimentos.'}
      action={!professionalMode && <button className="primary-button" onClick={() => setCreating(true)}>
        <Plus size={18} /> Registrar chegada
      </button>} />
    <section className="panel table-panel">
      <div className="table-toolbar visits-toolbar">
        <select className="field-input compact-select" aria-label="Status das visitas" value={status}
          onChange={event => { setStatus(event.target.value as VisitStatus | 'all'); setPage(1) }}>
          <option value="all">Todos os status</option>
          {Object.entries(statusLabels).map(([value, label]) => <option value={value} key={value}>{label}</option>)}
        </select>
        {!professionalMode && <>
          <select className="field-input compact-select" aria-label="Filtrar por profissional" value={professionalId}
            onChange={event => { setProfessionalId(event.target.value); setPage(1) }}><option value="">Todos os profissionais</option>{professionals.map(item => <option value={item.id} key={item.id}>{item.name}</option>)}</select>
          <select className="field-input compact-select" aria-label="Filtrar por sala" value={roomId}
            onChange={event => { setRoomId(event.target.value); setPage(1) }}><option value="">Todas as salas</option>{rooms.map(item => <option value={item.id} key={item.id}>{item.name}</option>)}</select>
        </>}
        <input className="field-input compact-select" type="date" aria-label="Visitas desde" value={from} onChange={event => { setFrom(event.target.value); setPage(1) }} />
        <input className="field-input compact-select" type="date" aria-label="Visitas até" value={to} onChange={event => { setTo(event.target.value); setPage(1) }} />
        <span>{result.totalCount} visitas</span>
      </div>
      {loading ? <div className="empty-state" role="status">Carregando visitas…</div>
        : error && !result.items.length ? <EmptyState>{error}</EmptyState>
          : !result.items.length ? <EmptyState><UserRoundSearch size={30} />Nenhuma visita encontrada.</EmptyState>
            : <div className="table-scroll"><table className="data-table"><thead><tr>
              <th>Visitante</th><th>Profissional</th><th>Sala</th><th>Chegada</th><th>Status</th><th>Ações</th>
            </tr></thead><tbody>{result.items.map(visit => <tr key={visit.id}>
              <td><div className="visitor-name-cell"><span>{visit.visitorName.split(' ').map(part => part[0]).slice(0, 2).join('')}</span><strong>{visit.visitorName}</strong></div></td>
              <td>{visit.professionalName}</td><td>{visit.roomName ?? 'Não informada'}</td>
              <td><span className="time-pill"><Clock3 size={14} /> {formatDate(visit.arrivedAt)}</span></td>
              <td><span className={`status-badge ${statusClasses[visit.status]}`}>{statusLabels[visit.status]}</span></td>
              <td><div className="room-admin-actions">
                <button className="secondary-button" onClick={() => void showDetail(visit)}>Detalhes</button>
                {visit.status === 'WAITING' && <button className="secondary-button" aria-label={`Iniciar atendimento de ${visit.visitorName}`} onClick={() => void transition(visit, 'start')}>Iniciar atendimento</button>}
                {visit.status === 'IN_SERVICE' && <button className="secondary-button" aria-label={`Encerrar atendimento de ${visit.visitorName}`} onClick={() => void transition(visit, 'end')}>Encerrar</button>}
                {(visit.status === 'WAITING' || visit.status === 'IN_SERVICE') && <button className="ghost-button" aria-label={`Cancelar visita de ${visit.visitorName}`} onClick={() => void transition(visit, 'cancel')}>Cancelar</button>}
                {!professionalMode && <button className="ghost-button" onClick={() => { setCorrecting(visit); setCorrectionStatus(visit.status); setCorrectionReason('') }}>Corrigir</button>}
              </div></td>
            </tr>)}</tbody></table></div>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}
      {result.totalCount > pageSize && <div className="pagination"><button className="secondary-button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>

    <Modal open={creating} onClose={() => setCreating(false)} title="Registrar chegada">
      <form className="simple-form" onSubmit={create}>
        <label className="field-label">Nome do visitante<input className="field-input" required maxLength={200} value={form.visitorName} onChange={event => setForm(current => ({ ...current, visitorName: event.target.value }))} /></label>
        <label className="field-label">Reserva (opcional)<select className="field-input" value={form.reservationId} onChange={event => chooseReservation(event.target.value)}><option value="">Sem reserva vinculada</option>{reservations.map(item => <option value={item.id} key={item.id}>{item.professionalName} · {item.roomName} · {formatDate(item.startAt)}</option>)}</select></label>
        <label className="field-label">Profissional<select className="field-input" required value={form.professionalId} onChange={event => setForm(current => ({ ...current, professionalId: event.target.value }))}><option value="">Selecione</option>{professionals.map(item => <option value={item.id} key={item.id}>{item.name}</option>)}</select></label>
        <label className="field-label">Sala<select className="field-input" value={form.roomId} onChange={event => setForm(current => ({ ...current, roomId: event.target.value }))}><option value="">Não informada</option>{rooms.map(item => <option value={item.id} key={item.id}>{item.name}</option>)}</select></label>
        <div className="modal-actions"><button type="button" className="ghost-button" onClick={() => setCreating(false)}>Cancelar</button><button className="primary-button" disabled={saving}>{saving ? 'Salvando…' : 'Registrar chegada'}</button></div>
      </form>
    </Modal>
    <Modal open={detail !== null} onClose={() => setDetail(null)} title="Detalhes da visita">{detail && <div><dl className="room-rates"><div><dt>Visitante</dt><dd>{detail.visitorName}</dd></div><div><dt>Profissional</dt><dd>{detail.professionalName}</dd></div><div><dt>Status</dt><dd>{statusLabels[detail.status]}</dd></div></dl><h3>Histórico</h3>{detail.history.map(item => <p key={item.id}>{item.previousStatus ? `${statusLabels[item.previousStatus]} → ` : ''}{statusLabels[item.newStatus]} · {formatDate(item.occurredAt)}{item.reason ? ` · ${item.reason}` : ''}</p>)}</div>}</Modal>
    <Modal open={correcting !== null} onClose={() => setCorrecting(null)} title="Correção administrativa">
      <form className="simple-form" onSubmit={correct}><label className="field-label">Estado correto<select className="field-input" value={correctionStatus} onChange={event => setCorrectionStatus(event.target.value as VisitStatus)}>{Object.entries(statusLabels).map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select></label><label className="field-label">Justificativa<textarea className="field-input" required maxLength={500} value={correctionReason} onChange={event => setCorrectionReason(event.target.value)} /></label><div className="modal-actions"><button type="button" className="ghost-button" onClick={() => setCorrecting(null)}>Cancelar</button><button className="primary-button">Aplicar correção</button></div></form>
    </Modal>
  </div>
}
