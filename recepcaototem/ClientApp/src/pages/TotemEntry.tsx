import { ArrowRight, BriefcaseBusiness, QrCode, UserRound } from 'lucide-react'
import type { CSSProperties } from 'react'
import { useNavigate } from 'react-router-dom'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { KioskClock } from '../features/totem/KioskClock'
import { BlurFade } from '../features/totem/magic/BlurFade'
import { MagicCard } from '../features/totem/magic/MagicCard'
import { LumisLogo } from '../theme/LumisLogo'

// The `/totem` decision screen. It asks one question — "Como deseja continuar?" — and
// routes to the code-based check-in, the professional carousel, or (third option, full
// width below the other two) the public room catalog at `/totem/salas`. No QR field and no code
// input live here; those belong to `/totem/check-in`. The two options are real <button>
// elements (via MagicCard) so they are reachable by keyboard and by role/name queries.
// Inline because the entry-screen rules live in styles.css. Width = one grid column: 50% minus
// half of `.totem-entry-options`' gap (clamp(14px, 2vw, 22px)). Below 621px viewport width —
// where styles.css collapses the grid to one column (max-width: 620px) — the second term becomes
// huge, so min() yields 100% and the card matches the stacked cards.
const RENT_CARD_STYLE: CSSProperties = {
  gridColumn: '1 / -1',
  justifySelf: 'center',
  width: 'min(100%, max(calc(50% - clamp(7px, 1vw, 11px)), calc((621px - 100vw) * 1000)))',
}

export function TotemEntry() {
  const navigate = useNavigate()

  return (
    <main className="totem-entry">
      <LumisBackground />
      <div className="totem-entry-inner">
        <BlurFade>
          <LumisLogo
            className="totem-entry-logo"
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

            {/* Own row, centred under both options, as wide as one of them. */}
            <MagicCard
              as="button"
              className="totem-entry-card"
              style={RENT_CARD_STYLE}
              onClick={() => navigate('/totem/salas')}
            >
              <span className="totem-entry-card-icon" aria-hidden="true"><BriefcaseBusiness size={22} /></span>
              <span className="totem-entry-card-label">Alugar sala</span>
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
