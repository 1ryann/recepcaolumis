import QRCode from 'qrcode'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { totemApi } from '../api/modules'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { KioskClock } from '../features/totem/KioskClock'
import { usePrefersReducedMotion } from '../features/totem/magic/usePrefersReducedMotion'

// `/totem/handoff` — the Totem side of the booking handoff. The visitor picked a
// professional on `/totem/profissionais`; "Continuar" opened a handoff and navigated
// here with everything in `location.state` (never storage — the `statusToken` lives
// only in memory). This screen shows the QR the visitor scans to finish the booking on
// their own phone, counts down to `expiresAt`, and polls `pollHandoff` every 2 s until a
// terminal status. On COMPLETED it shows a ✓ screen and auto-returns to `/totem` after
// 6 s. The Totem itself NEVER navigates to any `/cliente/*` route — the customer URL is
// only ever encoded inside the QR image.
//
// F5 / direct navigation loses `location.state`: with no state we bounce to `/totem`.

type HandoffNavState = {
  handoffId: string
  handoffToken: string
  statusToken: string
  professionalId: string
  professionalName: string
  profession: string
  expiresAt: string
}

type Screen =
  | { phase: 'pending' }
  | { phase: 'completed'; professionalName: string; startAt: string }
  | { phase: 'expired' }

const RECONNECT_AFTER = 5
const QR_OPTS = { margin: 1, width: 320, color: { dark: '#181818', light: '#ffffff' } } as const

function pad2(n: number): string {
  return String(Math.max(0, n)).padStart(2, '0')
}

function formatCountdown(msRemaining: number): string {
  const total = Math.max(0, Math.ceil(msRemaining / 1000))
  return `${pad2(Math.floor(total / 60))}:${pad2(total % 60)}`
}

function dateLabel(value: string): string {
  return new Date(value).toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: 'long' })
}
function timeLabel(value: string): string {
  return new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
}

export function TotemHandoff() {
  const location = useLocation()
  const nav = location.state as HandoffNavState | null
  if (!nav || !nav.handoffId || !nav.statusToken) return <Navigate to="/totem" replace />
  return <TotemHandoffScreen nav={nav} />
}

