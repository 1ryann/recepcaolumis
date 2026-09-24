import { Check, Copy, KeyRound, Search, Trash2, UserMinus, UserPlus } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { type CustomerAdministrationDto, customersAdministrationApi, type ModuleStatus, type PagedResponse } from '../../api/modules'
import { EmptyState, PageHeader, StatusBadge, countLabel } from '../../components/PageElements'
import { Modal } from '../../components/Modal'
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
  const [removing, setRemoving] = useState<CustomerAdministrationDto | null>(null)
  const [resetting, setResetting] = useState<CustomerAdministrationDto | null>(null)
  const [issued, setIssued] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)

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

  // The API decides between erasing the row and erasing only the person, because reservations, visits
  // and WhatsApp notices point at the customer. The screen reports which one happened.
  const remove = async () => {
    if (!removing) return
    setSaving(true)
    try {
      const outcome = await customersAdministrationApi.remove(removing.id, removing.concurrencyToken)
      setNotice(outcome.outcome === 'DELETED'
        ? `${removing.name} foi excluído.`
        : `${removing.name} tinha atendimentos registrados: os dados pessoais foram apagados e o histórico foi mantido sem identificação.`)
      setRemoving(null)
      await load()
    } catch (reason) {
      if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') {
        await load()
        setError('Este cadastro foi alterado por outra operação. Recarregamos os dados para você tentar novamente.')
      } else setError(reason instanceof Error ? reason.message : 'Não foi possível excluir o cadastro.')
      setRemoving(null)
    } finally { setSaving(false) }
  }

  // There is no self-service recovery: a customer who forgot the password asks here, and the
  // reception reads the temporary one back. It is shown once and the next sign-in forces a change.
  const resetPassword = async () => {
    if (!resetting) return
    setSaving(true); setError(null)
    try {
      setIssued((await customersAdministrationApi.resetPassword(resetting.id)).temporaryPassword)
    } catch (reason) {
      setError(reason instanceof ApiError && reason.code === 'ACCOUNT_NOT_CUSTOMER_ONLY'
        ? 'Esta conta também dá outro acesso. Redefina por Profissionais ou peça ao administrador.'
        : 'Não foi possível redefinir a senha deste cliente.')
      setResetting(null)
    } finally { setSaving(false) }
  }
  const closeReset = () => { setResetting(null); setIssued(null); setCopied(false) }
  const copyPassword = async () => {
    if (!issued) return
    try { await navigator.clipboard?.writeText(issued); setCopied(true) } catch { setCopied(false) }
  }

  const start = result.totalCount ? ((result.page - 1) * result.pageSize) + 1 : 0
  const end = Math.min(result.page * result.pageSize, result.totalCount)
  const pages = Math.max(1, Math.ceil(result.totalCount / result.pageSize))

  return <div className="page-enter">
    <PageHeader eyebrow="Pessoas atendidas" tour="pagina-clientes" title="Clientes" description="Cadastros criados pelos agendamentos e pela área do cliente. Um cadastro desativado não agenda, não faz check-in e não recebe avisos." />
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
                  <td><div className="row-actions"><button type="button" disabled={saving} title={customer.isActive ? 'Desativar' : 'Ativar'} onClick={() => void toggleStatus(customer)} aria-label={`${customer.isActive ? 'Desativar' : 'Ativar'} ${customer.name}`}>{customer.isActive ? <UserMinus size={17} /> : <UserPlus size={17} />}</button>{customer.hasAccount && <button type="button" disabled={saving} title="Redefinir senha" aria-label={`Redefinir a senha de ${customer.name}`} onClick={() => { setNotice(null); setIssued(null); setCopied(false); setResetting(customer) }}><KeyRound size={17} /></button>}<button type="button" disabled={saving} title="Excluir" aria-label={`Excluir ${customer.name}`} onClick={() => { setNotice(null); setRemoving(customer) }}><Trash2 size={17} /></button></div></td>
                </tr>)}</tbody>
              </table></div>}
      {notice && <p className="form-hint" role="status">{notice}</p>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}
      {refreshing && <p className="list-refreshing" role="status">Atualizando lista…</p>}
      {result.totalCount > pageSize && <div className="pagination"><button className="secondary-button" disabled={page <= 1 || refreshing} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages || refreshing} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>
    <Modal open={removing !== null} onClose={() => setRemoving(null)} title="Excluir cliente"
      subtitle={removing ? `${removing.name} · ${displayWhatsApp(removing.phone)}` : undefined}>
      <div className="simple-form">
        <p className="form-hint">Sem volta. O acesso do cliente é apagado junto com o cadastro.</p>
        <p className="form-hint">Se houver atendimentos, avisos ou visitas registrados, eles continuam no histórico do prédio, sem o nome e o telefone.</p>
        <div className="modal-actions">
          <button className="ghost-button" type="button" disabled={saving} onClick={() => setRemoving(null)}>Cancelar</button>
          <button className="danger-button" type="button" disabled={saving} onClick={() => void remove()}>{saving ? 'Excluindo…' : 'Excluir definitivamente'}</button>
        </div>
      </div>
    </Modal>
    <Modal open={resetting !== null} onClose={closeReset} title="Redefinir senha"
      subtitle={resetting ? `${resetting.name} · ${displayWhatsApp(resetting.phone)}` : undefined}>
      {issued
        ? <div className="user-account-created">
            <p className="form-hint">Senha temporária criada. Passe para o cliente agora — ele terá que escolher uma nova senha ao entrar.</p>
            <div className="user-account-password">
              <code>{issued}</code>
              <button className="secondary-button" type="button" onClick={() => void copyPassword()}>{copied ? <><Check size={16} /> Copiado</> : <><Copy size={16} /> Copiar</>}</button>
            </div>
            <p className="field-hint">Guarde agora: a senha não volta a ser exibida.</p>
            <div className="modal-actions"><button className="primary-button" type="button" onClick={closeReset}>Fechar</button></div>
          </div>
        : <div className="simple-form">
            <p className="form-hint">Use quando o cliente não lembra a senha. A senha atual deixa de valer na hora e as sessões abertas caem.</p>
            <p className="form-hint">A senha temporária aparece uma única vez, aqui nesta tela.</p>
            <div className="modal-actions">
              <button className="ghost-button" type="button" disabled={saving} onClick={closeReset}>Cancelar</button>
              <button className="primary-button" type="button" disabled={saving} onClick={() => void resetPassword()}>{saving ? 'Redefinindo…' : 'Redefinir senha'}</button>
            </div>
          </div>}
    </Modal>
  </div>
}
