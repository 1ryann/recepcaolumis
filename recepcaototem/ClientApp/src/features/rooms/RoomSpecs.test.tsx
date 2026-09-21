import { render, screen, within } from '@testing-library/react'
import { expect, test } from 'vitest'
import { aPublicRoomDetail } from '../../test/roomFixtures'
import { RoomSpecs } from './RoomSpecs'

// The strip used to mix the measurements with the amenities in one flat list. The detail
// page now has two places for them — the measurements in the sidebar card, the amenities
// in the checklist under the description — so this component keeps only the former, and
// each entry carries the attribute's name as well as its value ("Capacidade" / "4 a 8
// pessoas") rather than a bare value the reader has to name for themselves.

test('each measurement shows the attribute name above its value', () => {
  render(<RoomSpecs room={aPublicRoomDetail({ areaSquareMeters: 25, bathroomCount: 2, capacityMin: 4, capacityMax: 8 })} />)

  const specs = within(screen.getByTestId('room-specs'))
  expect(specs.getByText('Área')).toBeInTheDocument()
  expect(specs.getByText('25m²')).toBeInTheDocument()
  expect(specs.getByText('Capacidade')).toBeInTheDocument()
  expect(specs.getByText('4 a 8 pessoas')).toBeInTheDocument()
})

test('a room with no bathroom says so rather than showing a bare zero', () => {
  render(<RoomSpecs room={aPublicRoomDetail({ bathroomCount: 0 })} />)

  const specs = within(screen.getByTestId('room-specs'))
  expect(specs.getByText('Banheiros')).toBeInTheDocument()
  expect(specs.getByText('Nenhum')).toBeInTheDocument()
})

test('amenities stay out of the strip — they belong to the checklist', () => {
  render(<RoomSpecs room={aPublicRoomDetail({ capacityMin: 4, amenities: ['WIFI', 'CLIMATIZADA'] })} />)

  expect(screen.queryByText('Wi-Fi')).not.toBeInTheDocument()
  expect(screen.queryByText('Climatizada')).not.toBeInTheDocument()
})

test('a room with no measurements renders no strip at all', () => {
  render(<RoomSpecs room={aPublicRoomDetail({ amenities: ['WIFI'] })} />)

  expect(screen.queryByTestId('room-specs')).not.toBeInTheDocument()
})