function TotemHandoffScreen({ nav }: { nav: HandoffNavState }) {
  const navigate = useNavigate()
  const reduced = usePrefersReducedMotion()

  // Handoff identity — seeded from navigation state, replaced on "Gerar novo QR".
  const [handoffId, setHandoffId] = useState(nav.handoffId)
  const [statusToken, setStatusToken] = useState(nav.statusToken)
  const [handoffToken, setHandoffToken] = useState(nav.handoffToken)
  const [expiresAt, setExpiresAt] = useState(nav.expiresAt)

  const [screen, setScreen] = useState<Screen>({ phase: 'pending' })
  const [qrDataUrl, setQrDataUrl] = useState('')
  const [failures, setFailures] = useState(0)
  const [nowTick, setNowTick] = useState(() => Date.now())

  // --- QR image (re-generated whenever the handoff token changes) ---------------
  useEffect(() => {
    let cancelled = false
    const target = `${window.location.origin}/cliente/agendar?handoff=${handoffToken}`
    QRCode.toDataURL(target, QR_OPTS)
      .then((data) => { if (!cancelled) setQrDataUrl(data) })
      .catch(() => { /* the manual copy path is not part of the kiosk flow */ })
    return () => { cancelled = true }
  }, [handoffToken])

  // --- Polling every 2 s while PENDING ----------------------------------------
  // `pollCancelledRef` mirrors the `cancelled` flag the QR effect above uses: it is
  // flipped true whenever the polling effect below tears down (unmount, or the phase
  // leaving 'pending'), so a `pollHandoff` promise still in flight at that point can't
  // call `setScreen`/`setFailures` afterwards.
  const pollCancelledRef = useRef(false)
  const poll = useCallback(async () => {
    try {
      const res = await totemApi.pollHandoff(handoffId, statusToken)
      if (pollCancelledRef.current) return
      setFailures(0)
      if (res.status === 'PENDING') {
        setExpiresAt(res.expiresAt)
      } else if (res.status === 'COMPLETED') {
        setScreen({ phase: 'completed', professionalName: res.professionalName, startAt: res.startAt })
      } else {
        setScreen({ phase: 'expired' })
      }
    } catch {
      if (!pollCancelledRef.current) setFailures((n) => n + 1)
    }
  }, [handoffId, statusToken])

  useEffect(() => {
    if (screen.phase !== 'pending') return
    pollCancelledRef.current = false
    const id = window.setInterval(() => { void poll() }, 2000)
    return () => { pollCancelledRef.current = true; window.clearInterval(id) }
  }, [screen.phase, poll])

  // --- Coarse countdown tick (1 s visual; 30 s under reduced motion) ----------
  useEffect(() => {
    if (screen.phase !== 'pending') return
    const id = window.setInterval(() => setNowTick(Date.now()), reduced ? 30_000 : 1_000)
    return () => window.clearInterval(id)
  }, [screen.phase, reduced])

  // --- Auto-return 6 s after completion --------------------------------------
  useEffect(() => {
    if (screen.phase !== 'completed') return
    const id = window.setTimeout(() => navigate('/totem', { replace: true }), 6000)
    return () => window.clearTimeout(id)
  }, [screen.phase, navigate])

  const regenerating = useRef(false)
  const regenerate = async () => {
    if (regenerating.current) return
    regenerating.current = true
    try {
      const dto = await totemApi.createHandoff(nav.professionalId)
      setHandoffId(dto.id)
      setStatusToken(dto.statusToken)
      setHandoffToken(dto.handoffToken)
      setExpiresAt(dto.expiresAt)
      setFailures(0)
      setNowTick(Date.now())
      setScreen({ phase: 'pending' })
    } catch {
      /* stay on the expired screen; the visitor can try again */
    } finally {
      regenerating.current = false
    }
  }

  const chooseAnother = async () => {
    await totemApi.cancelHandoff(handoffId, statusToken).catch(() => {})
    navigate('/totem/profissionais')
  }

  const msRemaining = new Date(expiresAt).getTime() - nowTick
  // The announced text only changes on a 30 s boundary, so the aria-live region
  // mutates coarsely even though the visual ticker below it updates every second.
  const coarseCountdown = formatCountdown(Math.ceil(msRemaining / 30_000) * 30_000)

  return (
    <main className="totem-handoff">
      <LumisBackground />

      <header className="totem-handoff-bar">
        <img src="/lumis-logo-transparent.png" alt="LUMIS" width={120} height={36} />
        <KioskClock />
      </header>

      <div className="totem-handoff-inner">
        {screen.phase === 'pending' && (
          <div className="totem-handoff-stage">
            <h1 className="totem-handoff-title">Continue no seu celular</h1>
            <p className="totem-handoff-professional">
              <strong>{nav.professionalName}</strong>
              <span>{nav.profession}</span>
            </p>

            {qrDataUrl && (
              <div className="totem-handoff-qr-card">
                <img
                  className="totem-handoff-qr"
                  src={qrDataUrl}
                  alt="QR Code para continuar o agendamento no seu celular"
                />
              </div>
            )}

            <p className="totem-handoff-hint">Escaneie para continuar seu agendamento</p>

            <div className="totem-handoff-waiting">
              <p>
                <span className={reduced ? undefined : 'totem-handoff-spinner'} aria-hidden="true" />
                Aguardando conclusão...
              </p>
              <p className="totem-handoff-countdown" aria-hidden="true">
                {formatCountdown(msRemaining)}
              </p>
              <p className="sr-only" aria-live="polite">
                Tempo restante aproximado {coarseCountdown}
              </p>
            </div>

            {failures >= RECONNECT_AFTER && (
              <div className="totem-handoff-reconnect" role="status">
                <span>Reconectando…</span>
                <button type="button" className="totem-back" onClick={() => { void poll() }}>
                  Tentar novamente
                </button>
              </div>
            )}

            <button type="button" className="totem-handoff-choose" onClick={() => { void chooseAnother() }}>
              ← Escolher outro profissional
            </button>
          </div>
        )}

        {screen.phase === 'completed' && (
          <div className="totem-handoff-stage">
            <p className="totem-handoff-check" aria-hidden="true">✓</p>
            <h1 className="totem-handoff-title">Agendamento concluído!</h1>
            <p className="totem-handoff-professional">
              <strong>{screen.professionalName}</strong>
            </p>
            <p className="totem-handoff-when">
              {dateLabel(screen.startAt)} · {timeLabel(screen.startAt)}
            </p>
            <p className="totem-handoff-hint">Tudo certo por aqui.</p>
            <p className="totem-handoff-hint">Retornando ao início...</p>
          </div>
        )}

        {screen.phase === 'expired' && (
          <div className="totem-handoff-stage">
            <h1 className="totem-handoff-title">Este QR Code expirou.</h1>
            <p className="totem-handoff-hint">
              Gere um novo código para continuar ou escolha outro profissional.
            </p>
            <div className="totem-handoff-actions">
              <button
                type="button"
                className="totem-continue"
                onClick={() => { void regenerate() }}
              >
                Gerar novo QR
              </button>
              <button
                type="button"
                className="totem-back"
                onClick={() => navigate('/totem/profissionais')}
              >
                Escolher outro profissional
              </button>
            </div>
          </div>
        )}
      </div>
    </main>
  )
}
