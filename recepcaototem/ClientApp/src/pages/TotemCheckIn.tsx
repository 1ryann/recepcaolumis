import { ArrowRight, Camera, Check, Clock3, Keyboard, ShieldCheck, TriangleAlert, UserRound } from 'lucide-react'
import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ApiError } from '../api/client'
import { totemApi, type CheckInPreviewDto } from '../api/modules'
import { Modal } from '../components/Modal'
import { KioskClock } from '../features/totem/KioskClock'
import { LightRays } from '../features/totem/magic/LightRays'
import { normalizeToken } from '../features/totem/normalizeToken'
import { isComplete6, onlyDigits6 } from '../features/totem/sixDigitCode'
import { useQrScanner } from '../features/totem/useQrScanner'

type Segment = 'scan' | 'manual'
type Source = 'scan' | 'manual'

// Seconds the "Chegada registrada" screen stays up before the kiosk resets itself
// for the next person. It is a kiosk UI convenience only — it changes no server state.
const AUTO_RESET_MS = 12_000

const errorFor = (caught: unknown, fallback: string) =>
  caught instanceof ApiError && caught.status === 429 ? 'Muitas tentativas. Aguarde um instante.' : fallback

export function TotemCheckIn() {
  const navigate = useNavigate()
  const [segment, setSegment] = useState<Segment>('scan')
  const [token, setToken] = useState('')
  const [preview, setPreview] = useState<CheckInPreviewDto | null>(null)
  const [confirmed, setConfirmed] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const registerArrival = async (code: string) => {
    await totemApi.confirmCheckIn(code)
    setConfirmed(true)
    stop()
  }

  const resolveToken = async (value: string, source: Source) => {
    const trimmed = value.trim()
    if (!trimmed) return
    setLoading(true); setError(''); setConfirmed(false)
    let dto: CheckInPreviewDto
    try {
      dto = await totemApi.resolveCheckIn(trimmed)
    } catch (caught) {
      setPreview(null); setError(errorFor(caught, source === 'scan' ? 'Não foi possível validar este QR Code.' : 'Não foi possível validar este código.')); setLoading(false)
      return
    }
    setPreview(dto)
    if (source === 'scan' && dto.eligible) {
      try { await registerArrival(trimmed) }
      catch (caught) { setError(errorFor(caught, 'Não foi possível registrar a chegada. Toque em “Confirmar chegada”.')) }
    }
    setLoading(false)
  }

  const { videoRef, state: cameraState, start, stop } = useQrScanner((raw) => {
    const code = normalizeToken(raw)
    setToken(code)
    void resolveToken(code, 'scan')
  })

  useEffect(() => {
    if (cameraState === 'denied' || cameraState === 'unsupported' || cameraState === 'error') setSegment('manual')
  }, [cameraState])

  useEffect(() => stop, [stop])

  const selectSegment = (next: Segment) => {
    if (next === 'manual') stop()
    setSegment(next)
  }

  const submitManual = (event: FormEvent) => { event.preventDefault(); if (!isComplete6(token)) return; void resolveToken(token, 'manual') }

  const confirmManually = async () => {
    setLoading(true); setError('')
    try { await registerArrival(token.trim()) }
    catch (caught) { setError(errorFor(caught, 'Não foi possível registrar a chegada.')) }
    finally { setLoading(false) }
  }

  const reset = useCallback(() => {
    setConfirmed(false); setPreview(null); setToken(''); setError(''); setSegment('scan')
  }, [])

  // "← Voltar", "Concluir" and the auto-return all clear the flow state and then
  // hand the kiosk back to the decision screen at /totem.
  const goHome = useCallback(() => { reset(); navigate('/totem') }, [reset, navigate])

  // After a successful check-in the kiosk hands itself back to the next person.
  useEffect(() => {
    if (!confirmed) return
    const timer = window.setTimeout(goHome, AUTO_RESET_MS)
    return () => window.clearTimeout(timer)
  }, [confirmed, goHome])

  const cameraStatus = cameraState === 'starting' ? 'Abrindo a câmera…'
    : cameraState === 'scanning' ? 'Aponte o QR Code para a câmera.'
    : cameraState === 'denied' ? 'Permissão de câmera negada. Use o código manual abaixo.'
    : cameraState === 'unsupported' ? 'Nenhuma câmera disponível neste dispositivo. Use o código manual.'
    : cameraState === 'error' ? 'Não foi possível abrir a câmera. Use o código manual.'
    : 'A câmera é aberta somente quando você inicia a leitura.'

  return <main className="totem-kiosk">
    <LightRays />
    <button type="button" className="totem-back" aria-label="Voltar" onClick={goHome}>← Voltar</button>

    <aside className="totem-aside">
      <img className="totem-aside-logo" src="/lumis-logo-transparent.png" alt="LUMIS" width={132} height={40} />
      <KioskClock />
    </aside>

    <section className="totem-stage">
      <div className="totem-stage-inner">
        <header className="totem-stage-head">
          <span className="totem-eyebrow">Check-in</span>
          <h2>Confirme sua chegada</h2>
          <p>Escolha como quer identificar seu agendamento.</p>
        </header>

        <div className="totem-options" role="tablist" aria-label="Forma de check-in">
          <button type="button" role="tab" aria-selected={segment === 'scan'}
            aria-label="Escanear QR: use a câmera para ler seu código"
            className={`totem-option ${segment === 'scan' ? 'is-active' : ''}`}
            onClick={() => selectSegment('scan')}>
            <span className="totem-option-icon" aria-hidden="true"><Camera size={26} /></span>
            <span className="totem-option-text">
              <strong>Escanear QR</strong>
              <small>Use a câmera para ler seu código</small>
            </span>
          </button>
          <button type="button" role="tab" aria-selected={segment === 'manual'}
            aria-label="Digitar código: informe os 6 dígitos da sua reserva"
            className={`totem-option ${segment === 'manual' ? 'is-active' : ''}`}
            onClick={() => selectSegment('manual')}>
            <span className="totem-option-icon" aria-hidden="true"><Keyboard size={26} /></span>
            <span className="totem-option-text">
              <strong>Digitar código</strong>
              <small>Digite o código de 6 dígitos da sua reserva</small>
            </span>
          </button>
        </div>

        {segment === 'scan' ? <div className="totem-scan">
          <div className="totem-scan-frame"><video ref={videoRef} muted playsInline /></div>
          <p className="totem-hint" role="status">{cameraStatus}</p>
          {cameraState === 'scanning'
            ? <button className="totem-btn totem-btn-ghost" type="button" onClick={stop}>Parar leitura</button>
            : <button className="totem-btn totem-btn-primary" type="button" disabled={cameraState === 'starting'} onClick={() => void start()}>
                <Camera size={20} aria-hidden="true" /> {cameraState === 'starting' ? 'Abrindo…' : 'Ativar câmera'}
              </button>}
        </div> : <form className="totem-manual" onSubmit={submitManual}>
          <label className="totem-field-label" htmlFor="totem-code">Código de 6 dígitos</label>
          <input id="totem-code" className="totem-input totem-code-input" value={token}
            onChange={(event) => setToken(onlyDigits6(event.target.value))}
            inputMode="numeric" autoComplete="one-time-code" maxLength={6} spellCheck={false}
            placeholder="000000" />
          <button className="totem-btn totem-btn-primary" type="submit" disabled={!isComplete6(token) || loading}>
            {loading ? 'Validando…' : 'Validar agendamento'} <ArrowRight size={20} aria-hidden="true" />
          </button>
        </form>}

        {error && <div className="totem-alert" role="alert">{error}</div>}

        {preview && !confirmed && <div className="totem-preview">
          <div className="totem-preview-head"><ShieldCheck size={20} aria-hidden="true" /><strong>Confirme seus dados</strong></div>
          <div className="totem-preview-row"><UserRound size={19} aria-hidden="true" /><span><small>Profissional</small><strong>{preview.professional}</strong></span></div>
          <div className="totem-preview-row"><Clock3 size={19} aria-hidden="true" /><span><small>Horário</small><strong>{new Date(preview.startAt).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}</strong></span></div>
          {!preview.eligible && <p className="totem-preview-warning"><TriangleAlert size={17} aria-hidden="true" /> Este agendamento ainda não pode ser confirmado agora. Procure a recepção se precisar de ajuda.</p>}
          <button className="totem-btn totem-btn-primary" type="button" onClick={() => void confirmManually()} disabled={loading}>Confirmar chegada <Check size={20} aria-hidden="true" /></button>
        </div>}
      </div>
    </section>

    <Modal open={confirmed} title="Chegada registrada" onClose={goHome}>
      <div className="totem-modal-done">
        <span className="totem-modal-icon" aria-hidden="true"><Check size={30} /></span>
        <p>O profissional foi avisado. Pode aguardar, você será chamado.</p>
        <p className="totem-modal-note">Esta tela volta ao início em alguns segundos.</p>
        <button className="primary-button full-button" type="button" onClick={goHome}>Concluir <ArrowRight size={17} aria-hidden="true" /></button>
      </div>
    </Modal>
  </main>
}
