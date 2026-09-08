import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { describe, expect, test, vi } from 'vitest'
import type { ProfessionalAvailabilityDto } from '../../api/modules'
import { AvailabilityEditor, ExceptionsEditor } from './AvailabilityEditor'

const base = {
  mode: 'INHERIT_GLOBAL' as const,
  days: [
    { dayOfWeek: 'MONDAY', intervals: [{ startTime: '09:00', endTime: '12:00' }] },
    { dayOfWeek: 'TUESDAY', intervals: [] }, { dayOfWeek: 'WEDNESDAY', intervals: [] },
    { dayOfWeek: 'THURSDAY', intervals: [] }, { dayOfWeek: 'FRIDAY', intervals: [] },
    { dayOfWeek: 'SATURDAY', intervals: [] }, { dayOfWeek: 'SUNDAY', intervals: [] },
  ],
  effectiveDays: [
    { dayOfWeek: 'MONDAY', intervals: [{ startTime: '08:00', endTime: '18:00' }] },
  ],
  concurrencyToken: 'v1',
  existingReservationsOutsideAvailabilityCount: 2,
}

test('inherit mode explains the source schedule and preserves stored custom intervals', () => {
  render(<AvailabilityEditor value={base} onChange={vi.fn()} onSave={vi.fn()} pending={false} />)
  expect(screen.getByText(/horário de funcionamento do estabelecimento/i)).toBeInTheDocument()
  expect(screen.getByDisplayValue('09:00')).toBeInTheDocument()
  expect(screen.getByText(/2 agendamento/)).toBeInTheDocument()
  expect(screen.getByRole('radio', { name: /horário personalizado/i })).toBeInTheDocument()
})

test('custom mode allows adding and removing intervals while rejecting an inverted range', () => {
  function Harness() {
    const [value, setValue] = useState<ProfessionalAvailabilityDto>({ ...base, mode: 'CUSTOM' })
    return <AvailabilityEditor value={value} onChange={(next) => setValue((current) => ({ ...current, ...next }))} onSave={vi.fn()} pending={false} />
  }
  render(<Harness />)
  fireEvent.click(screen.getAllByRole('button', { name: /adicionar intervalo/i })[0])
  fireEvent.change(screen.getAllByLabelText('Início')[0], { target: { value: '13:00' } })
  fireEvent.change(screen.getAllByLabelText('Fim')[0], { target: { value: '12:00' } })
  expect(screen.getAllByText(/início deve ser anterior ao fim/i).length).toBeGreaterThan(0)
})

test('exceptions editor supports all-day and partial entries and keeps row token on edit', () => {
  const onCreate = vi.fn().mockResolvedValue(undefined)
  const onUpdate = vi.fn().mockResolvedValue(undefined)
  const onDelete = vi.fn().mockResolvedValue(undefined)
  render(<ExceptionsEditor exceptions={[{ id: 'e1', date: '2026-09-15', allDay: true, startTime: null, endTime: null, reason: 'Compromisso', createdAt: '', updatedAt: '', concurrencyToken: 'e1-v' }]} onCreate={onCreate} onUpdate={onUpdate} onDelete={onDelete} pending={false} />)
  expect(screen.getByText(/dia inteiro/i)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /adicionar indisponibilidade/i }))
  fireEvent.change(screen.getByLabelText('Data'), { target: { value: '2026-09-22' } })
  fireEvent.click(screen.getByRole('radio', { name: /horário específico/i }))
  fireEvent.change(screen.getByLabelText('Início'), { target: { value: '14:00' } })
  fireEvent.change(screen.getByLabelText('Fim'), { target: { value: '16:00' } })
  fireEvent.click(screen.getByRole('button', { name: /^salvar$/i }))
  expect(onCreate).toHaveBeenCalledWith(expect.objectContaining({ date: '2026-09-22', allDay: false, startTime: '14:00', endTime: '16:00', reason: '' }))
})
