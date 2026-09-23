import { Check, Copy, UserPlus } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { ApiError } from '../../api/client'
import { adminUsersApi, type SystemRole } from '../../api/modules'

const roleLabels: Record<SystemRole, string> = {
  ADMINISTRADOR: 'Administrador — acesso total, inclusive configurações e contas',
  GERENTE: 'Gerente — operação da recepção',
  PROFISSIONAL: 'Profissional — agenda e atendimentos próprios',
}

// Until now the only account the screens could create was a professional's, from the link modal in
// Profissionais — a manager or another administrator had to be created by the CLI on the server.
export function CreateUserAccount() {
  const [form, setForm] = useState({ displayName: '', email: '', role: 'GERENTE' as SystemRole })
  const [created, setCreated] = useState<{ email: string; temporaryPassword: string } | null>(null)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setPending(true); setError('')
    try {
      const account = await adminUsersApi.create({
        displayName: form.displayName.trim(), email: form.email.trim(), role: form.role,
      })
      setCreated({ email: form.email.trim(), temporaryPassword: account.temporaryPassword })
      setForm({ displayName: '', email: '', role: 'GERENTE' })
    } catch (reason) {
      setError(reason instanceof ApiError && reason.status === 503
        ? 'A configuração de identidade está indisponível no servidor. Tente novamente em instantes.'
        : 'Não foi possível criar a conta. Confira o nome e o e-mail — talvez já exista uma conta com esse e-mail.')
    } finally { setPending(false) }
  }

  const copy = async () => {
    if (!created) return
    try { await navigator.clipboard?.writeText(created.temporaryPassword); setCopied(true) } catch { setCopied(false) }
  }

  if (created) {
    return (
      <div className="user-account-created" role="status">
        <p className="form-hint">Conta criada para <strong>{created.email}</strong>. A troca de senha é obrigatória no primeiro acesso.</p>
        <div className="user-account-password">
          <code>{created.temporaryPassword}</code>
          <button className="secondary-button" type="button" onClick={() => void copy()}>
            {copied ? <><Check size={16} /> Copiado</> : <><Copy size={16} /> Copiar</>}
          </button>
        </div>
        {/* Identity only hands the password back once, so leaving this card resets it for good. */}
        <p className="field-hint">Guarde agora: a senha não volta a ser exibida.</p>
        <button className="ghost-button" type="button" onClick={() => { setCreated(null); setCopied(false) }}>Criar outra conta</button>
      </div>
    )
  }

  return (
    <form className="simple-form" onSubmit={submit}>
      <label className="field-label">Nome
        <input className="field-input" required maxLength={200} autoComplete="off" placeholder="Ex.: Helena Martins"
          value={form.displayName} onChange={(event) => setForm(current => ({ ...current, displayName: event.target.value }))} />
      </label>
      <label className="field-label">E-mail
        <input className="field-input" required type="email" maxLength={256} autoComplete="off" placeholder="pessoa@exemplo.com"
          value={form.email} onChange={(event) => setForm(current => ({ ...current, email: event.target.value }))} />
      </label>
      <label className="field-label">Perfil
        <select className="field-input" value={form.role}
          onChange={(event) => setForm(current => ({ ...current, role: event.target.value as SystemRole }))}>
          {(Object.keys(roleLabels) as SystemRole[]).map(role => <option key={role} value={role}>{roleLabels[role]}</option>)}
        </select>
      </label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="modal-actions">
        <button className="primary-button" type="submit" disabled={pending || !form.displayName.trim() || !form.email.trim()}>
          {pending ? 'Criando…' : <><UserPlus size={16} /> Criar conta</>}
        </button>
      </div>
    </form>
  )
}
