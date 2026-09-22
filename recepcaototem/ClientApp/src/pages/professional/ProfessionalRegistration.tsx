import { ArrowLeft, ArrowRight, Eye, EyeOff } from 'lucide-react'
import { type ChangeEvent, type FormEvent, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { professionalRegistrationApi } from '../../api/modules'
import { LumisPageShell } from '../../features/lumis/LumisPageShell'
import { LumisLogo } from '../../theme/LumisLogo'
import { caretAfterFormat, formatBrazilWhatsApp, isCompleteWhatsApp, whatsAppDigits } from '../../utils/whatsappMask'

export function ProfessionalRegistration() {
  const [form, setForm] = useState({ name: '', profession: '', whatsApp: '', email: '', password: '', confirmation: '', description: '' })
  const [showPassword, setShowPassword] = useState(false)
  const [showConfirmation, setShowConfirmation] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const whatsAppRef = useRef<HTMLInputElement>(null)
  const change = (field: keyof typeof form, value: string) => setForm((current) => ({ ...current, [field]: value }))
  // WhatsApp field keeps only the digits in state; the mask is presentation. The caret is
  // restored to the same digit offset so editing in the middle stays natural.
  const changeWhatsApp = (event: ChangeEvent<HTMLInputElement>) => {
    const element = event.target
    const caret = element.selectionStart ?? element.value.length
    const digitsBeforeCaret = element.value.slice(0, caret).replace(/\D/g, '').length
    const digits = whatsAppDigits(element.value)
    change('whatsApp', digits)
    requestAnimationFrame(() => {
      const position = caretAfterFormat(digitsBeforeCaret, formatBrazilWhatsApp(digits))
      whatsAppRef.current?.setSelectionRange(position, position)
    })
  }
  const submit = async (event: FormEvent) => {
    event.preventDefault(); setError('')
    if (form.password !== form.confirmation) { setError('As senhas não coincidem.'); return }
    if (!isCompleteWhatsApp(form.whatsApp)) { setError('Informe um WhatsApp válido com DDD.'); return }
    if (form.description.includes('<') || form.description.includes('>')) { setError('A descrição não pode conter HTML.'); return }
    setLoading(true)
    try { await professionalRegistrationApi.register({ ...form, description: form.description.trim() || null }); setSent(true) }
    catch (failure) { setError(failure instanceof ApiError && failure.status === 429 ? 'Muitas tentativas. Aguarde e tente novamente.' : 'Não foi possível concluir o cadastro. Confira os dados e tente novamente.') }
    finally { setLoading(false) }
  }
  if (sent) return <LumisPageShell className="lumis-login"><main className="lumis-login-card lumis-auth-surface is-wide"><LumisLogo className="lumis-login-logo" alt="LUMIS" width={124} height={38} /><span className="lumis-login-eyebrow">ÁREA DO PROFISSIONAL</span><h1 className="lumis-login-title">Cadastro enviado</h1><p className="lumis-login-text">Recebemos sua solicitação de cadastro profissional.</p><p className="lumis-login-text">Você poderá acessar sua área assim que a gerência aprovar o cadastro.</p><Link className="primary-button lumis-login-submit" to="/profissional/login">Entrar e acompanhar</Link></main></LumisPageShell>
  return <LumisPageShell className="lumis-login"><main className="lumis-login-card lumis-auth-surface is-wide"><LumisLogo className="lumis-login-logo" alt="LUMIS" width={124} height={38} /><span className="lumis-login-eyebrow">ÁREA DO PROFISSIONAL</span><h1 className="lumis-login-title">Solicitar cadastro profissional</h1><p className="lumis-login-text">Envie seus dados para análise da gerência.</p><form onSubmit={submit} className="lumis-login-form">
    <label className="field-label">Nome completo<input className="field-input" value={form.name} maxLength={200} required autoComplete="name" placeholder="Seu nome completo" onChange={(e) => change('name', e.target.value)} /></label>
    <label className="field-label">Profissão / especialidade<input className="field-input" value={form.profession} maxLength={150} required placeholder="Ex.: Psicóloga, Nutricionista" onChange={(e) => change('profession', e.target.value)} /></label>
    <label className="field-label">WhatsApp<input ref={whatsAppRef} className="field-input" value={formatBrazilWhatsApp(form.whatsApp)} inputMode="numeric" autoComplete="tel-national" maxLength={16} placeholder="(69) 99999-9999" required onChange={changeWhatsApp} /></label>
    <label className="field-label">E-mail<input className="field-input" type="email" value={form.email} required autoComplete="email" placeholder="voce@exemplo.com" onChange={(e) => change('email', e.target.value)} /></label>
    <label className="field-label">Senha<span className="password-field"><input className="field-input" type={showPassword ? 'text' : 'password'} value={form.password} required autoComplete="new-password" onChange={(e) => change('password', e.target.value)} /><button type="button" aria-label={showPassword ? 'Ocultar senha' : 'Mostrar senha'} onClick={() => setShowPassword(v => !v)}>{showPassword ? <EyeOff size={18}/> : <Eye size={18}/>}</button></span></label>
    <label className="field-label">Confirmar senha<span className="password-field"><input className="field-input" type={showConfirmation ? 'text' : 'password'} value={form.confirmation} required autoComplete="new-password" onChange={(e) => change('confirmation', e.target.value)} /><button type="button" aria-label={showConfirmation ? 'Ocultar confirmação' : 'Mostrar confirmação'} onClick={() => setShowConfirmation(v => !v)}>{showConfirmation ? <EyeOff size={18}/> : <Eye size={18}/>}</button></span></label>
    <label className="field-label">Descrição profissional<textarea className="field-input" value={form.description} maxLength={500} rows={4} onChange={(e) => change('description', e.target.value)} /></label>
    {error && <div className="form-error" role="alert">{error}</div>}<button className="primary-button lumis-login-submit" disabled={loading} type="submit">{loading ? 'Enviando…' : <>Enviar solicitação <ArrowRight size={17}/></>}</button>
  </form><Link className="lumis-login-back" to="/profissional/login"><ArrowLeft size={16} /> Voltar para entrar</Link></main></LumisPageShell>
}
