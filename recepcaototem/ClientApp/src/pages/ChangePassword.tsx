import { ArrowLeft } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { homeForRoles } from '../auth/roleRoutes'
import { useSession } from '../auth/SessionProvider'
import { LumisPageShell } from '../features/lumis/LumisPageShell'
import { LumisLogo } from '../theme/LumisLogo'

// Two ways in, one screen. The forced one: a brand-new account (or one an administrator reset)
// carries mustChangePassword, and ProtectedRoute sends it here before anything else. The
// voluntary one: "Alterar senha" in the account menu. The screen used to bounce an already
// authenticated visitor straight back home, which made that menu item do nothing at all.
export function ChangePassword() {
  const session = useSession()
  const navigate = useNavigate()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [error, setError] = useState('')
  const [changed, setChanged] = useState(false)
  const [loading, setLoading] = useState(false)
  if (session.status === 'loading') return <div className="route-loading" role="status">Carregando…</div>
  if (session.status === 'anonymous') return <Navigate to="/login" replace />
  // Same wording as ProtectedRoute: without a confirmed session there is no role to go back to and
  // the change would fail anyway.
  if (session.status === 'error') return <div role="alert">Não foi possível confirmar a sessão. <button onClick={() => void session.refresh()}>Tentar novamente</button></div>

  const forced = session.status === 'mustChangePassword'
  const home = homeForRoles(session.user?.roles ?? [])

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    // Caught here so a typo in the confirmation does not cost a round trip and come back as the
    // same generic failure as a wrong current password.
    if (newPassword !== confirmation) { setError('A confirmação não confere com a nova senha.'); return }
    setLoading(true); setError('')
    try {
      const current = await session.changePassword(currentPassword, newPassword, confirmation)
      if (forced) { if (current) navigate(homeForRoles(current.roles), { replace: true }); return }
      setChanged(true)
    }
    catch { setError('Não foi possível alterar a senha. Confira a senha atual e use pelo menos 6 caracteres, entre letras e números.') }
    finally { setLoading(false) }
  }

  return (
    <LumisPageShell className="lumis-login">
      <main className="lumis-login-card lumis-auth-surface">
        <LumisLogo className="lumis-login-logo" alt="LUMIS" width={124} height={38} />
        <span className="lumis-login-eyebrow">PROTEJA SEU ACESSO</span>
        <h1 className="lumis-login-title">Alterar senha</h1>
        {changed ? (
          <>
            <p className="lumis-login-text" role="status">Senha alterada. Use a nova senha no próximo acesso.</p>
            <Link className="primary-button lumis-login-submit" to={home} replace>Voltar</Link>
          </>
        ) : (
          <>
            <p className="lumis-login-text">
              {forced ? 'Esta troca é obrigatória no primeiro acesso.' : 'Escolha uma nova senha para a sua conta.'}
            </p>
            <form onSubmit={submit} className="lumis-login-form">
              <label className="field-label">{forced ? 'Senha temporária' : 'Senha atual'}
                <input className="field-input" type="password" required autoComplete="current-password"
                  value={currentPassword} onChange={e => setCurrentPassword(e.target.value)} />
              </label>
              <label className="field-label">Nova senha
                <input className="field-input" type="password" required minLength={6} autoComplete="new-password"
                  value={newPassword} onChange={e => setNewPassword(e.target.value)} />
              </label>
              {/* Outside the label on purpose: inside it, the hint becomes part of the field's
                  accessible name and a screen reader reads the rule back on every focus. */}
              <span className="field-hint">Pelo menos 6 caracteres, entre letras e números.</span>
              <label className="field-label">Confirmar nova senha
                <input className="field-input" type="password" required minLength={6} autoComplete="new-password"
                  value={confirmation} onChange={e => setConfirmation(e.target.value)} />
              </label>
              {error && <div className="form-error" role="alert">{error}</div>}
              <button className="primary-button lumis-login-submit" disabled={loading}>
                {loading ? <span className="spinner" /> : 'Salvar nova senha'}
              </button>
              {/* Nobody is trapped here: only the forced flow has no way back. */}
              {!forced && <Link className="lumis-login-back" to={home} replace><ArrowLeft size={16} /> Cancelar</Link>}
            </form>
          </>
        )}
      </main>
    </LumisPageShell>
  )
}
