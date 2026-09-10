import { ArrowRight, Eye, EyeOff, LockKeyhole, ShieldCheck } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { ApiError } from '../api/client'
import { homeForRoles } from '../auth/roleRoutes'
import { safeCustomerReturnUrl } from '../auth/returnUrl'

export function Login({ audience = 'admin' }: { audience?: 'admin' | 'customer' | 'professional' }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const returnUrl = audience === 'customer' ? safeCustomerReturnUrl(params.get('returnUrl')) : null

  const session = useSession()

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setLoading(true)
    try {
      const current = await session.login(email, password)
      if (!current) throw new Error('Session unavailable')
      navigate(current.mustChangePassword ? '/change-password' : (returnUrl ?? homeForRoles(current.roles)), { replace: true })
    } catch (error) {
      setError(error instanceof ApiError && error.status === 429
        ? 'Muitas tentativas. Aguarde alguns instantes e tente novamente.'
        : 'E-mail ou senha inválidos.')
    } finally { setLoading(false) }
  }

  if (session.status === 'error') return <main className="login-form-panel"><p>Não foi possível confirmar a sessão.</p><button onClick={() => void session.refresh()}>Tentar novamente</button><button onClick={() => void session.logout().catch(() => setError('Saída não confirmada.'))}>Sair / Trocar conta</button></main>
  if (session.status === 'loading') return <div role="status">Confirmando sessão…</div>
  return (
    <main className="login-page">
      <section className="login-brand-panel">
        <div className="login-brand-top"><img className="login-logo" src="/lumis-logo-transparent.png" alt="LUMIS" /></div>
        <div className="login-message"><span className="eyebrow eyebrow-light">Gestão integrada</span><h1>Seu edifício, organizado em cada detalhe.</h1><p>Recepção, salas, profissionais e locações em uma experiência simples e acolhedora.</p></div>
        <div className="login-trust"><ShieldCheck size={19} /><span><strong>{audience === 'customer' ? 'Acesso do cliente' : audience === 'professional' ? 'Acesso profissional' : 'Acesso administrativo'}</strong><small>{audience === 'customer' ? 'Seus dados protegidos em toda a jornada' : audience === 'professional' ? 'Seu dia de trabalho em um só lugar' : 'Ambiente seguro para gestão do edifício'}</small></span></div>
      </section>
      <section className="login-form-panel">
        <div className="login-form-wrap">
          <div className="login-mobile-logo"><img src="/lumis-logo-transparent.png" alt="LUMIS" /></div>
          <span className="login-icon"><LockKeyhole size={22} /></span>
          <h2>{audience === 'customer' ? 'Área do cliente' : audience === 'professional' ? 'Área do profissional' : 'Bem-vindo de volta'}</h2>
          <p>{audience === 'customer' ? 'Acesse seus agendamentos e cuide do seu atendimento.' : audience === 'professional' ? 'Entre para acessar sua área profissional.' : 'Entre para acessar o painel administrativo.'}</p>
          {session.user ? <div><p>Sessão ativa: {session.user.displayName}</p><button className="primary-button" onClick={() => navigate(returnUrl ?? homeForRoles(session.user!.roles))}>Continuar na minha área</button><button className="secondary-button" onClick={async () => { try { await session.logout(); setPassword('') } catch { setError('Não foi possível confirmar a saída. Tente novamente.') } }}>Sair / Trocar conta</button></div> : <form onSubmit={submit}>
            <label className="field-label">E-mail<input className="field-input" type="email" value={email} onChange={(event) => { setEmail(event.target.value); setError('') }} autoComplete="email" /></label>
            <label className="field-label">Senha<span className="password-field"><input className="field-input" type={showPassword ? 'text' : 'password'} value={password} onChange={(event) => { setPassword(event.target.value); setError('') }} autoComplete="current-password" /><button type="button" onClick={() => setShowPassword((value) => !value)} aria-label={showPassword ? 'Ocultar senha' : 'Mostrar senha'}>{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></span></label>
            {error && <div className="form-error" role="alert">{error}</div>}
            <button className="primary-button login-submit" type="submit" disabled={loading}>{loading ? <span className="spinner" /> : <>Entrar{audience === 'customer' ? ' na minha área' : audience === 'admin' ? ' no painel' : ''} <ArrowRight size={18} /></>}</button>
          </form>}
          {audience === 'customer' && <p className="login-secondary-action">Não possui uma conta? <a href={`/cliente/cadastro${returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : ''}`}>Criar minha conta</a></p>}
          {audience === 'professional' && <p className="login-secondary-action">Ainda não possui cadastro? <a href="/profissional/cadastro">Solicitar cadastro profissional</a></p>}
          {audience === 'admin' && <p className="login-secondary-action">É cliente? <a href="/cliente/login">Acessar minha área</a></p>}
          {audience !== 'professional' && <p className="login-secondary-action"><a href="/profissional/login">Área do profissional</a></p>}<a className="back-reception" href="/">← Voltar</a>
        </div>
      </section>
    </main>
  )
}
