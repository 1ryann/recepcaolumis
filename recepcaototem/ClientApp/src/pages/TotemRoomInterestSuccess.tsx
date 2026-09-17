import QRCode from 'qrcode'
import { useEffect, useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { LumisLogo } from '../theme/LumisLogo'

// `/totem/salas/:id/interesse` — the static hand-off screen shown right after a room
// rental inquiry is submitted successfully on TotemRoomDetail.tsx. Everything it needs
// (`roomName`, `whatsappUrl`, `presentedAvailabilityLabel`) arrives ONLY in navigation
// state — never storage, never the URL — and `inquiryId` is deliberately not part of that
// shape: nothing here shows or persists it.
//
// Unlike TotemHandoff.tsx, this screen is static once the QR renders: no polling, no
// countdown/expiry, no auto-return timers — enforced by a test asserting
// `vi.getTimerCount() === 0`. That is also why, unlike every other Totem screen, the
// header does NOT include `KioskClock`: it self-schedules a 20s interval to keep the wall
// clock live, which would make this the one screen that is not truly timer-free. The
// only thing reused from TotemHandoff.tsx is the `qrcode` call pattern and its exact
// `QR_OPTS`. A refresh loses `location.state` (same as TotemHandoff) — with no state, or
// a `whatsappUrl` that fails validation, this bounces to `/totem/salas` rather than ever
// handing an unvalidated target to `window.open`.
type RoomInterestNavState = {
  roomName: string
  whatsappUrl: string
  presentedAvailabilityLabel: string
}

const QR_OPTS = { margin: 1, width: 320, color: { dark: '#181818', light: '#ffffff' } } as const

function isValidWhatsappUrl(value: string): boolean {
  try {
    const url = new URL(value)
    return url.protocol === 'https:' && url.hostname === 'wa.me'
  } catch {
    return false
  }
}

function isValidState(value: unknown): value is RoomInterestNavState {
  const state = value as Partial<RoomInterestNavState> | null
  return Boolean(
    state
    && typeof state.roomName === 'string' && state.roomName
    && typeof state.whatsappUrl === 'string' && isValidWhatsappUrl(state.whatsappUrl)
    && typeof state.presentedAvailabilityLabel === 'string' && state.presentedAvailabilityLabel,
  )
}

export function TotemRoomInterestSuccess() {
  const location = useLocation()
  const state = location.state
  if (!isValidState(state)) return <Navigate to="/totem/salas" replace />
  return <TotemRoomInterestSuccessScreen state={state} />
}

function TotemRoomInterestSuccessScreen({ state }: { state: RoomInterestNavState }) {
  const navigate = useNavigate()
  const [qrDataUrl, setQrDataUrl] = useState('')

  useEffect(() => {
    let cancelled = false
    QRCode.toDataURL(state.whatsappUrl, QR_OPTS)
      .then((data) => { if (!cancelled) setQrDataUrl(data) })
      .catch(() => { /* "Abrir WhatsApp" below does not depend on the QR image */ })
    return () => { cancelled = true }
  }, [state.whatsappUrl])

  const openWhatsapp = () => window.open(state.whatsappUrl, '_blank', 'noopener,noreferrer')

  return (
    <main className="totem-room-interest">
      <LumisBackground />

      <header className="totem-room-interest-bar">
        <button
          type="button"
          className="totem-room-interest-logo-link"
          onClick={() => navigate('/totem')}
          aria-label="Voltar ao início"
        >
          <LumisLogo
            className="totem-room-interest-logo"
            alt="LUMIS"
            width={132}
            height={40}
          />
        </button>
      </header>

      <div className="totem-room-interest-inner">
        <h1 className="totem-room-interest-title">Continue no WhatsApp</h1>
        <p className="totem-room-interest-room">
          <strong>{state.roomName}</strong>
          <span>{state.presentedAvailabilityLabel}</span>
        </p>

        {qrDataUrl && (
          <div className="totem-room-interest-qr-card">
            <img
              className="totem-room-interest-qr"
              src={qrDataUrl}
              alt="QR Code para continuar no WhatsApp"
            />
          </div>
        )}

        <p className="totem-room-interest-hint">Escaneie para continuar no seu celular</p>

        <button type="button" className="totem-btn totem-btn-primary" onClick={openWhatsapp}>
          Abrir WhatsApp
        </button>

        <div className="totem-room-interest-actions">
          <button type="button" className="totem-btn totem-btn-ghost" onClick={() => navigate('/totem/salas')}>
            ← Voltar para salas
          </button>
          <button type="button" className="totem-btn totem-btn-ghost" onClick={() => navigate('/totem')}>
            Início
          </button>
        </div>
      </div>
    </main>
  )
}
