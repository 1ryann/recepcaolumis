import { type MouseEvent, type ReactNode } from 'react'
import { usePrefersReducedMotion } from './usePrefersReducedMotion'

// A real <button> with a material-style ripple on press. `disabled` blocks `onClick`
// entirely. Under `prefers-reduced-motion` the ripple is skipped but `onClick` still fires,
// so the control never loses function for motion-sensitive users.
export function RippleButton({
  onClick,
  disabled,
  className,
  children,
  'aria-label': ariaLabel,
}: {
  onClick: (event: MouseEvent<HTMLButtonElement>) => void
  disabled?: boolean
  className?: string
  children: ReactNode
  'aria-label'?: string
}) {
  const reduced = usePrefersReducedMotion()

  const spawnRipple = (event: MouseEvent<HTMLButtonElement>) => {
    const host = event.currentTarget
    if (!host || !host.isConnected) return
    const rect = host.getBoundingClientRect()
    const size = Math.max(rect.width, rect.height)
    const dot = document.createElement('span')
    dot.className = 'totem-magic-ripple-dot'
    dot.style.width = dot.style.height = `${size}px`
    dot.style.left = `${event.clientX - rect.left - size / 2}px`
    dot.style.top = `${event.clientY - rect.top - size / 2}px`
    dot.addEventListener('animationend', () => dot.remove())
    host.appendChild(dot)
  }

  const handleClick = (event: MouseEvent<HTMLButtonElement>) => {
    if (disabled) return
    if (!reduced) spawnRipple(event)
    onClick(event)
  }

  return (
    <button
      type="button"
      className={`totem-magic-ripple ${className ?? ''}`.trim()}
      disabled={disabled}
      aria-label={ariaLabel}
      onClick={handleClick}
    >
      {children}
    </button>
  )
}
