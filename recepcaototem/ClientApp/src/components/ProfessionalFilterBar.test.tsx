import { render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import { ProfessionalFilterBar } from './ProfessionalFilterBar'

test('renders children inside the professional-filter-bar wrapper', () => {
  render(<ProfessionalFilterBar><button>Filtro</button></ProfessionalFilterBar>)
  const button = screen.getByRole('button', { name: 'Filtro' })
  expect(button.parentElement?.className).toBe('professional-filter-bar')
})
