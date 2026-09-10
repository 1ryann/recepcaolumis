import { useSyncExternalStore } from 'react'

// Single source of truth for motion preference across the local Magic UI components.
// Backed by `useSyncExternalStore` so the value is read fresh on every render (a mid-session
// OS change fires "change"; a programmatic override is picked up on the next render) and is
// SSR-safe via the `() => false` server snapshot.
const QUERY = '(prefers-reduced-motion: reduce)'

function subscribe(onChange: () => void): () => void {
  if (typeof matchMedia !== 'function') return () => {}
  const mq = matchMedia(QUERY)
  mq.addEventListener?.('change', onChange)
  return () => mq.removeEventListener?.('change', onChange)
}

function getSnapshot(): boolean {
  return typeof matchMedia === 'function' && matchMedia(QUERY).matches
}

export function usePrefersReducedMotion(): boolean {
  return useSyncExternalStore(subscribe, getSnapshot, () => false)
}
