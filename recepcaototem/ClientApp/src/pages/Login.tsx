import { ArrowRight, Eye, EyeOff, LockKeyhole, ShieldCheck } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'

export function Login() {
  const [email, setEmail] = useState('admin@demo.com')
  const [password, setPassword] = useState('123456')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (email !== 'admin@demo.com' || password !== '123456') {
      setError('E-mail ou senha incorretos. Confira os dados de acesso.')
      return
    }
    setLoading(true)
    window.setTimeout(() => {
      localStorage.setItem('atrium_session', 'active')
      const destination = (location.state as { from?: string } | null)?.from ?? '/admin'
      navigate(destination, { replace: true })
    }, 550)
  }

  return (
    <main className="login-page">
      <section className="login-brand-panel">
        <div className="login-brand-top"><img className="login-logo" src="/lumis-logo-transparent.png" alt="LUMIS" /></div>
        <div className="login-message"><span className="eyebrow eyebrow-light">Gestão integrada</span><h1>Seu edifício, organizado em cada detalhe.</h1><p>Recepção, salas, profissionais e locações em uma experiência simples e acolhedora.</p></div>
        <div className="login-trust"><ShieldCheck size={19} /><span><strong>Acesso administrativo</strong><small>Ambiente seguro para gestão do edifício</small></span></div>
      </section>
      <section className="login-form-panel">
        <div className="login-form-wrap">
          <div className="login-mobile-logo"><img src="/lumis-logo-transparent.png" alt="LUMIS" /></div>
          <span className="login-icon"><LockKeyhole size={22} /></span>
          <h2>Bem-vindo de volta</h2>
          <p>Entre para acessar o painel administrativo.</p>
          <form onSubmit={submit}>
            <label className="field-label">E-mail<input className="field-input" type="email" value={email} onChange={(event) => { setEmail(event.target.value); setError('') }} autoComplete="email" /></label>
            <label className="field-label">Senha<span className="password-field"><input className="field-input" type={showPassword ? 'text' : 'password'} value={password} onChange={(event) => { setPassword(event.target.value); setError('') }} autoComplete="current-password" /><button type="button" onClick={() => setShowPassword((value) => !value)} aria-label={showPassword ? 'Ocultar senha' : 'Mostrar senha'}>{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></span></label>
            {error && <div className="form-error" role="alert">{error}</div>}
            <button className="primary-button login-submit" type="submit" disabled={loading}>{loading ? <span className="spinner" /> : <>Entrar no painel <ArrowRight size={18} /></>}</button>
          </form>
          <div className="demo-credentials"><strong>Acesso para apresentação</strong><span><b>E-mail</b> admin@demo.com</span><span><b>Senha</b> 123456</span></div>
          <a className="back-reception" href="/recepcao">Voltar para a recepção</a>
        </div>
      </section>
    </main>
  )
}
