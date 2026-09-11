import { ArrowRight, QrCode, UserRound } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { KioskClock } from '../features/totem/KioskClock'
import { BlurFade } from '../features/totem/magic/BlurFade'
import { MagicCard } from '../features/totem/magic/MagicCard'

// The `/totem` decision screen. It asks one question — "Como deseja continuar?" — and
// routes to the code-based check-in or the professional carousel. No QR field and no code
// input live here; those belong to `/totem/check-in`. The two options are real <button>
// elements (via MagicCard) so they are reachable by keyboard and by role/name queries.
export function TotemEntry() {
  const navigate = useNavigate()

  return (
    <main className="totem-entry">
      <LumisBackground />
      <div className="totem-entry-inner">
        <BlurFade>
          <img
            className="totem-entry-logo"
            src="/lumis-logo-transparent.png"
            alt="LUMIS"
            width={132}
            height={40}
          />
        </BlurFade>

        <BlurFade delay={80}>
          <span className="totem-eyebrow totem-entry-eyebrow">Bem-vindo</span>
          <h1 className="totem-entry-title">Como deseja continuar?</h1>
        </BlurFade>

        <BlurFade delay={160}>
          <div className="totem-entry-options">
            <MagicCard
              as="button"
              className="totem-entry-card"
              onClick={() => navigate('/totem/check-in')}
            >
              <span className="totem-entry-card-icon" aria-hidden="true"><QrCode size={22} /></span>
              <span className="totem-entry-card-label">Tenho código</span>
              <ArrowRight className="totem-entry-card-arrow" size={20} aria-hidden="true" />
            </MagicCard>

            <MagicCard
              as="button"
              className="totem-entry-card"
              onClick={() => navigate('/totem/profissionais')}
            >
              <span className="totem-entry-card-icon" aria-hidden="true"><UserRound size={22} /></span>
              <span className="totem-entry-card-label">Não tenho código</span>
              <ArrowRight className="totem-entry-card-arrow" size={20} aria-hidden="true" />
            </MagicCard>
          </div>
        </BlurFade>

        <p className="totem-entry-hint">Toque em uma opção para continuar.</p>
      </div>

      <footer className="totem-entry-footer">
        <KioskClock />
      </footer>
    </main>
  )
}
