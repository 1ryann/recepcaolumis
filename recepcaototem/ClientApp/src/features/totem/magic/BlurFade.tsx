import { useEffect, useState, type ReactNode } from 'react'

// One-shot intro: children mount blurred/offset and settle into place once, on the first
// animation frame after mount. `delay` staggers entrances. Under `prefers-reduced-motion`
// the CSS zeroes the transform/blur so the content simply appears with no transition.
export function BlurFade({ delay, children }: { delay?: number; children: ReactNode }) {
  const [shown, setShown] = useState(false)
  useEffect(() => {
    const id = requestAnimationFrame(() => setShown(true))
    return () => cancelAnimationFrame(id)
  }, [])
  return (
    <div
      className={`totem-magic-fade ${shown ? 'is-in' : ''}`.trim()}
      style={{ transitionDelay: `${delay ?? 0}ms` }}
    >
      {children}
    </div>
  )
}
