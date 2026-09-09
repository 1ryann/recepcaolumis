import { CreateProfessionalAccess } from './CreateProfessionalAccess'
import { ResetProfessionalPassword } from './ResetProfessionalPassword'
import { useEffect, useState } from 'react'
import type { EligibleUserDto, ProfessionalUserLinkDto } from '../../api/modules'

export function ProfessionalUserLink({
  professionalName, link,
  eligible,
  pending,
  onSearch,
  onSave,
  onRemove,
}: {
  professionalName?: string
  link: ProfessionalUserLinkDto | null
  eligible: EligibleUserDto[]
  pending: boolean
  onSearch(search: string): void
  onSave(userId: string): Promise<void>
  onRemove(): Promise<void>
}) {
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState('')
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { onSearch(search) }, [search, onSearch])

  const save = async () => {
    if (!selected) { setError('Selecione uma conta elegível.'); return }
    try { await onSave(selected); setError(null) } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível vincular a conta.')
    }
  }
  const remove = async () => {
    try { await onRemove(); setError(null) } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível desvincular a conta.')
    }
  }

  return <div className="link-editor">
    {link?.linked ? <p className="link-current">Conta vinculada: <strong>{link.displayName}</strong><br /><small>{link.email}</small></p>
      : <p className="link-current">Nenhuma conta está vinculada a este profissional.</p>}
    {link?.linked && link.userId && <ResetProfessionalPassword userId={link.userId} />}
    {professionalName && !link?.linked && <CreateProfessionalAccess name={professionalName} onCreated={userId => { setSelected(userId); onSearch(search) }} />}<label className="field-label">Buscar conta profissional
      <input className="field-input" value={search} onChange={event => setSearch(event.target.value)} placeholder="Nome ou e-mail" />
    </label>
    <label className="field-label">Conta elegível
      <select className="field-input" value={selected} onChange={event => setSelected(event.target.value)}>
        <option value="">Selecione</option>
        {eligible.map(user => <option key={user.userId} value={user.userId}>{user.displayName} · {user.email}</option>)}
      </select>
    </label>
    {error && <p className="form-error" role="alert">{error}</p>}
    <div className="modal-actions">
      {link?.linked && <button className="danger-button" type="button" disabled={pending} onClick={() => void remove()}>Desvincular</button>}
      <button className="primary-button" type="button" disabled={pending} onClick={() => void save()}>
        {pending ? 'Salvando…' : link?.linked ? 'Substituir vínculo' : 'Vincular conta'}
      </button>
    </div>
  </div>
}
