import { homeForRoles } from '../auth/roleRoutes'
import { type FormEvent, useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { LumisPageShell } from '../features/lumis/LumisPageShell'

export function ChangePassword() {
  const session = useSession()
  const navigate = useNavigate()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  if (session.status === 'loading') return <div className="route-loading" role="status">Carregando…</div>
  if (session.status === 'anonymous') return <Navigate to="/login" replace />
  if (session.status === 'authenticated') return <Navigate to={homeForRoles(session.user?.roles ?? [])} replace />

  const submit = async (event: FormEvent) => {
    event.preventDefault(); setLoading(true); setError('')
    try { const current = await session.changePassword(currentPassword, newPassword, confirmation); if (current) navigate(homeForRoles(current.roles), { replace: true }) }
    catch { setError('Não foi possível alterar a senha.') }
    finally { setLoading(false) }
  }
  return <LumisPageShell className="lumis-login"><main className="lumis-login-card lumis-auth-surface"><img className="lumis-login-logo" src="/lumis-logo-transparent.png" alt="LUMIS" width={124} height={38} /><span className="lumis-login-eyebrow">PROTEJA SEU ACESSO</span><h1 className="lumis-login-title">Alterar senha</h1><p className="lumis-login-text">Esta troca é obrigatória no primeiro acesso.</p><form onSubmit={submit} className="lumis-login-form"><label className="field-label">Senha temporária<input className="field-input" type="password" autoComplete="current-password" value={currentPassword} onChange={e => setCurrentPassword(e.target.value)} /></label><label className="field-label">Nova senha<input className="field-input" type="password" autoComplete="new-password" value={newPassword} onChange={e => setNewPassword(e.target.value)} /></label><label className="field-label">Confirmar nova senha<input className="field-input" type="password" autoComplete="new-password" value={confirmation} onChange={e => setConfirmation(e.target.value)} /></label>{error && <div className="form-error" role="alert">{error}</div>}<button className="primary-button lumis-login-submit" disabled={loading}>{loading ? <span className="spinner" /> : 'Salvar nova senha'}</button></form></main></LumisPageShell>
}
