import { type FormEvent, useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { apiClient, ApiError } from '../api/client'
import { homeForRoles } from '../auth/roleRoutes'
import { safeCustomerReturnUrl } from '../auth/returnUrl'
import { LumisPageShell } from '../features/lumis/LumisPageShell'
import { LumisLogo } from '../theme/LumisLogo'

type Audience = 'admin' | 'customer' | 'professional'
const COPY: Record<Audience, { eyebrow: string; text: string }> = {
  customer:     { eyebrow: 'ÁREA DO CLIENTE',      text: 'Acesse sua conta para continuar.' },
  professional: { eyebrow: 'ÁREA DO PROFISSIONAL', text: 'Acesse sua conta para continuar.' },
  admin:        { eyebrow: 'ADMINISTRAÇÃO',        text: '' },
}

export function Login({ audience = 'admin' }: { audience?: Audience }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const session = useSession()
  const returnUrl = audience === 'customer' ? safeCustomerReturnUrl(params.get('returnUrl')) : null

  useEffect(() => {
    if (audience !== 'customer' || !returnUrl) return
    const query = returnUrl.split('?')[1] ?? ''
    const handoffToken = new URLSearchParams(query).get('handoff')
    if (!handoffToken) return
    void apiClient.post('/api/totem/booking-handoffs/claim', { handoffToken }).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setLoading(true)
    try {
      const current = await session.login(email, password)
      if (!current) throw new Error('Session unavailable')
      navigate(current.mustChangePassword ? '/change-password' : (returnUrl ?? homeForRoles(current.roles)), { replace: true })
    } catch (err) {
      setError(err instanceof ApiError && err.status === 429
        ? 'Muitas tentativas. Aguarde alguns instantes e tente novamente.'
        : 'E-mail ou senha inválidos.')
    } finally { setLoading(false) }
  }

  if (session.status === 'loading') return <div role="status">Confirmando sessão…</div>

  const { eyebrow, text } = COPY[audience]
  return (
    <LumisPageShell className="lumis-login">
      <main className="lumis-login-card">
        <LumisLogo className="lumis-login-logo" alt="LUMIS" width={124} height={38} />
        <span className="lumis-login-eyebrow">{eyebrow}</span>
        <h1 className="lumis-login-title">Bem-vindo de volta</h1>
        {text && <p className="lumis-login-text">{text}</p>}

        {session.status === 'error' && (
          <div className="form-error" role="alert">Não foi possível confirmar a sessão.
            <button type="button" onClick={() => void session.refresh()}>Tentar novamente</button></div>
        )}

        {session.user ? (
          <div className="lumis-login-active">
            <p>Sessão ativa: {session.user.displayName}</p>
            <button className="primary-button" onClick={() => navigate(returnUrl ?? homeForRoles(session.user!.roles))}>Continuar</button>
            <button className="secondary-button" onClick={async () => { try { await session.logout(); setPassword('') } catch { setError('Não foi possível confirmar a saída.') } }}>Sair / Trocar conta</button>
          </div>
        ) : (
          <form onSubmit={submit} className="lumis-login-form">
            <label className="field-label">E-mail
              <input className="field-input" type="email" autoComplete="email" value={email}
                onChange={(e) => { setEmail(e.target.value); setError('') }} />
            </label>
            <label className="field-label">Senha
              <span className="lumis-password-field">
                <input className="field-input" type={showPassword ? 'text' : 'password'} autoComplete="current-password"
                  value={password} onChange={(e) => { setPassword(e.target.value); setError('') }} />
                <button type="button" className="lumis-password-toggle" onClick={() => setShowPassword((v) => !v)}>
                  {showPassword ? 'Ocultar' : 'Mostrar'}
                </button>
              </span>
            </label>
            {error && <div className="form-error" role="alert">{error}</div>}
            <button className="primary-button lumis-login-submit" type="submit" disabled={loading}>
              {loading ? <span className="spinner" /> : 'Entrar'}
            </button>
          </form>
        )}

        {audience === 'customer' && (
          <p className="lumis-login-secondary">Não tem uma conta?{' '}
            <a href={`/cliente/cadastro${returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : ''}`}>Criar conta</a></p>
        )}
        {audience === 'professional' && (
          <p className="lumis-login-secondary">Ainda não possui acesso? <a href="/profissional/cadastro">Solicitar cadastro</a></p>
        )}
        <a className="lumis-login-back" href="/">← Voltar</a>
      </main>
    </LumisPageShell>
  )
}
