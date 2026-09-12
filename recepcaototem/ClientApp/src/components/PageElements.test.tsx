import { render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import { StatusBadge } from './PageElements'

test('StatusBadge renders the given tone class and label text', () => {
  render(<StatusBadge tone="waiting" label="Aguardando" />)
  const badge = screen.getByText('Aguardando')
  expect(badge.className).toContain('status-badge')
  expect(badge.className).toContain('status-waiting')
})

test('StatusBadge keeps supporting the legacy status prop for existing callers', () => {
  render(<StatusBadge status="active" />)
  const badge = screen.getByText('Ativo')
  expect(badge.className).toContain('status-badge')
  expect(badge.className).toContain('status-active')
})
