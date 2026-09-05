import { render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import { ModuleUnavailable } from './ModuleUnavailable'

test('renders a discrete unavailable state without operational data', () => {
  render(<ModuleUnavailable title="Visitas" />)
  expect(screen.getByRole('heading', { name: 'Visitas' })).toBeInTheDocument()
  expect(screen.getByText('Módulo ainda não disponível')).toBeInTheDocument()
  expect(screen.queryByText(/dados demonstrativos/i)).not.toBeInTheDocument()
})
