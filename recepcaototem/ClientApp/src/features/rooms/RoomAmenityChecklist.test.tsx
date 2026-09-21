import { render, screen, within } from '@testing-library/react'
import { expect, test } from 'vitest'
import { RoomAmenityChecklist } from './RoomAmenityChecklist'

// The ticked list under the room's description. Its counterpart is RoomSpecs, which holds
// the measurements; between them every feature the room carries appears exactly once.

test('every amenity the room carries is listed by name', () => {
  render(<RoomAmenityChecklist amenities={['WIFI', 'CLIMATIZADA', 'ACESSIVEL']} />)

  const list = within(screen.getByTestId('room-amenities'))
  expect(list.getByText('Wi-Fi')).toBeInTheDocument()
  expect(list.getByText('Climatizada')).toBeInTheDocument()
  expect(list.getByText('Acessível')).toBeInTheDocument()
})

test('a room with no amenities renders nothing rather than an empty list', () => {
  const { container } = render(<RoomAmenityChecklist amenities={[]} />)

  expect(screen.queryByTestId('room-amenities')).not.toBeInTheDocument()
  expect(container).toBeEmptyDOMElement()
})
