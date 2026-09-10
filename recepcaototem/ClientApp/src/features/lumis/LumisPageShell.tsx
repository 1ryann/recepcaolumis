import type { ReactNode } from 'react'
import { LumisBackground } from './LumisBackground'

// One implementation of the LUMIS identity backdrop for a full page/shell. The content layer is
// z-raised and never carries a filter, so nested scroll/touch/carousel/camera areas keep working.
export function LumisPageShell({
  intensity, className, children,
}: { intensity?: 'default' | 'muted'; className?: string; children: ReactNode }) {
  return (
    <div className={`lumis-shell ${className ?? ''}`.trim()}>
      <LumisBackground intensity={intensity} />
      <div className="lumis-shell-content">{children}</div>
    </div>
  )
}
