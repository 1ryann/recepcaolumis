import { Camera, KeyRound, Pencil, Plus, Search, UserMinus, UserPlus } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { type EligibleUserDto, type ModuleStatus, type PagedResponse, type ProfessionalDto, professionalsApi } from '../../api/modules'
import { useSession } from '../../auth/SessionProvider'
import { Modal } from '../../components/Modal'
import { EmptyState, PageHeader, StatusBadge } from '../../components/PageElements'
import { ProfessionalForm } from '../../features/professionals/ProfessionalForm'
import { ProfessionalPhotoEditor } from '../../features/professionals/ProfessionalPhotoEditor'
import { ProfessionalUserLink } from '../../features/professionals/ProfessionalUserLink'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'

const pageSize = 20
const empty: PagedResponse<ProfessionalDto> = { items: [], page: 1, pageSize, totalCount: 0 }

function displayWhatsApp(value: string) {
  const brazil = value.match(/^\+55(\d{2})(\d{4,5})(\d{4})$/)
  return brazil ? `(${brazil[1]}) ${brazil[2]}-${brazil[3]}` : value
}

export function Professionals() {
  const { user } = useSession()
  const administrator = user?.roles.includes('ADMINISTRADOR') ?? false
  const [rawSearch, setRawSearch] = useState('')
  const search = useDebouncedValue(rawSearch.trim().replace(/\s+/g, ' ') || undefined)
  const [status, setStatus] = useState<ModuleStatus>('all')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [formProfessional, setFormProfessional] = useState<ProfessionalDto | null | undefined>(undefined)
  const [photoProfessional, setPhotoProfessional] = useState<ProfessionalDto | null>(null)
  const [linkProfessional, setLinkProfessional] = useState<ProfessionalDto | null>(null)
  const [link, setLink] = useState<Awaited<ReturnType<typeof professionalsApi.userLink>> | null>(null)
  const [eligible, setEligible] = useState<EligibleUserDto[]>([])
  const [saving, setSaving] = useState(false)

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      setError(null)
      setResult(await professionalsApi.list({ search, status, page, pageSize }, signal))
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError') setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os profissionais.')
    }
  }, [page, search, status])

  useEffect(() => {
    const controller = new AbortController()
    setLoading(result.items.length === 0); setRefreshing(result.items.length > 0)
    void load(controller.signal).finally(() => { if (!controller.signal.aborted) { setLoading(false); setRefreshing(false) } })
    return () => controller.abort()
  }, [load])

  const refresh = async () => { await load() }
  const upsert = (professional: ProfessionalDto) => setResult(current => ({ ...current, items: current.items.map(item => item.id === professional.id ? professional : item) }))
  const resolveFailure = async (reason: unknown) => {
    if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') {
      await refresh()
      setError('Este registro foi alterado por outra operação. Recarregamos os dados para você tentar novamente.')
      return
    }
    throw reason
  }
  const saveForm = async (values: { name: string, profession: string, whatsApp: string }) => {
    setSaving(true)
    try {
      if (formProfessional) upsert(await professionalsApi.update(formProfessional.id, { ...values, concurrencyToken: formProfessional.concurrencyToken }))
      else {
        const created = await professionalsApi.create(values)
        setResult(current => ({ ...current, items: [created, ...current.items], totalCount: current.totalCount + 1 }))
      }
      setFormProfessional(undefined)
    } catch (reason) { await resolveFailure(reason) } finally { setSaving(false) }
  }
  const toggleStatus = async (professional: ProfessionalDto) => {
    setSaving(true)
    try { upsert(await professionalsApi.changeStatus(professional.id, !professional.isActive, professional.concurrencyToken)) }
    catch (reason) { await resolveFailure(reason) } finally { setSaving(false) }
  }
  const uploadPhoto = async (file: File) => {
    if (!photoProfessional) return
    setSaving(true)
    try { upsert(await professionalsApi.putPhoto(photoProfessional.id, file, photoProfessional.concurrencyToken)) }
    catch (reason) { await resolveFailure(reason) } finally { setSaving(false) }
  }
  const removePhoto = async () => {
    if (!photoProfessional) return
    setSaving(true)
    try { upsert(await professionalsApi.removePhoto(photoProfessional.id, photoProfessional.concurrencyToken)) }
    catch (reason) { await resolveFailure(reason) } finally { setSaving(false) }
  }
  const openLink = async (professional: ProfessionalDto) => {
    setLinkProfessional(professional); setLink(null); setEligible([])
    try {
      const [current, users] = await Promise.all([professionalsApi.userLink(professional.id), professionalsApi.eligibleUsers({ page: 1, pageSize })])
      setLink(current); setEligible(users.items)
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as contas.') }
  }
  const searchEligible = useCallback((value: string) => {
    void professionalsApi.eligibleUsers({ search: value.trim() || undefined, page: 1, pageSize })
      .then(response => setEligible(response.items))
      .catch(reason => setError(reason instanceof Error ? reason.message : 'Não foi possível buscar as contas.'))
  }, [])
  const saveLink = async (userId: string) => {
    if (!linkProfessional) return
    setSaving(true)
    try {
      const updated = await professionalsApi.putUserLink(linkProfessional.id, userId, linkProfessional.concurrencyToken)
      upsert(updated); setLinkProfessional(updated); setLink(await professionalsApi.userLink(updated.id))
    } catch (reason) { await resolveFailure(reason) } finally { setSaving(false) }
  }
  const removeLink = async () => {
    if (!linkProfessional) return
    setSaving(true)
    try {
      const updated = await professionalsApi.removeUserLink(linkProfessional.id, linkProfessional.concurrencyToken)
      upsert(updated); setLinkProfessional(updated); setLink({ linked: false })
    } catch (reason) { await resolveFailure(reason) } finally { setSaving(false) }
  }
  const start = result.totalCount ? ((result.page - 1) * result.pageSize) + 1 : 0
  const end = Math.min(result.page * result.pageSize, result.totalCount)
  const pages = Math.max(1, Math.ceil(result.totalCount / result.pageSize))

  return <div className="page-enter">
    <PageHeader eyebrow="Equipe do edifício" title="Profissionais" description="Gerencie os dados cadastrais e o acesso das pessoas que atendem no LUMIS."
      action={<button className="primary-button" data-tour="novo-profissional" onClick={() => setFormProfessional(null)}><Plus size={18} /> Novo profissional</button>} />
    <section className="panel table-panel">
      <div className="table-toolbar"><div className="search-field" data-tour="busca-profissionais"><Search size={18} /><input value={rawSearch} onChange={event => { setRawSearch(event.target.value); setPage(1) }} placeholder="Buscar por nome ou profissão" aria-label="Buscar profissionais" /></div>
        <select className="field-input compact-select" data-tour="filtro-status-profissionais" value={status} aria-label="Status dos profissionais" onChange={event => { setStatus(event.target.value as ModuleStatus); setPage(1) }}><option value="all">Todos os status</option><option value="active">Ativos</option><option value="inactive">Inativos</option></select>
        <span>{start}–{end} de {result.totalCount} profissionais</span></div>
      {loading ? <div className="empty-state" role="status">Carregando profissionais…</div>
        : error && result.items.length === 0 ? <EmptyState><p>{error}</p><button className="secondary-button" onClick={() => void refresh()}>Tentar novamente</button></EmptyState>
          : result.items.length === 0 ? <EmptyState>Nenhum profissional encontrado.</EmptyState>
            : <div className="table-scroll"><table className="data-table"><thead><tr><th>Profissional</th><th>Profissão</th><th>WhatsApp</th><th>Conta</th><th>Status</th><th className="actions-column">Ações</th></tr></thead><tbody>{result.items.map(professional => <tr key={professional.id}><td><div className="person-cell">
              {professional.hasPhoto && professional.photoUrl ? <img src={professional.photoUrl} alt={`Foto de ${professional.name}`} /> : <span className="person-placeholder">{professional.name.slice(0, 1).toUpperCase()}</span>}
              <span><strong>{professional.name}</strong><small>{professional.hasPhoto ? 'Foto cadastrada' : 'Sem foto'}</small></span></div></td><td>{professional.profession}</td><td>{displayWhatsApp(professional.whatsApp)}</td><td>{professional.hasLinkedUser ? 'Conta vinculada' : 'Sem conta'}</td><td><StatusBadge status={professional.isActive ? 'active' : 'inactive'} /></td><td><div className="row-actions" data-tour="acoes-profissional">
              <button onClick={() => setFormProfessional(professional)} aria-label={`Editar ${professional.name}`}><Pencil size={17} /></button><button onClick={() => setPhotoProfessional(professional)} aria-label={`Foto de ${professional.name}`}><Camera size={17} /></button>{administrator && <button onClick={() => void openLink(professional)} aria-label={`Vincular conta ${professional.name}`}><KeyRound size={17} /></button>}<button disabled={saving} onClick={() => void toggleStatus(professional)} aria-label={`${professional.isActive ? 'Desativar' : 'Ativar'} ${professional.name}`}>{professional.isActive ? <UserMinus size={17} /> : <UserPlus size={17} />}</button>
            </div></td></tr>)}</tbody></table></div>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}{refreshing && <p className="list-refreshing" role="status">Atualizando lista…</p>}
      {result.totalCount > pageSize && <div className="pagination" data-tour="paginacao-profissionais"><button className="secondary-button" disabled={page <= 1 || refreshing} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages || refreshing} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>
    <Modal open={formProfessional !== undefined} onClose={() => setFormProfessional(undefined)} title={formProfessional ? 'Editar profissional' : 'Novo profissional'} subtitle="Os dados são validados e normalizados pela API." size="large"><ProfessionalForm professional={formProfessional ?? null} pending={saving} onCancel={() => setFormProfessional(undefined)} onSubmit={saveForm} /></Modal>
    <Modal open={photoProfessional !== null} onClose={() => setPhotoProfessional(null)} title="Foto do profissional" subtitle="A imagem fica em armazenamento privado." size="large">{photoProfessional && <ProfessionalPhotoEditor professional={photoProfessional} pending={saving} onClose={() => setPhotoProfessional(null)} onUpload={uploadPhoto} onRemove={removePhoto} />}</Modal>
    <Modal open={linkProfessional !== null} onClose={() => setLinkProfessional(null)} title="Vínculo com conta" subtitle="Somente administradores podem administrar este vínculo." size="large">{linkProfessional && <ProfessionalUserLink link={link} eligible={eligible} pending={saving} onSearch={searchEligible} onSave={saveLink} onRemove={removeLink} />}</Modal>
  </div>
}
