import { fireEvent, render, screen } from '@testing-library/react'
import { expect, test, vi } from 'vitest'
import { RoomForm } from './RoomForm'
import { aRoomDto } from '../../test/roomFixtures'

const noop = () => {}

test('editing a room shows every stored attribute, with its amenities already ticked', () => {
  const room = aRoomDto({
    name: 'Sala Premium', hourlyRate: 45, dailyRate: 280,
    monthlyRate: 3100, areaSquareMeters: 25, bathroomCount: 1,
    capacityMin: 4, capacityMax: 8, category: 'CONSULTORIO',
    amenities: ['CLIMATIZADA', 'MOBILIADA', 'WIFI'],
  })
  render(<RoomForm room={room} pending={false} onCancel={noop} onSubmit={async () => {}} />)

  expect(screen.getByLabelText('Valor mensal')).toHaveValue('3100')
  expect(screen.getByLabelText('Área (m²)')).toHaveValue('25')
  expect(screen.getByLabelText('Banheiros')).toHaveValue('1')
  expect(screen.getByLabelText('Capacidade mínima')).toHaveValue('4')
  expect(screen.getByLabelText('Capacidade máxima')).toHaveValue('8')
  expect(screen.getByLabelText('Categoria')).toHaveValue('CONSULTORIO')

  expect(screen.getByLabelText('Climatizada')).toBeChecked()
  expect(screen.getByLabelText('Mobiliada')).toBeChecked()
  expect(screen.getByLabelText('Wi-Fi')).toBeChecked()
  expect(screen.getByLabelText('Janela')).not.toBeChecked()
})

test('a blank optional field is submitted as null, never as zero', async () => {
  const onSubmit = vi.fn(async () => {})
  render(<RoomForm room={null} pending={false} onCancel={noop} onSubmit={onSubmit} />)

  fireEvent.change(screen.getByLabelText('Nome da sala'), { target: { value: 'Sala nova' } })
  fireEvent.change(screen.getByLabelText('Tarifa por hora'), { target: { value: '10' } })
  fireEvent.change(screen.getByLabelText('Tarifa diária'), { target: { value: '50' } })
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar sala' }))

  expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
    monthlyRate: null, areaSquareMeters: null, bathroomCount: null,
    capacityMin: null, capacityMax: null, category: null, amenities: [],
  }))
})

test('ticking amenities and filling the catalogue fields submits them', async () => {
  const onSubmit = vi.fn(async () => {})
  render(<RoomForm room={null} pending={false} onCancel={noop} onSubmit={onSubmit} />)

  fireEvent.change(screen.getByLabelText('Nome da sala'), { target: { value: 'Sala nova' } })
  fireEvent.change(screen.getByLabelText('Tarifa por hora'), { target: { value: '10' } })
  fireEvent.change(screen.getByLabelText('Tarifa diária'), { target: { value: '50' } })
  fireEvent.change(screen.getByLabelText('Valor mensal'), { target: { value: '3.100,00' } })
  fireEvent.change(screen.getByLabelText('Área (m²)'), { target: { value: '25,5' } })
  fireEvent.change(screen.getByLabelText('Capacidade mínima'), { target: { value: '4' } })
  fireEvent.change(screen.getByLabelText('Capacidade máxima'), { target: { value: '8' } })
  fireEvent.change(screen.getByLabelText('Categoria'), { target: { value: 'REUNIAO' } })
  fireEvent.click(screen.getByLabelText('Mobiliada'))
  fireEvent.click(screen.getByLabelText('Wi-Fi'))
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar sala' }))

  expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
    monthlyRate: 3100, areaSquareMeters: 25.5, capacityMin: 4, capacityMax: 8,
    category: 'REUNIAO', amenities: ['MOBILIADA', 'WIFI'],
  }))
})

test('a maximum capacity without a minimum is refused before reaching the server', () => {
  const onSubmit = vi.fn(async () => {})
  render(<RoomForm room={null} pending={false} onCancel={noop} onSubmit={onSubmit} />)

  fireEvent.change(screen.getByLabelText('Nome da sala'), { target: { value: 'Sala nova' } })
  fireEvent.change(screen.getByLabelText('Tarifa por hora'), { target: { value: '10' } })
  fireEvent.change(screen.getByLabelText('Tarifa diária'), { target: { value: '50' } })
  fireEvent.change(screen.getByLabelText('Capacidade máxima'), { target: { value: '8' } })
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar sala' }))

  expect(screen.getByRole('alert')).toHaveTextContent(/capacidade mínima antes da máxima/i)
  expect(onSubmit).not.toHaveBeenCalled()
})
