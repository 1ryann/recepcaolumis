import { CalendarDays, Pencil, Plus, Search } from 'lucide-react'
import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import {
  type LeaseDto, type LeaseInput, type LeaseMode, type LeaseStatus, type PagedResponse,
  leasesApi, professionalsApi, roomsApi, tenantsApi, type ProfessionalDto, type RoomDto, type TenantDto,
} from '../../api/modules'
import { EmptyState, PageHeader } from '../../components/PageElements'
import { Modal } from '../../components/Modal'
import { parseRoomRate } from '../../features/rooms/money'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'

const pageSize = 20
const empty: PagedResponse<LeaseDto> = { items: [], page: 1, pageSize, totalCount: 0 }
const statusLabels: Record<LeaseStatus, string> = {
  AGENDADA: 'Agendada', ATIVA: 'Ativa', ENCERRAMENTO_PENDENTE: 'Encerramento pendente',
  ENCERRADA: 'Encerrada', CANCELADA: 'Cancelada',
}
const modeLabels: Record<LeaseMode, string> = { MONTHLY: 'Mensal', DAILY: 'Diária', HOURLY: 'Por hora' }
const formatBrl = (value: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
const formatDate = (value: string | null) => value ? new Date(value).toLocaleString('pt-BR') : 'Sem término definido'
const toInputDate = (value: string | null) => value ? value.slice(0, 16) : ''
const toIso = (value: string) => new Date(value).toISOString()

type FormState = {
  tenantId: string, professionalId: string, roomId: string, mode: LeaseMode, contractedRate: string,
  billingStartAt: string, billingDueDay: string, occupancyStartAt: string, occupancyEndAt: string,
}
const emptyForm: FormState = {
  tenantId: '', professionalId: '', roomId: '', mode: 'HOURLY', contractedRate: '',
  billingStartAt: '', billingDueDay: '', occupancyStartAt: '', occupancyEndAt: '',
}

export function Leases() {
  const [rawSearch, setRawSearch] = useState('')
  const search = useDebouncedValue(rawSearch.trim().replace(/\s+/g, ' ') || undefined)
  const [status, setStatus] = useState<LeaseStatus | 'all'>('all')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [formLease, setFormLease] = useState<LeaseDto | null | undefined>(undefined)
  const [form, setForm] = useState<FormState>(emptyForm)
  const [tenants, setTenants] = useState<TenantDto[]>([])
  const [professionals, setProfessionals] = useState<ProfessionalDto[]>([])
  const [rooms, setRooms] = useState<RoomDto[]>([])
  const [saving, setSaving] = useState(false)
  const [detail, setDetail] = useState<LeaseDto | null>(null)
  const [postpone, setPostpone] = useState<LeaseDto | null>(null)
  const [postponeAt, setPostponeAt] = useState('')

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      setError(null)
      setResult(await leasesApi.list({ search, status, page, pageSize }, signal))
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError') setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as locações.')
    }
  }, [page, search, status])
  useEffect(() => {
    const controller = new AbortController()
    setLoading(result.items.length === 0); setRefreshing(result.items.length > 0)
    void load(controller.signal).finally(() => { if (!controller.signal.aborted) { setLoading(false); setRefreshing(false) } })
    return () => controller.abort()
  }, [load])

  const refresh = () => load()
  const upsert = (lease: LeaseDto) => setResult(current => ({
    ...current, items: current.items.map(item => item.id === lease.id ? lease : item),
  }))
  const resolveFailure = async (reason: unknown) => {
    if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') {
      await refresh(); setError('Esta locação foi alterada por outra operação. Recarregamos os dados para você tentar novamente.'); return
    }
    setError(reason instanceof Error ? reason.message : 'Não foi possível concluir a operação.')
  }
  const openForm = async (lease: LeaseDto | null) => {
    setFormLease(lease)
    setForm(lease ? {
      tenantId: lease.tenantId, professionalId: lease.professionalId, roomId: lease.roomId, mode: lease.mode,
      contractedRate: String(lease.contractedRate).replace('.', ','), billingStartAt: toInputDate(lease.billingStartAt),
      billingDueDay: lease.billingDueDay?.toString() ?? '', occupancyStartAt: toInputDate(lease.occupancyStartAt),
      occupancyEndAt: toInputDate(lease.occupancyEndAt),
    } : emptyForm)
    try {
      const query = { status: 'active' as const, page: 1, pageSize: 100 }
      const [tenantPage, professionalPage, roomPage] = await Promise.all([
        tenantsApi.list(query), professionalsApi.list(query), roomsApi.list(query),
      ])
      setTenants(tenantPage.items); setProfessionals(professionalPage.items); setRooms(roomPage.items)
    } catch (reason) { await resolveFailure(reason) }
  }
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const rate = parseRoomRate(form.contractedRate)
    if (rate === null) { setError('Informe um valor contratado válido, com no máximo duas casas decimais.'); return }
    const input: LeaseInput = {
      tenantId: form.tenantId, professionalId: form.professionalId, roomId: form.roomId, mode: form.mode,
      contractedRate: rate, billingStartAt: toIso(form.billingStartAt),
      billingDueDay: form.billingDueDay ? Number(form.billingDueDay) : null,
      occupancyStartAt: toIso(form.occupancyStartAt), occupancyEndAt: form.occupancyEndAt ? toIso(form.occupancyEndAt) : null,
    }
    setSaving(true)
    try {
      if (formLease) upsert(await leasesApi.update(formLease.id, { ...input, concurrencyToken: formLease.concurrencyToken }))
      else {
        const created = await leasesApi.create(input)
        setResult(current => ({ ...current, items: [created, ...current.items], totalCount: current.totalCount + 1 }))
      }
      setFormLease(undefined)
    } catch (reason) { await resolveFailure(reason) } finally { setSaving(false) }
  }
  const cancel = async (lease: LeaseDto) => {
    try { upsert(await leasesApi.cancel(lease.id, lease.concurrencyToken)) } catch (reason) { await resolveFailure(reason) }
  }
  const endNow = async (lease: LeaseDto) => {
    try { upsert(await leasesApi.end(lease.id, null, lease.concurrencyToken)) } catch (reason) { await resolveFailure(reason) }
  }
  const savePostpone = async (event: FormEvent) => {
    event.preventDefault(); if (!postpone) return
    try { upsert(await leasesApi.postpone(postpone.id, toIso(postponeAt), postpone.concurrencyToken)); setPostpone(null) }
    catch (reason) { await resolveFailure(reason) }
  }
  const showDetail = async (lease: LeaseDto) => {
    try { setDetail(await leasesApi.detail(lease.id)) } catch (reason) { await resolveFailure(reason) }
  }
  const pages = Math.max(1, Math.ceil(result.totalCount / pageSize))

  return <div className="page-enter">
    <PageHeader eyebrow="Ocupação contratual" title="Locações" description="Gerencie contratos e períodos de ocupação das salas."
      action={<button className="primary-button" onClick={() => void openForm(null)}><Plus size={18} /> Nova locação</button>} />
    <section className="panel table-panel">
      <div className="table-toolbar"><div className="search-field"><Search size={18} /><input value={rawSearch} onChange={event => { setRawSearch(event.target.value); setPage(1) }} placeholder="Buscar por locatário, profissional ou sala" aria-label="Buscar locações" /></div>
        <select className="field-input compact-select" value={status} aria-label="Status das locações" onChange={event => { setStatus(event.target.value as LeaseStatus | 'all'); setPage(1) }}>
          <option value="all">Todos os status</option>{Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select><span>{result.totalCount} locações</span></div>
      {loading ? <div className="empty-state" role="status">Carregando locações…</div>
        : error && result.items.length === 0 ? <EmptyState><p>{error}</p><button className="secondary-button" onClick={() => void refresh()}>Tentar novamente</button></EmptyState>
          : result.items.length === 0 ? <EmptyState>Nenhuma locação encontrada.</EmptyState>
            : <div className="table-scroll"><table className="data-table"><thead><tr><th>Locatário</th><th>Profissional</th><th>Sala</th><th>Modalidade</th><th>Período</th><th>Valor</th><th>Status</th><th>Ações</th></tr></thead><tbody>
              {result.items.map(lease => <tr key={lease.id}><td><strong>{lease.tenantName}</strong></td><td>{lease.professionalName}</td><td>{lease.roomName}</td><td>{modeLabels[lease.mode]}</td><td><span className="date-cell"><CalendarDays size={16} />{formatDate(lease.occupancyStartAt)} — {formatDate(lease.occupancyEndAt)}</span></td><td className="money-cell">{formatBrl(lease.contractedRate)}</td><td><span className={`status-badge status-${lease.status.toLowerCase()}`}>{statusLabels[lease.status]}</span></td><td><div className="room-admin-actions">
                <button className="secondary-button" onClick={() => void showDetail(lease)}>Detalhes</button>
                {lease.status === 'AGENDADA' && <><button className="secondary-button" aria-label={`Editar locação ${lease.tenantName}`} onClick={() => void openForm(lease)}><Pencil size={15} /> Editar</button><button className="ghost-button" onClick={() => { setPostpone(lease); setPostponeAt(toInputDate(lease.occupancyStartAt)) }}>Postergar</button><button className="ghost-button" aria-label={`Cancelar locação ${lease.tenantName}`} onClick={() => void cancel(lease)}>Cancelar</button></>}
                {lease.status === 'ATIVA' && <button className="ghost-button" onClick={() => void endNow(lease)}>Encerrar agora</button>}
              </div></td></tr>)}
            </tbody></table></div>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}{refreshing && <p className="list-refreshing" role="status">Atualizando lista…</p>}
      {result.totalCount > pageSize && <div className="pagination"><button className="secondary-button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>

    <Modal open={formLease !== undefined} onClose={() => setFormLease(undefined)} title={formLease ? 'Editar locação' : 'Nova locação'} subtitle="Informe o contrato e o período de ocupação." size="large">
      <form className="simple-form" onSubmit={submit}><div className="fields-area full-fields">
        <label className="field-label">Locatário<select className="field-input" required value={form.tenantId} onChange={event => setForm(current => ({ ...current, tenantId: event.target.value }))}><option value="">Selecione</option>{tenants.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label className="field-label">Profissional<select className="field-input" required value={form.professionalId} onChange={event => setForm(current => ({ ...current, professionalId: event.target.value }))}><option value="">Selecione</option>{professionals.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label className="field-label">Sala<select className="field-input" required value={form.roomId} onChange={event => setForm(current => ({ ...current, roomId: event.target.value }))}><option value="">Selecione</option>{rooms.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label className="field-label">Modalidade<select className="field-input" value={form.mode} onChange={event => setForm(current => ({ ...current, mode: event.target.value as LeaseMode }))}><option value="HOURLY">Por hora</option><option value="DAILY">Diária</option><option value="MONTHLY">Mensal</option></select></label>
        <label className="field-label">Valor contratado<input className="field-input" required inputMode="decimal" value={form.contractedRate} onChange={event => setForm(current => ({ ...current, contractedRate: event.target.value }))} /></label>
        <label className="field-label">Dia de vencimento<input className="field-input" type="number" min="1" max="31" value={form.billingDueDay} onChange={event => setForm(current => ({ ...current, billingDueDay: event.target.value }))} /></label>
        <label className="field-label">Início da cobrança<input className="field-input" required type="datetime-local" value={form.billingStartAt} onChange={event => setForm(current => ({ ...current, billingStartAt: event.target.value }))} /></label>
        <label className="field-label">Início da ocupação<input className="field-input" required type="datetime-local" value={form.occupancyStartAt} onChange={event => setForm(current => ({ ...current, occupancyStartAt: event.target.value }))} /></label>
        <label className="field-label">Fim da ocupação<input className="field-input" required={form.mode !== 'MONTHLY'} type="datetime-local" value={form.occupancyEndAt} onChange={event => setForm(current => ({ ...current, occupancyEndAt: event.target.value }))} /></label>
      </div><div className="modal-actions"><button className="ghost-button" type="button" onClick={() => setFormLease(undefined)}>Cancelar</button><button className="primary-button" disabled={saving} type="submit">{saving ? 'Salvando…' : formLease ? 'Salvar alterações' : 'Cadastrar locação'}</button></div></form>
    </Modal>
    <Modal open={detail !== null} onClose={() => setDetail(null)} title="Detalhes da locação">{detail && <dl className="room-rates"><div><dt>Locatário</dt><dd>{detail.tenantName}</dd></div><div><dt>Profissional</dt><dd>{detail.professionalName}</dd></div><div><dt>Sala</dt><dd>{detail.roomName}</dd></div><div><dt>Status</dt><dd>{statusLabels[detail.status]}</dd></div></dl>}</Modal>
    <Modal open={postpone !== null} onClose={() => setPostpone(null)} title="Postergar ocupação"><form className="simple-form" onSubmit={savePostpone}><label className="field-label">Novo início da ocupação<input className="field-input" required type="datetime-local" value={postponeAt} onChange={event => setPostponeAt(event.target.value)} /></label><div className="modal-actions"><button className="ghost-button" type="button" onClick={() => setPostpone(null)}>Cancelar</button><button className="primary-button" type="submit">Confirmar postergação</button></div></form></Modal>
  </div>
}
