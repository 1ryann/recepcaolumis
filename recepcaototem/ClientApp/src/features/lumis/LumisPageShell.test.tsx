import { render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { LumisPageShell } from './LumisPageShell'

test('renders the backdrop behind a z-raised content layer', () => {
  render(<ThemeProvider><LumisPageShell><h1>Olá</h1></LumisPageShell></ThemeProvider>)
  expect(screen.getByRole('heading', { name: 'Olá' })).toBeInTheDocument()
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  expect(css).toMatch(/\.lumis-shell\s*\{[^}]*position:\s*relative/)
  expect(css).toMatch(/\.lumis-shell-content\s*\{[^}]*z-index:\s*1/)
  expect(css).not.toMatch(/\.lumis-shell(-content)?\s*\{[^}]*filter:/)
})

test('passes intensity through to the backdrop', () => {
  const { container } = render(<ThemeProvider><LumisPageShell intensity="muted">x</LumisPageShell></ThemeProvider>)
  expect(container.querySelector('.lumis-bg')?.getAttribute('data-intensity')).toBe('muted')
})
