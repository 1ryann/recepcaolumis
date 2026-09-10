import { ArrowLeft, ArrowRight, Eye, EyeOff, LockKeyhole, UserRound } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { customerApi } from '../../api/modules'
import { safeCustomerReturnUrl } from '../../auth/returnUrl'

const passwordHint = 'Use pelo menos 12 caracteres, com maiúscula, minúscula, número e símbolo.'

function isValidCustomerPassword(password: string) {
  return password.length >= 12
    && /[a-z]/.test(password)
    && /[A-Z]/.test(password)
    && /\d/.test(password)
    && /[^A-Za-z0-9]/.test(password)
}

export function CustomerRegister() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const returnUrl = safeCustomerReturnUrl(params.get('returnUrl'))
  const [form, setForm] = useState({ name: '', phone: '', email: '', password: '', confirmation: '' })
  const [showPassword, setShowPassword] = useState(false)
  const [showConfirmation, setShowConfirmation] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError('')
    const name = form.name.trim()
    if (name.length < 2) return setError('Informe seu nome completo.')
    if (!isValidCustomerPassword(form.password)) return setError(passwordHint)
    if (form.password !== form.confirmation) return setError('A confirmação de senha não confere.')
    setLoading(true)
    try {
      await customerApi.register({ ...form, name })
      navigate(`/cliente/login${returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : ''}`, { replace: true, state: { registered: true } })
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 429) setError('Muitas tentativas. Aguarde um pouco e tente novamente.')
      else setError('Não foi possível criar sua conta. Confira os dados e tente novamente.')
    } finally { setLoading(false) }
  }

  return (
    <main className="customer-auth-page">
      <section className="customer-auth-aside">
        <Link className="customer-auth-brand" to="/recepcao"><img src="/lumis-logo-transparent.png" alt="LUMIS" /></Link>
        <div className="customer-auth-message"><span className="eyebrow eyebrow-light">Sua jornada começa aqui</span><h1>Cuide do seu tempo com mais leveza.</h1><p>Encontre seu profissional, agende com tranquilidade e acompanhe tudo em um só lugar.</p></div>
        <div className="customer-auth-note"><UserRound size={19} /><span><strong>Cadastro seguro</strong><small>Usamos seus dados somente para organizar seu atendimento.</small></span></div>
      </section>
      <section className="customer-auth-form-panel">
        <div className="customer-auth-form-wrap">
          <Link className="customer-back-link" to="/cliente/login"><ArrowLeft size={16} /> Voltar para entrar</Link>
          <span className="login-icon"><LockKeyhole size={22} /></span>
          <h2>Criar sua conta</h2>
          <p>Tenha seu próximo atendimento sempre à mão.</p>
          <form onSubmit={submit}>
            <label className="field-label">Nome completo<input className="field-input" required value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} autoComplete="name" placeholder="Como podemos chamar você?" /></label>
            <label className="field-label">WhatsApp / telefone<input className="field-input" required value={form.phone} onChange={(event) => setForm({ ...form, phone: event.target.value })} autoComplete="tel" placeholder="(69) 99999-9999" /></label>
            <label className="field-label">E-mail<input className="field-input" type="email" required value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} autoComplete="email" placeholder="voce@exemplo.com" /></label>
            <label className="field-label">Senha<span className="password-field"><input className="field-input" required type={showPassword ? 'text' : 'password'} value={form.password} onChange={(event) => setForm({ ...form, password: event.target.value })} autoComplete="new-password" /><button type="button" onClick={() => setShowPassword((value) => !value)} aria-label={showPassword ? 'Ocultar senha' : 'Mostrar senha'}>{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></span><small className="field-hint">{passwordHint}</small></label>
            <label className="field-label">Confirmar senha<span className="password-field"><input className="field-input" required type={showConfirmation ? 'text' : 'password'} value={form.confirmation} onChange={(event) => setForm({ ...form, confirmation: event.target.value })} autoComplete="new-password" /><button type="button" onClick={() => setShowConfirmation((value) => !value)} aria-label={showConfirmation ? 'Ocultar confirmação' : 'Mostrar confirmação'}>{showConfirmation ? <EyeOff size={18} /> : <Eye size={18} />}</button></span></label>
            {error && <div className="form-error" role="alert">{error}</div>}
            <button className="primary-button login-submit" type="submit" disabled={loading}>{loading ? <span className="spinner" /> : <>Criar minha conta <ArrowRight size={18} /></>}</button>
          </form>
          <p className="login-secondary-action">Já possui uma conta? <Link to="/cliente/login">Entrar na minha área</Link></p>
        </div>
      </section>
    </main>
  )
}
