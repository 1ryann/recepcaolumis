import { useTheme } from './ThemeProvider'

type LumisLogoProps = {
  className?: string
  width?: number
  height?: number
  alt?: string
}

// Two LUMIS wordmark assets exist:
// - `lumis-logo-transparent.png`: a WHITE wordmark on transparency. Correct over dark
//   backgrounds.
// - `lumis-logo-dark.png`: near-black ink (~rgb(45,45,45)) on a TRANSPARENT background —
//   structurally the same transparent PNG as `lumis-logo-transparent.png`, just with dark
//   ink instead of light ink (verified: every corner sampled has alpha 0). There is no opaque
//   white background here. `mix-blend-mode: multiply` (applied via the shared `.lumis-logo`
//   class in styles.css) is kept anyway: multiplying near-black ink against the near-white
//   light-mode surfaces it's used on is visually indistinguishable from normal compositing,
//   transparent pixels are unaffected by blend mode either way, and it costs nothing to leave
//   in place — so there's no behavior change from switching to `normal`, just less certainty.
//
// This component centralizes that swap so no call site hardcodes a `src`/blend-mode pair.
// `.sidebar-logo` (AdminLayout) predates this component and has its own per-theme blend
// handling (multiply/screen) tailored to the dark sidebar rail — it is intentionally not
// using this component.
export function LumisLogo({ className, width, height, alt = 'LUMIS' }: LumisLogoProps) {
  const { theme } = useTheme()
  const src = theme === 'dark' ? '/lumis-logo-transparent.png' : '/lumis-logo-dark.png'
  const classes = className ? `lumis-logo ${className}` : 'lumis-logo'
  return <img className={classes} src={src} alt={alt} width={width} height={height} />
}
