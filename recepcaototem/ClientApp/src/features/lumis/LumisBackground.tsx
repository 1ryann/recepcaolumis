// The single LUMIS animated backdrop: slow diagonal light rays over #181818. Purely decorative —
// no pointer events, hidden from assistive tech. All motion lives in styles.css (.lumis-bg) and is
// disabled under prefers-reduced-motion. `intensity="muted"` dims it behind dense screens.
export function LumisBackground({ intensity = 'default' }: { intensity?: 'default' | 'muted' }) {
  return (
    <div className="lumis-bg" data-intensity={intensity} aria-hidden="true" style={{ pointerEvents: 'none' }}>
      <span className="lumis-bg-ray" />
      <span className="lumis-bg-ray" />
      <span className="lumis-bg-ray" />
    </div>
  )
}
