import { Check, RefreshCw, X } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { professionalRegistrationApi, type ProfessionalApplicationDto } from '../../api/modules'
import { EmptyState } from '../../components/PageElements'

export function ProfessionalApplications() {
  const [items, setItems] = useState<ProfessionalApplicationDto[]>([]); const [error, setError] = useState(''); const [busy, setBusy] = useState('')
  const load = useCallback(async () => { try { setItems((await professionalRegistrationApi.list({ status: 'all', page: 1, pageSize: 100 })).items); setError('') } catch { setError('Não foi possível carregar as solicitações.') } }, [])
  useEffect(() => { void load() }, [load])
  const review = async (item: ProfessionalApplicationDto, approve: boolean) => { setBusy(item.id); try { const updated = approve ? await professionalRegistrationApi.approve(item.id, item.concurrencyToken) : await professionalRegistrationApi.reject(item.id, item.concurrencyToken); setItems(current => current.map(x => x.id === item.id ? updated : x)) } catch { setError('A solicitação foi alterada ou não pôde ser revisada. Atualize a lista.') } finally { setBusy('') } }
  return <section className="admin-page"><div className="page-header"><div><span className="page-eyebrow">Credenciamento</span><h1>Solicitações de profissionais</h1><p>Analise os cadastros antes de liberar o acesso profissional.</p></div><button className="secondary-button" onClick={() => void load()}><RefreshCw size={16}/> Atualizar</button></div>{error && <div className="form-error">{error}</div>}<div className="panel application-list">{items.length === 0 && <EmptyState>Nenhuma solicitação encontrada.</EmptyState>}{items.map(item => <article className="application-row" key={item.id}><div><strong>{item.name}</strong><span>{item.profession}</span><small>{new Date(item.createdAt).toLocaleString('pt-BR')} · {item.status}</small>{item.description && <p>{item.description}</p>}</div>{item.status === 'PENDING' && <div><button className="primary-button" disabled={busy === item.id} onClick={() => void review(item, true)}><Check size={16}/> Aprovar</button><button className="secondary-button" disabled={busy === item.id} onClick={() => void review(item, false)}><X size={16}/> Recusar</button></div>}</article>)}</div></section>
}
