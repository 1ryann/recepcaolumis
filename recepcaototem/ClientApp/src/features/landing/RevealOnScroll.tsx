import { useEffect, useRef, useState, type ReactNode } from 'react'

// Reveals a block the first time it enters the viewport, in the same visual language as
// the Totem's BlurFade (offset + blur + opacity) so the product feels like one piece. It
// reveals once and stays revealed: scrolling back up does not replay it. `delay` staggers
// siblings. Under prefers-reduced-motion the CSS drops the transform and blur, so the
// content simply appears. Without IntersectionObserver (jsdom, older browsers) everything
// is shown immediately — the content is never left invisible.
export function RevealOnScroll({ delay, as: Tag = 'div', className, children, ...rest }: {
  delay?: number
  as?: 'div' | 'section' | 'li' | 'article'
  className?: string
  children: ReactNode
} & Record<string, unknown>) {
  const ref = useRef<HTMLElement | null>(null)
  const [shown, setShown] = useState(() => typeof IntersectionObserver === 'undefined')

  useEffect(() => {
    if (shown || !ref.current) return
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        setShown(true)
        observer.disconnect()
      }
    }, { threshold: 0.15, rootMargin: '0px 0px -8% 0px' })
    observer.observe(ref.current)
    return () => observer.disconnect()
  }, [shown])

  return (
    <Tag
      ref={ref as never}
      className={`landing-reveal ${shown ? 'is-in' : ''} ${className ?? ''}`.replace(/\s+/g, ' ').trim()}
      style={{ transitionDelay: `${delay ?? 0}ms` }}
      {...rest}
    >
      {children}
    </Tag>
  )
}
