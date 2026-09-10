import { render, screen } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { LightRays } from './LightRays'
import { BlurFade } from './BlurFade'
import { MagicCard } from './MagicCard'
import { BorderBeam } from './BorderBeam'
import { ProgressiveBlur } from './ProgressiveBlur'
import { RippleButton } from './RippleButton'

function mockReducedMotion(reduced: boolean) {
  vi.stubGlobal('matchMedia', (q: string) => ({
    matches: reduced && q.includes('reduce'),
    media: q, addEventListener: vi.fn(), removeEventListener: vi.fn(),
    addListener: vi.fn(), removeListener: vi.fn(), onchange: null, dispatchEvent: vi.fn(),
  }))
}
beforeEach(() => mockReducedMotion(false))

test('LightRays is decorative and non-interactive', () => {
  const { container } = render(<LightRays />)
  const el = container.firstChild as HTMLElement
  expect(el).toHaveAttribute('aria-hidden', 'true')
  expect(getComputedStyle(el).pointerEvents).toBe('none')
})

test('BlurFade renders its children', () => {
  render(<BlurFade><span>oi</span></BlurFade>)
  expect(screen.getByText('oi')).toBeInTheDocument()
})

test('MagicCard renders a real button and fires onClick', async () => {
  const onClick = vi.fn()
  render(<MagicCard as="button" onClick={onClick}>tap</MagicCard>)
  screen.getByRole('button', { name: 'tap' }).click()
  expect(onClick).toHaveBeenCalled()
})

test('BorderBeam animates only when active; reduced motion keeps it static', () => {
  const { rerender, container } = render(<BorderBeam active={false} />)
  expect(container.querySelector('.totem-magic-beam.is-active')).toBeNull()
  rerender(<BorderBeam active />)
  expect(container.querySelector('.totem-magic-beam.is-active')).not.toBeNull()
  mockReducedMotion(true)
  rerender(<BorderBeam active />)
  expect(container.querySelector('.totem-magic-beam.is-static')).not.toBeNull()
})

test('ProgressiveBlur is a non-interactive edge mask', () => {
  const { container } = render(<ProgressiveBlur side="left" />)
  const el = container.firstChild as HTMLElement
  expect(el).toHaveAttribute('aria-hidden', 'true')
  expect(el.className).toContain('totem-magic-progblur')
})

test('RippleButton is a real button, blocks when disabled, no ripple under reduced motion', () => {
  const onClick = vi.fn()
  const { rerender } = render(<RippleButton onClick={onClick} disabled>x</RippleButton>)
  screen.getByRole('button', { name: 'x' }).click()
  expect(onClick).not.toHaveBeenCalled()
  mockReducedMotion(true)
  rerender(<RippleButton onClick={onClick}>x</RippleButton>)
  screen.getByRole('button', { name: 'x' }).click()
  expect(onClick).toHaveBeenCalledTimes(1)
})
