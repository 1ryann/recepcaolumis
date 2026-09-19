import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { totemApi, type TotemProfessionalCardDto } from '../api/modules'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { KioskClock } from '../features/totem/KioskClock'
import { TotemProfessionalCarousel } from '../features/totem/TotemProfessionalCarousel'
import { BlurFade } from '../features/totem/magic/BlurFade'
import { LumisLogo } from '../theme/LumisLogo'

// The `/totem/profissionais` screen. A visitor without a check-in code lands here from
// `/totem`, picks one professional in the coverflow carousel, and a second tap on the
// already-centred card opens a booking handoff (`totemApi.createHandoff`) and carries the
// returned id/tokens/expiry to `/totem/handoff` via navigation `state` — the Totem NEVER
// navigates to any `/cliente/*` route (the customer finishes the booking on their own phone
// via the QR). A create failure surfaces an inline `role="alert"` below the carousel and the
// visitor stays on it. There is no separate "Continuar"/"Voltar" pair any more: the LUMIS
// logo doubles as the way back to `/totem`. Four phases share the kiosk shell (top bar +
// LumisBackground + a centred BlurFade column):
//   loading -> three shimmer skeleton cards
//   ready   -> carousel (tap the centred card to continue)
//   empty   -> "Nenhum profissional disponível." + Tentar novamente + Tenho código
//   error   -> "Não foi possível carregar os profissionais." + the same two buttons
// A visitor who *does* have a code is never trapped by a professionals-list failure: both
// empty and error offer "Tenho código" straight to `/totem/check-in`. All data comes from
// `totemApi.professionals` — there are no hardcoded professionals or photos here.
// "Alugar sala" is not offered here: it is the third option on the `/totem` entry screen.
type Phase = 'loading' | 'ready' | 'empty' | 'error'

export function TotemProfessionals() {
  const navigate = useNavigate()
  const [phase, setPhase] = useState<Phase>('loading')
  const [professionals, setProfessionals] = useState<TotemProfessionalCardDto[]>([])
  const [active, setActive] = useState<TotemProfessionalCardDto | null>(null)
  const [handoffLoading, setHandoffLoading] = useState(false)
  const [handoffError, setHandoffError] = useState('')

  const continueToHandoff = async () => {
    if (!active) return
    setHandoffLoading(true)
    setHandoffError('')
    try {
      const dto = await totemApi.createHandoff(active.id)
      navigate('/totem/handoff', {
        state: {
          handoffId: dto.id,
          handoffToken: dto.handoffToken,
          statusToken: dto.statusToken,
          professionalId: active.id,
          professionalName: dto.professionalName,
          profession: dto.profession,
          expiresAt: dto.expiresAt,
        },
      })
    } catch {
      setHandoffError('Não foi possível continuar agora. Tente novamente.')
      setHandoffLoading(false)
    }
  }

  // A fresh AbortController per call keeps this callable both from the mount effect
  // (which aborts it on cleanup) and from the "Tentar novamente" button.
  const load = useCallback(() => {
    const controller = new AbortController()
    setPhase('loading')
    totemApi
      .professionals(controller.signal)
      .then((list) => {
        if (list.length) {
          setProfessionals(list)
          setPhase('ready')
        } else {
          setPhase('empty')
        }
      })
      .catch((error: unknown) => {
        if ((error as { name?: string } | null)?.name === 'AbortError') return
        setPhase('error')
      })
    return controller
  }, [])

  useEffect(() => {
    const controller = load()
    return () => controller.abort()
  }, [load])

  return (
    <main className="totem-professionals">
      <LumisBackground />

      <header className="totem-professionals-bar">
        <button
          type="button"
          className="totem-professionals-logo-link"
          onClick={() => navigate('/totem')}
          aria-label="Voltar ao início"
        >
          <LumisLogo
            className="totem-professionals-logo"
            alt="LUMIS"
            width={132}
            height={40}
          />
        </button>
        <KioskClock />
      </header>

      <BlurFade>
        <div className="totem-professionals-inner">
          <div className="totem-professionals-header-row">
            <h1 className="totem-professionals-title">Escolha o profissional</h1>
          </div>

          {phase === 'loading' && (
            <div className="totem-professionals-skeletons">
              <div data-testid="totem-skeleton-card" className="totem-skeleton-card" />
              <div data-testid="totem-skeleton-card" className="totem-skeleton-card" />
              <div data-testid="totem-skeleton-card" className="totem-skeleton-card" />
            </div>
          )}

          {phase === 'ready' && (
            <div className="totem-professionals-slot">
              <TotemProfessionalCarousel
                professionals={professionals}
                onActiveChange={setActive}
                onContinue={() => { if (!handoffLoading) void continueToHandoff() }}
              />
              {handoffError && (
                <p className="totem-professionals-message" role="alert">{handoffError}</p>
              )}
            </div>
          )}

          {phase === 'empty' && (
            <div className="totem-professionals-slot">
              <p className="totem-professionals-message">Nenhum profissional disponível.</p>
              <button type="button" className="totem-btn totem-btn-primary" onClick={load}>
                Tentar novamente
              </button>
              <button
                type="button"
                className="totem-btn totem-btn-ghost"
                onClick={() => navigate('/totem/check-in')}
              >
                Tenho código
              </button>
            </div>
          )}

          {phase === 'error' && (
            <div className="totem-professionals-slot">
              <p className="totem-professionals-message">Não foi possível carregar os profissionais.</p>
              <button type="button" className="totem-btn totem-btn-primary" onClick={load}>
                Tentar novamente
              </button>
              <button
                type="button"
                className="totem-btn totem-btn-ghost"
                onClick={() => navigate('/totem/check-in')}
              >
                Tenho código
              </button>
            </div>
          )}
        </div>
      </BlurFade>
    </main>
  )
}
