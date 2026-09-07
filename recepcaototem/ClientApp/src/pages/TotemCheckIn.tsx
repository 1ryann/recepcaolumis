import { ArrowLeft, ArrowRight, Check, Clock3, QrCode, ShieldCheck, UserRound } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { totemApi, type CheckInPreviewDto } from '../api/modules'

export function TotemCheckIn() {
  const [token, setToken] = useState('')
  const [preview, setPreview] = useState<CheckInPreviewDto | null>(null)
  const [confirmed, setConfirmed] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const resolve = async (event: FormEvent) => {
    event.preventDefault(); setLoading(true); setError(''); setConfirmed(false)
    try { setPreview(await totemApi.resolveCheckIn(token.trim())) }
    catch (caught) { setPreview(null); setError(caught instanceof ApiError && caught.status === 429 ? 'Muitas tentativas. Aguarde um instante.' : 'Não foi possível validar este QR Code.') }
    finally { setLoading(false) }
  }
  const confirm = async () => {
    setLoading(true); setError('')
    try { await totemApi.confirmCheckIn(token.trim()); setConfirmed(true) }
    catch (caught) { setError(caught instanceof ApiError && caught.status === 429 ? 'Muitas tentativas. Aguarde um instante.' : 'Não foi possível registrar a chegada.') }
    finally { setLoading(false) }
  }
  return <main className="totem-checkin-page"><header className="totem-checkin-header"><Link className="customer-brand" to="/recepcao"><img src="/lumis-logo-dark.png" alt="LUMIS" /></Link><Link className="customer-back-link" to="/recepcao"><ArrowLeft size={16} /> Voltar</Link></header><section className="totem-checkin-card panel">{confirmed ? <div className="totem-checkin-success"><span><Check size={34} /></span><span className="eyebrow">Chegada registrada</span><h1>Pronto, estamos esperando por você.</h1><p>O profissional foi avisado. Aguarde ser chamado.</p><Link className="primary-button" to="/recepcao">Concluir <ArrowRight size={17} /></Link></div> : <><div className="totem-checkin-mark"><QrCode size={25} /></div><span className="eyebrow">Check-in</span><h1>Já tenho agendamento</h1><p>Apresente seu QR Code ou insira o código abaixo para confirmar sua chegada.</p><form onSubmit={resolve}><label className="field-label">Código do QR Code<input className="field-input" value={token} onChange={(event) => setToken(event.target.value)} placeholder="Cole o código aqui" autoComplete="off" /></label>{error && <div className="form-error" role="alert">{error}</div>}<button className="primary-button full-button" type="submit" disabled={!token.trim() || loading}>{loading ? 'Validando…' : 'Validar agendamento'} <ArrowRight size={17} /></button></form>{preview && <div className="totem-checkin-preview"><div className="totem-preview-heading"><ShieldCheck size={18} /><strong>Confirme seus dados</strong></div><div className="totem-preview-row"><UserRound size={17} /><span><small>Profissional</small><strong>{preview.professional}</strong></span></div><div className="totem-preview-row"><Clock3 size={17} /><span><small>Horário</small><strong>{new Date(preview.startAt).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}</strong></span></div><button className="secondary-button full-button" type="button" onClick={confirm} disabled={loading}>Confirmar chegada <Check size={17} /></button></div>}</> }</section></main>
}
