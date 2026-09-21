import { render, screen, within } from '@testing-library/react'
import { expect, test } from 'vitest'
import { aPublicRoomDetail } from '../../test/roomFixtures'
import { RoomPrice } from './RoomPrice'

// The per-use rates used to be one joined string ("R$ 45,00/hora · R$ 280,00/dia"), which
// the redesigned sidebar cannot lay out: it shows them as two cells with a rule between.
// They are separate elements now, each pairing its amount with its unit.

test('the hourly and daily rates are separate entries, not one joined string', () => {
  render(<RoomPrice room={aPublicRoomDetail({ hourlyRate: 150, dailyRate: 800 })} />)

  const rates = within(screen.getByTestId('room-price-rates')).getAllByRole('listitem')
  expect(rates).toHaveLength(2)
  expect(rates[0]).toHaveTextContent(/R\$\s?150,00/)
  expect(rates[0]).toHaveTextContent('/hora')
  expect(rates[1]).toHaveTextContent(/R\$\s?800,00/)
  expect(rates[1]).toHaveTextContent('/dia')
})

test('a room let only by the hour shows that rate alone', () => {
  render(<RoomPrice room={aPublicRoomDetail({ hourlyRate: 150, dailyRate: null })} />)

  const rates = within(screen.getByTestId('room-price-rates')).getAllByRole('listitem')
  expect(rates).toHaveLength(1)
  expect(rates[0]).toHaveTextContent('/hora')
})

test('the monthly rate leads when the room carries one', () => {
  render(<RoomPrice room={aPublicRoomDetail({ monthlyRate: 3100, hourlyRate: 150, dailyRate: 800 })} />)

  expect(screen.getByTestId('room-price-main')).toHaveTextContent(/R\$\s?3\.100,00/)
  expect(screen.getByTestId('room-price-main')).toHaveTextContent('/mês')
})

test('a room with no monthly rate shows no monthly line at all', () => {
  render(<RoomPrice room={aPublicRoomDetail({ monthlyRate: null, hourlyRate: 150 })} />)

  expect(screen.queryByTestId('room-price-main')).not.toBeInTheDocument()
  expect(screen.queryByText('/mês')).not.toBeInTheDocument()
})

test('a room with no rates at all renders nothing', () => {
  const { container } = render(<RoomPrice room={aPublicRoomDetail({ monthlyRate: null, hourlyRate: null, dailyRate: null })} />)

  expect(container).toBeEmptyDOMElement()
})
