import { Search, UserMinus, UserPlus } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { type CustomerAdministrationDto, customersAdministrationApi, type ModuleStatus, type PagedResponse } from '../../api/modules'
import { EmptyState, PageHeader, StatusBadge, countLabel } from '../../components/PageElements'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'
import { displayWhatsApp } from '../../utils/whatsappMask'

const pageSize = 20
const empty: PagedResponse<CustomerAdministrationDto> = { items: [], page: 1, pageSize, totalCount: 0 }

function optInLabel(customer: CustomerAdministrationDto) {
  return customer.whatsAppOptIn.status === 'GRANTED' ? 'Aceita avisos' : 'Sem aceite'
}

export function Customers() {
  const [rawSearch, setRawSearch] = useState('')
  const search = useDebouncedValue(rawSearch.trim().replace(/\s+/g, ' ') || undefined)
  const [status, setStatus] = useState<ModuleStatus>('all')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      setError(null)
      setResult(await customersAdministrationApi.list({ search, status, page, pageSize }, signal))
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError') setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os clientes.')
    }
  }, [page, search, status])

  useEffect(() => {
    const controller = new AbortController()
    setLoading(result.items.length === 0); setRefreshing(result.items.length > 0)
    void load(controller.signal).finally(() => { if (!controller.signal.aborted) { setLoading(false); setRefreshing(false) } })
    return () => controller.abort()
  }, [load])

  const toggleStatus = async (customer: CustomerAdministrationDto) => {
    setSaving(true)
    try {
      const updated = await customersAdministrationApi.changeStatus(customer.id, !customer.isActive, customer.concurrencyToken)
      setResult(current => ({ ...current, items: current.items.map(item => item.id === updated.id ? updated : item) }))
    } catch (reason) {
      if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') {
        await load()
        setError('Este cadastro foi alterado por outra operação. Recarregamos os dados para você tentar novamente.')
      } else setError(reason instanceof Error ? reason.message : 'Não foi possível alterar o cadastro.')
    } finally { setSaving(false) }
  }

  const start = result.totalCount ? ((result.page - 1) * result.pageSize) + 1 : 0
  const end = Math.min(result.page * result.pageSize, result.totalCount)
  const pages = Math.max(1, Math.ceil(result.totalCount / result.pageSize))

  return <div className="page-enter">
    <PageHeader eyebrow="Pessoas atendidas" title="Clientes" description="Cadastros criados pelos agendamentos e pela área do cliente. Um cadastro desativado não agenda, não faz check-in e não recebe avisos." />
    <section className="panel table-panel">
      <div className="table-toolbar">
        <div className="search-field"><Search size={18} /><input value={rawSearch} onChange={event => { setRawSearch(event.target.value); setPage(1) }} placeholder="Buscar por nome ou telefone" aria-label="Buscar clientes" /></div>
        <select className="field-input compact-select" value={status} aria-label="Status dos clientes" onChange={event => { setStatus(event.target.value as ModuleStatus); setPage(1) }}><option value="all">Todos os status</option><option value="active">Ativos</option><option value="inactive">Inativos</option></select>
        <span>{start}–{end} de {countLabel(result.totalCount, 'cliente', 'clientes')}</span>
      </div>
      {loading ? <div className="empty-state" role="status">Carregando clientes…</div>
        : error && result.items.length === 0 ? <EmptyState><p>{error}</p><button className="secondary-button" onClick={() => void load()}>Tentar novamente</button></EmptyState>
          : result.items.length === 0 ? <EmptyState>Nenhum cliente encontrado.</EmptyState>
            : <div className="table-scroll"><table className="data-table">
                <thead><tr><th>Cliente</th><th>WhatsApp</th><th>Avisos</th><th>Conta</th><th>Status</th><th className="actions-column">Ações</th></tr></thead>
                <tbody>{result.items.map(customer => <tr key={customer.id}>
                  <td><div className="person-cell"><span className="person-placeholder">{customer.name.slice(0, 1).toUpperCase()}</span><span><strong>{customer.name}</strong></span></div></td>
                  <td>{displayWhatsApp(customer.phone)}</td>
                  <td>{optInLabel(customer)}</td>
                  <td>{customer.hasAccount ? 'Conta vinculada' : 'Sem conta'}</td>
                  <td><StatusBadge status={customer.isActive ? 'active' : 'inactive'} /></td>
                  <td><div className="row-actions"><button disabled={saving} onClick={() => void toggleStatus(customer)} aria-label={`${customer.isActive ? 'Desativar' : 'Ativar'} ${customer.name}`}>{customer.isActive ? <UserMinus size={17} /> : <UserPlus size={17} />}</button></div></td>
                </tr>)}</tbody>
              </table></div>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}
      {refreshing && <p className="list-refreshing" role="status">Atualizando lista…</p>}
      {result.totalCount > pageSize && <div className="pagination"><button className="secondary-button" disabled={page <= 1 || refreshing} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages || refreshing} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>
  </div>
}
