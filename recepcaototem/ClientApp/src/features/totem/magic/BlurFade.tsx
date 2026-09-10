import { useEffect, useState, type ReactNode } from 'react'

// One-shot intro: children mount blurred/offset and settle into place once, on the first
// animation frame after mount. `delay` staggers entrances. Under `prefers-reduced-motion`
// the CSS zeroes the transform/blur so the content simply appears with no transition.
// Once the entry transition ends we flag `is-settled` so the CSS can drop the persistent
// filter/will-change (compositing hint) that the animated state needs.
export function BlurFade({ delay, children }: { delay?: number; children: ReactNode }) {
  const [shown, setShown] = useState(false)
  const [settled, setSettled] = useState(false)
  useEffect(() => {
    const id = requestAnimationFrame(() => setShown(true))
    return () => cancelAnimationFrame(id)
  }, [])
  return (
    <div
      className={`totem-magic-fade ${shown ? 'is-in' : ''} ${settled ? 'is-settled' : ''}`.replace(/\s+/g, ' ').trim()}
      style={{ transitionDelay: `${delay ?? 0}ms` }}
      onTransitionEnd={() => setSettled(true)}
    >
      {children}
    </div>
  )
}
