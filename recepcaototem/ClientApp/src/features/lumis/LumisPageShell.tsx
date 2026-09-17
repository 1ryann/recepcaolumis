import type { ReactNode } from 'react'
import { LumisBackground } from './LumisBackground'
import { ThemeToggle } from '../../theme/ThemeToggle'

// One implementation of the LUMIS identity backdrop for a full page/shell. The content layer is
// z-raised and never carries a filter, so nested scroll/touch/carousel/camera areas keep working.
// The single ThemeToggle instance lives here too, so every LumisPageShell consumer (Admin,
// Customer, Professional, Login, registration pages, Reception) gets it for free — see
// theme/ThemeProvider.tsx and styles.css's .theme-toggle rule.
export function LumisPageShell({
  intensity, className, children,
}: { intensity?: 'default' | 'muted'; className?: string; children: ReactNode }) {
  return (
    <div className={`lumis-shell ${className ?? ''}`.trim()}>
      <LumisBackground intensity={intensity} />
      <ThemeToggle />
      <div className="lumis-shell-content">{children}</div>
    </div>
  )
}
