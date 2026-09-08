import { ArrowLeft, ArrowRight, Camera, Check, Clock3, Keyboard, QrCode, ShieldCheck, UserRound } from 'lucide-react'
import { type FormEvent, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { totemApi, type CheckInPreviewDto } from '../api/modules'
import { normalizeToken } from '../features/totem/normalizeToken'
import { useQrScanner } from '../features/totem/useQrScanner'

type Segment = 'scan' | 'manual'

const errorFor = (caught: unknown, fallback: string) =>
  caught instanceof ApiError && caught.status === 429 ? 'Muitas tentativas. Aguarde um instante.' : fallback

export function TotemCheckIn() {
  const [segment, setSegment] = useState<Segment>('scan')
  const [token, setToken] = useState('')
  const [preview, setPreview] = useState<CheckInPreviewDto | null>(null)
  const [confirmed, setConfirmed] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const resolveToken = async (value: string) => {
    const trimmed = value.trim()
    if (!trimmed) return
    setLoading(true); setError(''); setConfirmed(false)
    try { setPreview(await totemApi.resolveCheckIn(trimmed)) }
    catch (caught) { setPreview(null); setError(errorFor(caught, 'Não foi possível validar este QR Code.')) }
    finally { setLoading(false) }
  }

  const { videoRef, state: cameraState, start, stop } = useQrScanner((raw) => {
    const code = normalizeToken(raw)
    setToken(code)
    void resolveToken(code)
  })

  useEffect(() => {
    if (cameraState === 'denied' || cameraState === 'unsupported' || cameraState === 'error') setSegment('manual')
  }, [cameraState])

  useEffect(() => stop, [stop])

  const selectSegment = (next: Segment) => {
    if (next === 'manual') stop()
    setSegment(next)
  }

  const submitManual = (event: FormEvent) => { event.preventDefault(); void resolveToken(token) }

  const confirm = async () => {
    setLoading(true); setError('')
    try { await totemApi.confirmCheckIn(token.trim()); setConfirmed(true); stop() }
    catch (caught) { setError(errorFor(caught, 'Não foi possível registrar a chegada.')) }
    finally { setLoading(false) }
  }

  const cameraStatus = cameraState === 'starting' ? 'Abrindo a câmera…'
    : cameraState === 'scanning' ? 'Aponte o QR Code para a câmera.'
    : cameraState === 'denied' ? 'Permissão de câmera negada. Use o código manual abaixo.'
    : cameraState === 'unsupported' ? 'Nenhuma câmera disponível neste dispositivo. Use o código manual.'
    : cameraState === 'error' ? 'Não foi possível abrir a câmera. Use o código manual.'
    : 'A câmera é aberta somente quando você inicia a leitura.'

  return <main className="totem-checkin-page">
    <header className="totem-checkin-header">
      <Link className="customer-brand" to="/recepcao"><img src="/lumis-logo-dark.png" alt="LUMIS" /></Link>
      <Link className="customer-back-link" to="/recepcao"><ArrowLeft size={16} /> Voltar</Link>
    </header>
    <section className="totem-checkin-card panel">
      {confirmed ? <div className="totem-checkin-success">
        <span><Check size={34} /></span>
        <span className="eyebrow">Chegada registrada</span>
        <h1>Pronto, estamos esperando por você.</h1>
        <p>O profissional foi avisado. Aguarde ser chamado.</p>
        <Link className="primary-button" to="/recepcao">Concluir <ArrowRight size={17} /></Link>
      </div> : <>
        <div className="totem-checkin-mark"><QrCode size={25} /></div>
        <span className="eyebrow">Check-in</span>
        <h1>Já tenho agendamento</h1>
        <p>Escaneie seu QR Code com a câmera ou digite o código para confirmar sua chegada.</p>

        <div className="totem-segments" role="tablist">
          <button type="button" role="tab" aria-selected={segment === 'scan'}
            className={`totem-segment ${segment === 'scan' ? 'is-active' : ''}`}
            onClick={() => selectSegment('scan')}><Camera size={16} /> Escanear QR</button>
          <button type="button" role="tab" aria-selected={segment === 'manual'}
            className={`totem-segment ${segment === 'manual' ? 'is-active' : ''}`}
            onClick={() => selectSegment('manual')}><Keyboard size={16} /> Digitar código</button>
        </div>

        {segment === 'scan' ? <div className="totem-scanner">
          <div className="totem-scanner-frame"><video ref={videoRef} muted playsInline /></div>
          <p className="totem-scanner-status" role="status">{cameraStatus}</p>
          {cameraState === 'scanning'
            ? <button className="secondary-button full-button" type="button" onClick={stop}>Parar leitura</button>
            : <button className="primary-button full-button" type="button" disabled={cameraState === 'starting'} onClick={() => void start()}>
                <Camera size={17} /> {cameraState === 'starting' ? 'Abrindo…' : 'Ativar câmera'}
              </button>}
        </div> : <form onSubmit={submitManual}>
          <label className="field-label">Código do QR Code
            <input className="field-input" value={token} onChange={(event) => setToken(event.target.value)}
              placeholder="Cole o código aqui" autoComplete="off" />
          </label>
          <button className="primary-button full-button" type="submit" disabled={!token.trim() || loading}>
            {loading ? 'Validando…' : 'Validar agendamento'} <ArrowRight size={17} />
          </button>
        </form>}

        {error && <div className="form-error" role="alert">{error}</div>}

        {preview && <div className="totem-checkin-preview">
          <div className="totem-preview-heading"><ShieldCheck size={18} /><strong>Confirme seus dados</strong></div>
          <div className="totem-preview-row"><UserRound size={17} /><span><small>Profissional</small><strong>{preview.professional}</strong></span></div>
          <div className="totem-preview-row"><Clock3 size={17} /><span><small>Horário</small><strong>{new Date(preview.startAt).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}</strong></span></div>
          <button className="secondary-button full-button" type="button" onClick={confirm} disabled={loading}>Confirmar chegada <Check size={17} /></button>
        </div>}
      </>}
    </section>
  </main>
}
