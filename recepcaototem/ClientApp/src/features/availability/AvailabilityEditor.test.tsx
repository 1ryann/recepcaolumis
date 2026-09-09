import { fireEvent, render, screen, within } from '@testing-library/react'
import { useState } from 'react'
import { expect, test, vi } from 'vitest'
import type { ProfessionalAvailabilityDto } from '../../api/modules'
import { AvailabilityEditor, ExceptionsEditor } from './AvailabilityEditor'

const buildingDays = ['MONDAY', 'TUESDAY', 'WEDNESDAY', 'THURSDAY', 'FRIDAY', 'SATURDAY', 'SUNDAY']
  .map((dayOfWeek) => ({ dayOfWeek, intervals: [{ startTime: '08:00', endTime: '18:00' }] }))

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
  globalDays: buildingDays,
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

test('custom mode allows adding periods while rejecting an inverted range', () => {
  function Harness() {
    const [value, setValue] = useState<ProfessionalAvailabilityDto>({ ...base, mode: 'CUSTOM' })
    return <AvailabilityEditor value={value} onChange={(next) => setValue((current) => ({ ...current, ...next }))} onSave={vi.fn()} pending={false} />
  }
  render(<Harness />)
  fireEvent.click(screen.getAllByRole('button', { name: /adicionar período/i })[0])
  fireEvent.change(screen.getAllByLabelText('Início')[0], { target: { value: '13:00' } })
  fireEvent.change(screen.getAllByLabelText('Fim')[0], { target: { value: '12:00' } })
  expect(screen.getAllByText(/início deve ser anterior ao fim/i).length).toBeGreaterThan(0)
})

test('custom mode shows the real establishment window on each day, from globalDays not effectiveDays', () => {
  // effectiveDays here is the professional's clamped custom result — deliberately different
  // from the establishment hours, to prove the hint reads globalDays.
  const value = {
    ...base,
    mode: 'CUSTOM' as const,
    effectiveDays: [{ dayOfWeek: 'MONDAY', intervals: [{ startTime: '09:00', endTime: '12:00' }] }],
    globalDays: buildingDays,
  }
  render(<AvailabilityEditor value={value} onChange={vi.fn()} onSave={vi.fn()} pending={false} />)
  // every day shows the building hours, including days with no custom period configured
  expect(within(screen.getByTestId('wpe-day-MONDAY')).getByText('Estabelecimento: 08:00–18:00')).toBeInTheDocument()
  expect(within(screen.getByTestId('wpe-day-TUESDAY')).getByText('Estabelecimento: 08:00–18:00')).toBeInTheDocument()
  // it must NOT reflect the professional's own (effective) schedule
  expect(within(screen.getByTestId('wpe-day-MONDAY')).queryByText('Estabelecimento: 09:00–12:00')).toBeNull()
})

test('custom mode reports a closed day when the building itself is closed', () => {
  const value = {
    ...base,
    mode: 'CUSTOM' as const,
    globalDays: buildingDays.map((day) => (day.dayOfWeek === 'SUNDAY' ? { dayOfWeek: 'SUNDAY', intervals: [] } : day)),
  }
  render(<AvailabilityEditor value={value} onChange={vi.fn()} onSave={vi.fn()} pending={false} />)
  expect(within(screen.getByTestId('wpe-day-SUNDAY')).getByText('Estabelecimento: sem atendimento')).toBeInTheDocument()
  expect(within(screen.getByTestId('wpe-day-MONDAY')).getByText('Estabelecimento: 08:00–18:00')).toBeInTheDocument()
})

test('custom periods edited locally are not lost on a re-render with the same record', () => {
  function Harness() {
    const [value, setValue] = useState<ProfessionalAvailabilityDto>({ ...base, mode: 'CUSTOM' })
    return <AvailabilityEditor value={value} draft={{ mode: 'CUSTOM', days: value.days }}
      onChange={(next) => setValue((current) => ({ ...current, days: next.days }))} onSave={vi.fn()} pending={false} />
  }
  render(<Harness />)
  fireEvent.change(screen.getAllByLabelText('Início')[0], { target: { value: '10:15' } })
  expect(screen.getAllByLabelText('Início')[0]).toHaveValue('10:15')
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
