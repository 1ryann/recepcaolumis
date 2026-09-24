import { Ban, CalendarClock, CalendarDays, CircleStop, Eye, Pencil, Plus, Search } from 'lucide-react'
import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import {
  type LeaseDto, type LeaseInput, type LeaseMode, type LeaseStatus, type PagedResponse,
  leasesApi, professionalsApi, roomRentalInquiriesApi, roomsApi, tenantsApi,
  type ProfessionalDto, type RoomDto, type RoomRentalInquiryAdminDto, type TenantDto,
} from '../../api/modules'
import { EmptyState, PageHeader, countLabel } from '../../components/PageElements'
import { Modal } from '../../components/Modal'
import { parseRoomRate } from '../../features/rooms/money'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'

const pageSize = 20
const empty: PagedResponse<LeaseDto> = { items: [], page: 1, pageSize, totalCount: 0 }
// Lease statuses reuse the badge tones every other table has; `status-agendada` & co. were
// never styled, so the status rendered as bare text.
const statusTones: Record<LeaseStatus, string> = { AGENDADA: 'pending', ATIVA: 'active', ENCERRAMENTO_PENDENTE: 'waiting', ENCERRADA: 'inactive', CANCELADA: 'cancelled' }
const statusLabels: Record<LeaseStatus, string> = {
  AGENDADA: 'Agendada', ATIVA: 'Ativa', ENCERRAMENTO_PENDENTE: 'Encerramento pendente',
  ENCERRADA: 'Encerrada', CANCELADA: 'Cancelada',
}
const modeLabels: Record<LeaseMode, string> = { MONTHLY: 'Mensal', DAILY: 'Diária', HOURLY: 'Por hora' }
const formatBrl = (value: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
const formatDate = (value: string | null) => value ? new Date(value).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) : 'Sem término definido'
// Inquiry desired dates are civil dates (yyyy-MM-dd), never instants: format them without going through Date/timezones.
const civilDateLabel = (value: string) => { const [year, month, day] = value.split('-'); return `${day}/${month}/${year}` }
export const toInputDate = (value: string | null) => {
  if (!value) return ''
  const date = new Date(value)
  const pad = (part: number) => String(part).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}
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
  const [searchParams, setSearchParams] = useSearchParams()
  // Task 13: the Admin arrives here from the Interesses de locação page with ?inquiryId= (open the same
  // Nova locação modal, room preselected) or ?leaseId= (open the existing detail view). conversionInquiryId
  // is tracked separately from FormState so submit() can attach it without polluting the plain lease form.
  const [conversionInquiryId, setConversionInquiryId] = useState<string | null>(null)
  const [conversionInquiry, setConversionInquiry] = useState<RoomRentalInquiryAdminDto | null>(null)
  const [rawSearch, setRawSearch] = useState('')
  const search = useDebouncedValue(rawSearch.trim().replace(/\s+/g, ' ') || undefined)
  const [status, setStatus] = useState<LeaseStatus | 'all'>('all')
  const [professionalFilter, setProfessionalFilter] = useState('')
  const [roomFilter, setRoomFilter] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [formLease, setFormLease] = useState<LeaseDto | null | undefined>(undefined)
  const [form, setForm] = useState<FormState>(emptyForm)
  const [tenants, setTenants] = useState<TenantDto[]>([])
  const [professionals, setProfessionals] = useState<ProfessionalDto[]>([])
  const [rooms, setRooms] = useState<RoomDto[]>([])
  const [saving, setSaving] = useState(false)
  const [detail, setDetail] = useState<LeaseDto | null>(null)
  const [postpone, setPostpone] = useState<LeaseDto | null>(null)
  const [postponeAt, setPostponeAt] = useState('')
  const [endLease, setEndLease] = useState<LeaseDto | null>(null)
  const [endAt, setEndAt] = useState('')
  const [newTenant, setNewTenant] = useState(false)
  const [tenantName, setTenantName] = useState('')
  const [tenantKind, setTenantKind] = useState<'INDIVIDUAL' | 'LEGAL_ENTITY'>('INDIVIDUAL')

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      setError(null)
      setResult(await leasesApi.list({ search, status, page, pageSize,
        professionalId: professionalFilter || undefined, roomId: roomFilter || undefined }, signal))
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError') setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as locações.')
    }
  }, [page, professionalFilter, roomFilter, search, status])
  useEffect(() => {
    const controller = new AbortController()
    setLoading(result.items.length === 0); setRefreshing(result.items.length > 0)
    void load(controller.signal).finally(() => { if (!controller.signal.aborted) { setLoading(false); setRefreshing(false) } })
    return () => controller.abort()
  }, [load])
  useEffect(() => {
    const controller = new AbortController()
    const query = { status: 'all' as const, page: 1, pageSize: 100 }
    void Promise.all([professionalsApi.list(query, controller.signal), roomsApi.list(query, controller.signal)])
      .then(([professionalPage, roomPage]) => { setProfessionals(professionalPage.items); setRooms(roomPage.items) })
      .catch(reason => { if ((reason as DOMException).name !== 'AbortError') void resolveFailure(reason) })
    return () => controller.abort()
  }, [])

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
    // Opening the plain "Nova locação"/"Editar" flow always leaves any prior inquiry-conversion context
    // behind; the ?inquiryId= effect below re-establishes it right after when that is how we got here.
    setConversionInquiryId(null); setConversionInquiry(null)
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
  // Closing without saving abandons the conversion too: drop ?inquiryId= so a refresh does not reopen it.
  const closeForm = () => {
    if (conversionInquiryId !== null || searchParams.has('inquiryId')) clearInquiryParam()
    setFormLease(undefined); setConversionInquiryId(null); setConversionInquiry(null)
  }
  const clearInquiryParam = () => setSearchParams(current => {
    const next = new URLSearchParams(current); next.delete('inquiryId'); return next
  }, { replace: true })
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const rate = parseRoomRate(form.contractedRate)
    if (rate === null) { setError('Informe um valor contratado válido, com no máximo duas casas decimais.'); return }
    const isConversion = conversionInquiryId !== null && !formLease
    const input: LeaseInput = {
      tenantId: form.tenantId, professionalId: form.professionalId, roomId: form.roomId, mode: form.mode,
      contractedRate: rate, billingStartAt: toIso(form.billingStartAt),
      billingDueDay: form.billingDueDay ? Number(form.billingDueDay) : null,
      occupancyStartAt: toIso(form.occupancyStartAt), occupancyEndAt: form.occupancyEndAt ? toIso(form.occupancyEndAt) : null,
      ...(isConversion ? { roomRentalInquiryId: conversionInquiryId } : {}),
    }
    setSaving(true)
    try {
      if (formLease) upsert(await leasesApi.update(formLease.id, { ...input, concurrencyToken: formLease.concurrencyToken }))
      else {
        const created = await leasesApi.create(input)
        setResult(current => ({ ...current, items: [created, ...current.items], totalCount: current.totalCount + 1 }))
        if (isConversion) {
          clearInquiryParam(); setConversionInquiryId(null); setConversionInquiry(null); setDetail(created)
        }
      }
      setFormLease(undefined)
    } catch (reason) { await resolveFailure(reason) } finally { setSaving(false) }
  }
  const cancel = async (lease: LeaseDto) => {
    try { upsert(await leasesApi.cancel(lease.id, lease.concurrencyToken)) } catch (reason) { await resolveFailure(reason) }
  }
  const saveEnd = async (event: FormEvent) => {
    event.preventDefault(); if (!endLease) return
    try { upsert(await leasesApi.end(endLease.id, endAt ? toIso(endAt) : null, endLease.concurrencyToken)); setEndLease(null) }
    catch (reason) { await resolveFailure(reason) }
  }
  const openNewTenant = () => {
    // Editable prefill only: the inquiry's FullName seeds the initial value, never its Kind — the Admin
    // always chooses INDIVIDUAL/LEGAL_ENTITY explicitly.
    setTenantName(conversionInquiry?.fullName ?? ''); setNewTenant(true)
  }
  const createTenant = async (event: FormEvent) => {
    event.preventDefault()
    try {
      const created = await tenantsApi.create({ name: tenantName, kind: tenantKind })
      setTenants(current => [...current, created]); setForm(current => ({ ...current, tenantId: created.id }))
      setNewTenant(false); setTenantName('')
    } catch (reason) { await resolveFailure(reason) }
  }
  const savePostpone = async (event: FormEvent) => {
    event.preventDefault(); if (!postpone) return
    try { upsert(await leasesApi.postpone(postpone.id, toIso(postponeAt), postpone.concurrencyToken)); setPostpone(null) }
    catch (reason) { await resolveFailure(reason) }
  }
  const showDetail = async (lease: LeaseDto) => {
    try { setDetail(await leasesApi.detail(lease.id)) } catch (reason) { await resolveFailure(reason) }
  }
  // ?leaseId=: open the existing detail view fetched from the server — no second detail UI.
  const leaseIdParam = searchParams.get('leaseId')
  useEffect(() => {
    if (!leaseIdParam) return
    let active = true
    void leasesApi.detail(leaseIdParam)
      .then(found => { if (active) setDetail(found) })
      .catch(reason => { if (active) void resolveFailure(reason) })
    return () => { active = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leaseIdParam])
  // ?inquiryId=: reuse the same "Nova locação" modal/form with the requested room preselected (still
  // editable) — no parallel contract form, no inference of Professional/Kind/contractual terms.
  const inquiryIdParam = searchParams.get('inquiryId')
  useEffect(() => {
    if (!inquiryIdParam) return
    let active = true
    void (async () => {
      try {
        const found = await roomRentalInquiriesApi.get(inquiryIdParam)
        if (!active) return
        // Already converted: never offer a second conversion — show the lease it produced instead.
        // Clear ?inquiryId= only after the detail is shown: clearing it re-runs this effect, whose cleanup
        // marks this run inactive and would discard the lease detail still in flight.
        if (found.status === 'CONVERTED') {
          setNotice('Este interesse já foi convertido em uma locação.')
          try {
            if (found.leaseId) {
              const converted = await leasesApi.detail(found.leaseId)
              if (active) setDetail(converted)
            }
          } finally { if (active) clearInquiryParam() }
          return
        }
        await openForm(null)
        if (!active) return
        setForm(current => ({ ...current, roomId: found.roomId }))
        setConversionInquiryId(found.id)
        setConversionInquiry(found)
      } catch (reason) { if (active) await resolveFailure(reason) }
    })()
    return () => { active = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [inquiryIdParam])
  const pages = Math.max(1, Math.ceil(result.totalCount / pageSize))

  return <div className="page-enter">
    <PageHeader eyebrow="Ocupação contratual" tour="pagina-locacoes" title="Locações" description="Gerencie contratos e períodos de ocupação das salas."
      action={<button className="primary-button" onClick={() => void openForm(null)}><Plus size={18} /> Nova locação</button>} />
    <section className="panel table-panel">
      <div className="table-toolbar"><div className="search-field"><Search size={18} /><input value={rawSearch} onChange={event => { setRawSearch(event.target.value); setPage(1) }} placeholder="Buscar por locatário, profissional ou sala" aria-label="Buscar locações" /></div>
        <select className="field-input compact-select" value={status} aria-label="Status das locações" onChange={event => { setStatus(event.target.value as LeaseStatus | 'all'); setPage(1) }}>
          <option value="all">Todos os status</option>{Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select>
        <select className="field-input compact-select" value={professionalFilter} aria-label="Filtrar por profissional" onChange={event => { setProfessionalFilter(event.target.value); setPage(1) }}><option value="">Todos os profissionais</option>{professionals.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>
        <select className="field-input compact-select" value={roomFilter} aria-label="Filtrar por sala" onChange={event => { setRoomFilter(event.target.value); setPage(1) }}><option value="">Todas as salas</option>{rooms.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>
        <span>{countLabel(result.totalCount, 'locação', 'locações')}</span></div>
      {loading ? <div className="empty-state" role="status">Carregando locações…</div>
        : error && result.items.length === 0 ? <EmptyState><p>{error}</p><button className="secondary-button" onClick={() => void refresh()}>Tentar novamente</button></EmptyState>
          : result.items.length === 0 ? <EmptyState>Nenhuma locação encontrada.</EmptyState>
            : <div className="table-scroll"><table className="data-table leases-table"><thead><tr><th>Locatário</th><th>Profissional</th><th>Sala</th><th>Modalidade</th><th>Período</th><th>Valor</th><th>Status</th><th className="actions-column">Ações</th></tr></thead><tbody>
              {result.items.map(lease => <tr key={lease.id}><td><strong>{lease.tenantName}</strong></td><td>{lease.professionalName}</td><td>{lease.roomName}</td><td>{modeLabels[lease.mode]}</td><td><span className="date-cell"><CalendarDays size={16} />{formatDate(lease.occupancyStartAt)} — {formatDate(lease.occupancyEndAt)}</span></td><td className="money-cell">{formatBrl(lease.contractedRate)}</td><td><span className={`status-badge status-${statusTones[lease.status]}`}><i />{statusLabels[lease.status]}</span></td><td><div className="row-actions">
                <button type="button" title="Detalhes" aria-label={`Detalhes da locação ${lease.tenantName}`} onClick={() => void showDetail(lease)}><Eye size={17} /></button>
                {lease.status === 'AGENDADA' && <><button type="button" title="Editar" aria-label={`Editar locação ${lease.tenantName}`} onClick={() => void openForm(lease)}><Pencil size={17} /></button><button type="button" title="Postergar ocupação" aria-label={`Postergar locação ${lease.tenantName}`} onClick={() => { setPostpone(lease); setPostponeAt(toInputDate(lease.occupancyStartAt)) }}><CalendarClock size={17} /></button><button type="button" title="Cancelar locação" aria-label={`Cancelar locação ${lease.tenantName}`} onClick={() => void cancel(lease)}><Ban size={17} /></button></>}
                {lease.status === 'ATIVA' && <button type="button" title="Encerrar locação" aria-label={`Encerrar locação ${lease.tenantName}`} onClick={() => { setEndLease(lease); setEndAt('') }}><CircleStop size={17} /></button>}
              </div></td></tr>)}
            </tbody></table></div>}
      {notice && <p className="form-hint" role="status">{notice}</p>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}{refreshing && <p className="list-refreshing" role="status">Atualizando lista…</p>}
      {result.totalCount > pageSize && <div className="pagination"><button className="secondary-button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>

    <Modal open={formLease !== undefined} onClose={closeForm} title={formLease ? 'Editar locação' : 'Nova locação'} subtitle="Informe o contrato e o período de ocupação." size="large">
      <form className="simple-form" onSubmit={submit}>
        {conversionInquiry && <p className="form-hint">Interesse registrado: {conversionInquiry.presentedAvailabilityLabel}</p>}
        {conversionInquiry?.desiredStartDate && conversionInquiry.desiredEndDate && <p className="form-hint">Período desejado: {civilDateLabel(conversionInquiry.desiredStartDate)} até {civilDateLabel(conversionInquiry.desiredEndDate)}</p>}
        <div className="fields-area full-fields">
        <label className="field-label">Locatário<select className="field-input" required value={form.tenantId} onChange={event => setForm(current => ({ ...current, tenantId: event.target.value }))}><option value="">Selecione</option>{tenants.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <button className="secondary-button field-inline-action" type="button" onClick={openNewTenant}>Novo locatário</button>
        <label className="field-label">Profissional<select className="field-input" required value={form.professionalId} onChange={event => setForm(current => ({ ...current, professionalId: event.target.value }))}><option value="">Selecione</option>{professionals.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label className="field-label">Sala<select className="field-input" required value={form.roomId} onChange={event => setForm(current => ({ ...current, roomId: event.target.value }))}><option value="">Selecione</option>{rooms.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label className="field-label">Modalidade<select className="field-input" value={form.mode} onChange={event => setForm(current => ({ ...current, mode: event.target.value as LeaseMode }))}><option value="HOURLY">Por hora</option><option value="DAILY">Diária</option><option value="MONTHLY">Mensal</option></select></label>
        <label className="field-label">Valor contratado<input className="field-input" required inputMode="decimal" value={form.contractedRate} onChange={event => setForm(current => ({ ...current, contractedRate: event.target.value }))} /></label>
        <label className="field-label">Dia de vencimento<input className="field-input" type="number" min="1" max="31" value={form.billingDueDay} onChange={event => setForm(current => ({ ...current, billingDueDay: event.target.value }))} /></label>
        <label className="field-label">Início da cobrança<input className="field-input" required type="datetime-local" value={form.billingStartAt} onChange={event => setForm(current => ({ ...current, billingStartAt: event.target.value }))} /></label>
        <label className="field-label">Início da ocupação<input className="field-input" required type="datetime-local" value={form.occupancyStartAt} onChange={event => setForm(current => ({ ...current, occupancyStartAt: event.target.value }))} /></label>
        <label className="field-label">Fim da ocupação<input className="field-input" required={form.mode !== 'MONTHLY'} type="datetime-local" value={form.occupancyEndAt} onChange={event => setForm(current => ({ ...current, occupancyEndAt: event.target.value }))} /></label>
        </div><div className="modal-actions"><button className="ghost-button" type="button" onClick={closeForm}>Cancelar</button><button className="primary-button" disabled={saving} type="submit">{saving ? 'Salvando…' : formLease ? 'Salvar alterações' : 'Cadastrar locação'}</button></div></form>
    </Modal>
    <Modal open={detail !== null} onClose={() => setDetail(null)} title="Detalhes da locação">{detail && <dl className="room-rates"><div><dt>Locatário</dt><dd>{detail.tenantName}</dd></div><div><dt>Profissional</dt><dd>{detail.professionalName}</dd></div><div><dt>Sala</dt><dd>{detail.roomName}</dd></div><div><dt>Status</dt><dd>{statusLabels[detail.status]}</dd></div></dl>}</Modal>
    <Modal open={postpone !== null} onClose={() => setPostpone(null)} title="Postergar ocupação"><form className="simple-form" onSubmit={savePostpone}><label className="field-label">Novo início da ocupação<input className="field-input" required type="datetime-local" value={postponeAt} onChange={event => setPostponeAt(event.target.value)} /></label><div className="modal-actions"><button className="ghost-button" type="button" onClick={() => setPostpone(null)}>Cancelar</button><button className="primary-button" type="submit">Confirmar postergação</button></div></form></Modal>
    <Modal open={endLease !== null} onClose={() => setEndLease(null)} title="Encerrar locação" subtitle="Deixe a data vazia para encerramento imediato."><form className="simple-form" onSubmit={saveEnd}><label className="field-label">Data de encerramento<input className="field-input" type="datetime-local" value={endAt} onChange={event => setEndAt(event.target.value)} /></label><div className="modal-actions"><button className="ghost-button" type="button" onClick={() => setEndLease(null)}>Cancelar</button><button className="primary-button" type="submit">Confirmar encerramento</button></div></form></Modal>
    <Modal open={newTenant} onClose={() => setNewTenant(false)} title="Novo locatário"><form className="simple-form" onSubmit={createTenant}><label className="field-label">Nome do locatário<input className="field-input" required maxLength={200} value={tenantName} onChange={event => setTenantName(event.target.value)} /></label><label className="field-label">Tipo<select className="field-input" value={tenantKind} onChange={event => setTenantKind(event.target.value as typeof tenantKind)}><option value="INDIVIDUAL">Pessoa física</option><option value="LEGAL_ENTITY">Pessoa jurídica</option></select></label><div className="modal-actions"><button className="ghost-button" type="button" onClick={() => setNewTenant(false)}>Cancelar</button><button className="primary-button" type="submit">Cadastrar locatário</button></div></form></Modal>
  </div>
}
