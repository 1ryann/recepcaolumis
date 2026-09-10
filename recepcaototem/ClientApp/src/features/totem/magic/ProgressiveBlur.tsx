// A soft edge mask for carousels/scrollers: a stack of backdrop-blur layers faded out with
// a mask gradient so content dissolves toward the chosen edge. Non-interactive and hidden
// from assistive tech. All visual detail lives in `styles.css` (`.totem-magic-progblur`).
export function ProgressiveBlur({ side }: { side: 'left' | 'right' }) {
  return (
    <div
      aria-hidden="true"
      className={`totem-magic-progblur totem-magic-progblur--${side}`}
      style={{ pointerEvents: 'none' }}
    />
  )
}
