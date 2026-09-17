import { fireEvent, render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import { ThemeProvider } from './ThemeProvider'
import { ThemeToggle } from './ThemeToggle'

function renderToggle() {
  return render(<ThemeProvider><ThemeToggle /></ThemeProvider>)
}

test('is a real button, keyboard accessible, that flips the theme on click with no page reload', () => {
  renderToggle()
  const button = screen.getByRole('button')
  expect(button.tagName).toBe('BUTTON')
  const before = document.documentElement.dataset.theme
  fireEvent.click(button)
  expect(document.documentElement.dataset.theme).not.toBe(before)
  fireEvent.click(button)
  expect(document.documentElement.dataset.theme).toBe(before)
})

test('the aria-label reflects the current theme and the action the click performs', () => {
  renderToggle()
  const button = screen.getByRole('button')
  const themeBefore = document.documentElement.dataset.theme
  const expectedBefore = themeBefore === 'dark' ? 'Ativar modo claro' : 'Ativar modo escuro'
  expect(button).toHaveAttribute('aria-label', expectedBefore)
  fireEvent.click(button)
  const themeAfter = document.documentElement.dataset.theme
  const expectedAfter = themeAfter === 'dark' ? 'Ativar modo claro' : 'Ativar modo escuro'
  expect(expectedAfter).not.toBe(expectedBefore)
  expect(button).toHaveAttribute('aria-label', expectedAfter)
})
