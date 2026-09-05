import { type FormEvent, useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { LockKeyhole } from 'lucide-react'
import { useSession } from '../auth/SessionProvider'

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
  if (session.status === 'authenticated') return <Navigate to="/admin" replace />

  const submit = async (event: FormEvent) => {
    event.preventDefault(); setLoading(true); setError('')
    try { await session.changePassword(currentPassword, newPassword, confirmation); navigate('/admin', { replace: true }) }
    catch { setError('Não foi possível alterar a senha.') }
    finally { setLoading(false) }
  }
  return <main className="login-page"><section className="login-brand-panel"><div className="login-brand-top"><img className="login-logo" src="/lumis-logo-transparent.png" alt="LUMIS" /></div><div className="login-message"><h1>Proteja seu acesso.</h1><p>Defina uma senha pessoal antes de acessar os demais módulos.</p></div></section><section className="login-form-panel"><div className="login-form-wrap"><span className="login-icon"><LockKeyhole size={22} /></span><h2>Alterar senha</h2><p>Esta troca é obrigatória no primeiro acesso.</p><form onSubmit={submit}><label className="field-label">Senha temporária<input className="field-input" type="password" autoComplete="current-password" value={currentPassword} onChange={e => setCurrentPassword(e.target.value)} /></label><label className="field-label">Nova senha<input className="field-input" type="password" autoComplete="new-password" value={newPassword} onChange={e => setNewPassword(e.target.value)} /></label><label className="field-label">Confirmar nova senha<input className="field-input" type="password" autoComplete="new-password" value={confirmation} onChange={e => setConfirmation(e.target.value)} /></label>{error && <div className="form-error" role="alert">{error}</div>}<button className="primary-button login-submit" disabled={loading}>{loading ? <span className="spinner" /> : 'Salvar nova senha'}</button></form></div></section></main>
}
