import { render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { LumisPageShell } from './LumisPageShell'

// The content layer paints above the z-index:0 backdrop by being positioned and later in the
// DOM. It must not carry a z-index of its own: that made it a stacking context and trapped
// the modal backdrop and the mobile nav underneath the fixed theme toggle.
test('renders the backdrop behind the content layer without trapping overlays', () => {
  render(<ThemeProvider><LumisPageShell><h1>Olá</h1></LumisPageShell></ThemeProvider>)
  expect(screen.getByRole('heading', { name: 'Olá' })).toBeInTheDocument()
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  expect(css).toMatch(/\.lumis-shell\s*\{[^}]*position:\s*relative/)
  expect(css).toMatch(/\.lumis-shell-content\s*\{[^}]*position:\s*relative/)
  expect(css).not.toMatch(/\.lumis-shell-content\s*\{[^}]*z-index/)
  expect(css).toMatch(/\.lumis-bg\s*\{[^}]*z-index:\s*0/)
  expect(css).not.toMatch(/\.lumis-shell(-content)?\s*\{[^}]*filter:/)
})

test('passes intensity through to the backdrop', () => {
  const { container } = render(<ThemeProvider><LumisPageShell intensity="muted">x</LumisPageShell></ThemeProvider>)
  expect(container.querySelector('.lumis-bg')?.getAttribute('data-intensity')).toBe('muted')
})
