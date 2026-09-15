import { Building2, Images, Pencil, Plus, Search, UserMinus, UserPlus } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { type ModuleStatus, type PagedResponse, type RoomDto, roomsApi } from '../../api/modules'
import { Modal } from '../../components/Modal'
import { EmptyState, PageHeader, StatusBadge } from '../../components/PageElements'
import { RoomForm } from '../../features/rooms/RoomForm'
import { RoomPhotoManager } from '../../features/rooms/RoomPhotoManager'
import { formatBrl } from '../../features/rooms/money'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'

const pageSize = 20
const empty: PagedResponse<RoomDto> = { items: [], page: 1, pageSize, totalCount: 0 }

export function Rooms() {
  const [rawSearch, setRawSearch] = useState('')
  const search = useDebouncedValue(rawSearch.trim().replace(/\s+/g, ' ') || undefined)
  const [status, setStatus] = useState<ModuleStatus>('all')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [formRoom, setFormRoom] = useState<RoomDto | null | undefined>(undefined)
  const [saving, setSaving] = useState(false)
  const [photoRoom, setPhotoRoom] = useState<RoomDto | null>(null)
  const photoTriggerRef = useRef<HTMLButtonElement | null>(null)
  const closePhotoManager = () => { setPhotoRoom(null); photoTriggerRef.current?.focus() }
  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      setError(null)
      setResult(await roomsApi.list({ search, status, page, pageSize }, signal))
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError') setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as salas.')
    }
  }, [page, search, status])
  useEffect(() => {
    const controller = new AbortController()
    setLoading(result.items.length === 0); setRefreshing(result.items.length > 0)
    void load(controller.signal).finally(() => { if (!controller.signal.aborted) { setLoading(false); setRefreshing(false) } })
    return () => controller.abort()
  }, [load])

  const refresh = async () => { await load() }
  const upsert = (room: RoomDto) => setResult(current => ({ ...current, items: current.items.map(item => item.id === room.id ? room : item) }))
  const resolveFailure = async (reason: unknown) => {
    if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') {
      await refresh(); setError('Este registro foi alterado por outra operação. Recarregamos os dados para você tentar novamente.'); return
    }
    throw reason
  }
  const save = async (input: { name: string, description: string | null, hourlyRate: number, dailyRate: number }) => {
    setSaving(true)
    try {
      if (formRoom) upsert(await roomsApi.update(formRoom.id, { ...input, concurrencyToken: formRoom.concurrencyToken }))
      else {
        const created = await roomsApi.create(input)
        setResult(current => ({ ...current, items: [created, ...current.items], totalCount: current.totalCount + 1 }))
      }
      setFormRoom(undefined)
    } catch (reason) { await resolveFailure(reason) } finally { setSaving(false) }
  }
  const toggle = async (room: RoomDto) => {
    setSaving(true)
    try { upsert(await roomsApi.changeStatus(room.id, !room.isActive, room.concurrencyToken)) }
    catch (reason) {
      if (reason instanceof ApiError && reason.code === 'ROOM_NAME_ALREADY_EXISTS') { setError(reason.message) }
      else await resolveFailure(reason)
    } finally { setSaving(false) }
  }
  const start = result.totalCount ? ((result.page - 1) * result.pageSize) + 1 : 0
  const end = Math.min(result.page * result.pageSize, result.totalCount)
  const pages = Math.max(1, Math.ceil(result.totalCount / result.pageSize))

  return <div className="page-enter">
    <PageHeader eyebrow="Espaços do edifício" title="Salas" description="Cadastre os espaços e as tarifas disponíveis para uso."
      action={<button className="primary-button" onClick={() => setFormRoom(null)}><Plus size={18} /> Nova sala</button>} />
    <section className="panel table-panel">
      <div className="table-toolbar"><div className="search-field"><Search size={18} /><input value={rawSearch} onChange={event => { setRawSearch(event.target.value); setPage(1) }} placeholder="Buscar por nome" aria-label="Buscar salas" /></div>
        <select className="field-input compact-select" value={status} aria-label="Status das salas" onChange={event => { setStatus(event.target.value as ModuleStatus); setPage(1) }}><option value="all">Todos os status</option><option value="active">Ativas</option><option value="inactive">Inativas</option></select>
        <span>{start}–{end} de {result.totalCount} salas</span></div>
      {loading ? <div className="empty-state" role="status">Carregando salas…</div>
        : error && result.items.length === 0 ? <EmptyState><p>{error}</p><button className="secondary-button" onClick={() => void refresh()}>Tentar novamente</button></EmptyState>
          : result.items.length === 0 ? <EmptyState>Nenhuma sala encontrada.</EmptyState>
            : <div className="rooms-admin-grid">{result.items.map(room => <article className="room-admin-card" key={room.id}>
              <div className="room-admin-heading"><span className="room-admin-icon"><Building2 size={20} /></span><div><strong>{room.name}</strong><StatusBadge status={room.isActive ? 'active' : 'inactive'} /></div></div>
              <p>{room.description || 'Sem descrição cadastrada.'}</p>
              <dl className="room-rates"><div><dt>Por hora</dt><dd>{formatBrl(room.hourlyRate)}</dd></div><div><dt>Diária</dt><dd>{formatBrl(room.dailyRate)}</dd></div></dl>
              <div className="room-admin-actions"><button className="secondary-button" onClick={() => setFormRoom(room)} aria-label={`Editar ${room.name}`}><Pencil size={16} /> Editar</button><button className="secondary-button" onClick={(event) => { photoTriggerRef.current = event.currentTarget; setPhotoRoom(room) }} aria-label={`Gerenciar fotos de ${room.name}`}><Images size={16} /> Fotos</button><button className="ghost-button" disabled={saving} onClick={() => void toggle(room)} aria-label={`${room.isActive ? 'Desativar' : 'Ativar'} ${room.name}`}>{room.isActive ? <UserMinus size={16} /> : <UserPlus size={16} />}{room.isActive ? 'Desativar' : 'Ativar'}</button></div>
            </article>)}</div>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}{refreshing && <p className="list-refreshing" role="status">Atualizando lista…</p>}
      {result.totalCount > pageSize && <div className="pagination"><button className="secondary-button" disabled={page <= 1 || refreshing} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages || refreshing} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>
    <Modal open={formRoom !== undefined} onClose={() => setFormRoom(undefined)} title={formRoom ? 'Editar sala' : 'Nova sala'} subtitle="As tarifas são informadas em reais e enviadas como números." size="large"><RoomForm room={formRoom ?? null} pending={saving} onCancel={() => setFormRoom(undefined)} onSubmit={save} /></Modal>
    <Modal open={photoRoom !== null} onClose={closePhotoManager} title={photoRoom ? `Fotos — ${photoRoom.name}` : 'Fotos da sala'} size="large">{photoRoom && <RoomPhotoManager room={photoRoom} onClose={closePhotoManager} />}</Modal>
  </div>
}
