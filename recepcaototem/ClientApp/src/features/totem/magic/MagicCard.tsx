import { useRef, type PointerEvent, type ReactNode } from 'react'
import { usePrefersReducedMotion } from './usePrefersReducedMotion'

// A real <button> with a faint radial highlight that follows the pointer. The highlight is
// pure decoration: it is only wired on fine-pointer, hover-capable devices and never under
// reduced motion, and the button's function never depends on hover.
export function MagicCard({
  as,
  onClick,
  className,
  children,
}: {
  as?: 'button'
  onClick?: () => void
  className?: string
  children: ReactNode
}) {
  const ref = useRef<HTMLButtonElement>(null)
  const reduced = usePrefersReducedMotion()

  const canFollow =
    !reduced &&
    typeof matchMedia === 'function' &&
    matchMedia('(hover: hover) and (pointer: fine)').matches

  const onPointerMove = (event: PointerEvent<HTMLButtonElement>) => {
    if (!canFollow) return
    const node = ref.current
    if (!node) return
    const rect = node.getBoundingClientRect()
    node.style.setProperty('--mx', `${event.clientX - rect.left}px`)
    node.style.setProperty('--my', `${event.clientY - rect.top}px`)
  }

  // `as` is part of the shared interface; only the button form is used by the Totem screens.
  void as
  return (
    <button
      ref={ref}
      type="button"
      className={`totem-magic-card ${className ?? ''}`.trim()}
      onClick={onClick}
      onPointerMove={onPointerMove}
    >
      {children}
    </button>
  )
}
