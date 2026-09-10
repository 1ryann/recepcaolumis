import { usePrefersReducedMotion } from './usePrefersReducedMotion'

// A travelling highlight around an element's border, drawn with a conic gradient in the
// strict #FFFFFF / #888888 / #3D3D3D palette. The beam only exists while `active`; when
// `active` and the user prefers reduced motion it collapses to a plain static 1px border
// (`is-static`) with no animation.
export function BorderBeam({ active }: { active: boolean }) {
  const reduced = usePrefersReducedMotion()
  const classes = ['totem-magic-beam']
  if (active) classes.push('is-active')
  if (active && reduced) classes.push('is-static')
  return <span aria-hidden="true" className={classes.join(' ')} />
}
