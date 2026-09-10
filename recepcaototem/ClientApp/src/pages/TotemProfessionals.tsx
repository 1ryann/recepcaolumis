import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { totemApi, type TotemProfessionalCardDto } from '../api/modules'
import { KioskClock } from '../features/totem/KioskClock'
import { TotemProfessionalCarousel } from '../features/totem/TotemProfessionalCarousel'
import { BlurFade } from '../features/totem/magic/BlurFade'
import { LightRays } from '../features/totem/magic/LightRays'
import { RippleButton } from '../features/totem/magic/RippleButton'

// The `/totem/profissionais` screen. A visitor without a check-in code lands here from
// `/totem`, picks one professional in the swipe carousel, and "Continuar →" carries that
// choice to the CUSTOMER booking route as `?professionalId=<id>` and nothing else (spec
// §5.4 — no origin/analytics param). Four phases share the kiosk shell (top bar + LightRays
// + a centred BlurFade column):
//   loading -> three shimmer skeleton cards
//   ready   -> carousel + Continuar + Voltar
//   empty   -> "Nenhum profissional disponível." + Tentar novamente + Tenho código
//   error   -> "Não foi possível carregar os profissionais." + the same two buttons
// A visitor who *does* have a code is never trapped by a professionals-list failure: both
// empty and error offer "Tenho código" straight to `/totem/check-in`. All data comes from
// `totemApi.professionals` — there are no hardcoded professionals or photos here.
type Phase = 'loading' | 'ready' | 'empty' | 'error'

export function TotemProfessionals() {
  const navigate = useNavigate()
  const [phase, setPhase] = useState<Phase>('loading')
  const [professionals, setProfessionals] = useState<TotemProfessionalCardDto[]>([])
  const [active, setActive] = useState<TotemProfessionalCardDto | null>(null)

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
      <LightRays />

      <header className="totem-professionals-bar">
        <img
          className="totem-professionals-logo"
          src="/lumis-logo-transparent.png"
          alt="LUMIS"
          width={132}
          height={40}
        />
        <KioskClock />
      </header>

      <BlurFade>
        <div className="totem-professionals-inner">
          <h1 className="totem-professionals-title">Escolha o profissional</h1>

          {phase === 'loading' && (
            <div className="totem-professionals-skeletons">
              <div data-testid="totem-skeleton-card" className="totem-skeleton-card" />
              <div data-testid="totem-skeleton-card" className="totem-skeleton-card" />
              <div data-testid="totem-skeleton-card" className="totem-skeleton-card" />
            </div>
          )}

          {phase === 'ready' && (
            <div className="totem-professionals-slot">
              <TotemProfessionalCarousel professionals={professionals} onActiveChange={setActive} />
              <RippleButton
                className="totem-continue"
                disabled={!active}
                onClick={() => active && navigate('/cliente/agendar?professionalId=' + active.id)}
              >
                Continuar →
              </RippleButton>
              <button type="button" className="totem-back" onClick={() => navigate('/totem')}>
                ← Voltar
              </button>
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
