// Decorative diagonal light streaks for the Totem backdrop. Purely visual: it takes no
// pointer events and is hidden from assistive tech. The animation lives in `styles.css`
// (`.totem-magic-rays`) and is disabled under `prefers-reduced-motion`. `pointerEvents`
// is also set inline so the non-interactive guarantee holds even where the app stylesheet
// is not loaded (e.g. jsdom in unit tests).
export function LightRays({ className }: { className?: string }) {
  return (
    <div
      className={`totem-magic-rays ${className ?? ''}`.trim()}
      aria-hidden="true"
      style={{ pointerEvents: 'none' }}
    />
  )
}
