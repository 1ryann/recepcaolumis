import { render } from '@testing-library/react'
import { expect, test } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { LumisBackground } from './LumisBackground'

test('is decorative: aria-hidden + pointer-events none + three rays', () => {
  const { container } = render(<LumisBackground />)
  const bg = container.querySelector('.lumis-bg') as HTMLElement
  expect(bg).not.toBeNull()
  expect(bg.getAttribute('aria-hidden')).toBe('true')
  expect(bg.style.pointerEvents).toBe('none')
  expect(bg.querySelectorAll('.lumis-bg-ray')).toHaveLength(3)
})

test('muted intensity is exposed as a data attribute', () => {
  const { container } = render(<LumisBackground intensity="muted" />)
  expect(container.querySelector('.lumis-bg')?.getAttribute('data-intensity')).toBe('muted')
})

test('CSS animates only transform/opacity and disables under reduced motion', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  const block = css.slice(css.indexOf('.lumis-bg'))
  expect(block).toMatch(/@keyframes lumis-ray-drift/)
  expect(block).not.toMatch(/\.lumis-bg-ray\s*\{[^}]*filter:/)          // no filter on the animated element
  expect(block).toMatch(/prefers-reduced-motion: reduce[\s\S]*\.lumis-bg-ray\s*\{[^}]*animation:\s*none/)
})
