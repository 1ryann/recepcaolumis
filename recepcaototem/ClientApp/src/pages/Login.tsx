import { ArrowRight, Eye, EyeOff, LockKeyhole, ShieldCheck } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { ApiError } from '../api/client'
import { homeForRoles } from '../components/ProtectedRoute'

export function Login({ audience = 'admin' }: { audience?: 'admin' | 'customer' }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()
  const session = useSession()

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setLoading(true)
    try {
      await session.login(email, password)
      const requested = (location.state as { from?: string } | null)?.from
      const destination = requested && requested.startsWith(audience === 'customer' ? '/cliente' : '/admin')
        ? requested
        : homeForRoles(session.user?.roles ?? [])
      navigate(destination, { replace: true })
    } catch (error) {
      setError(error instanceof ApiError && error.status === 429
        ? 'Muitas tentativas. Aguarde alguns instantes e tente novamente.'
        : 'E-mail ou senha inválidos.')
    } finally { setLoading(false) }
  }

  return (
    <main className="login-page">
      <section className="login-brand-panel">
        <div className="login-brand-top"><img className="login-logo" src="/lumis-logo-transparent.png" alt="LUMIS" /></div>
        <div className="login-message"><span className="eyebrow eyebrow-light">Gestão integrada</span><h1>Seu edifício, organizado em cada detalhe.</h1><p>Recepção, salas, profissionais e locações em uma experiência simples e acolhedora.</p></div>
        <div className="login-trust"><ShieldCheck size={19} /><span><strong>{audience === 'customer' ? 'Acesso do cliente' : 'Acesso administrativo'}</strong><small>{audience === 'customer' ? 'Seus dados protegidos em toda a jornada' : 'Ambiente seguro para gestão do edifício'}</small></span></div>
      </section>
      <section className="login-form-panel">
        <div className="login-form-wrap">
          <div className="login-mobile-logo"><img src="/lumis-logo-transparent.png" alt="LUMIS" /></div>
          <span className="login-icon"><LockKeyhole size={22} /></span>
          <h2>{audience === 'customer' ? 'Área do cliente' : 'Bem-vindo de volta'}</h2>
          <p>{audience === 'customer' ? 'Acesse seus agendamentos e cuide do seu atendimento.' : 'Entre para acessar o painel administrativo.'}</p>
          <form onSubmit={submit}>
            <label className="field-label">E-mail<input className="field-input" type="email" value={email} onChange={(event) => { setEmail(event.target.value); setError('') }} autoComplete="email" /></label>
            <label className="field-label">Senha<span className="password-field"><input className="field-input" type={showPassword ? 'text' : 'password'} value={password} onChange={(event) => { setPassword(event.target.value); setError('') }} autoComplete="current-password" /><button type="button" onClick={() => setShowPassword((value) => !value)} aria-label={showPassword ? 'Ocultar senha' : 'Mostrar senha'}>{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></span></label>
            {error && <div className="form-error" role="alert">{error}</div>}
            <button className="primary-button login-submit" type="submit" disabled={loading}>{loading ? <span className="spinner" /> : <>Entrar {audience === 'customer' ? 'na minha área' : 'no painel'} <ArrowRight size={18} /></>}</button>
          </form>
          {audience === 'customer' && <p className="login-secondary-action">Não possui uma conta? <a href="/cliente/cadastro">Criar minha conta</a></p>}
          {audience === 'admin' && <p className="login-secondary-action">É cliente? <a href="/cliente/login">Acessar minha área</a></p>}
          <a className="back-reception" href="/recepcao">Voltar para a recepção</a>
        </div>
      </section>
    </main>
  )
}
