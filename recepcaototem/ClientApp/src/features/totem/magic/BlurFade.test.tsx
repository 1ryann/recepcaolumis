import { act, render } from '@testing-library/react'
import { expect, test } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { BlurFade } from './BlurFade'

test('after the entry transition ends the fade layer settles (no persistent filter/will-change)', () => {
  const { container } = render(<BlurFade>hi</BlurFade>)
  const el = container.querySelector('.totem-magic-fade') as HTMLElement
  act(() => { el.dispatchEvent(new Event('transitionend', { bubbles: true })) })
  expect(el.className).toMatch(/\bis-settled\b/)
})

test('CSS clears filter and will-change on the settled fade layer', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  expect(css).toMatch(/\.totem-magic-fade\.is-in\.is-settled\s*\{[^}]*filter:\s*none[^}]*will-change:\s*auto/)
})

test('reduced-motion rule for .totem-magic-fade clears BOTH filter and will-change', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  // Walk the prefers-reduced-motion media block rule-by-rule (no unbounded [\s\S]*)
  // to isolate the .totem-magic-fade declaration body within it.
  const match = css.match(
    /@media \(prefers-reduced-motion: reduce\) \{(?:[^{}]*\{[^{}]*\})*?\s*\.totem-magic-fade\s*\{([^}]*)\}/,
  )
  expect(match).not.toBeNull()
  const body = match![1]
  expect(body).toMatch(/filter:\s*none/)
  expect(body).toMatch(/will-change:\s*auto/)
})
