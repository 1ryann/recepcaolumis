import { Bath, Ruler, Users } from 'lucide-react'
import type { PublicRoomDetailDto } from '../../api/modules'
import { amenityIcon, amenityLabel, areaLabel, bathroomLabel, capacityLabel } from './roomFeatures'

// The icon strip on the room detail page: size, bathrooms, how many people fit, then the
// comforts. Each entry appears only when the room actually carries that value, so a room
// still waiting to be measured shows a shorter strip rather than a row of blanks, and the
// strip disappears entirely for a room with nothing filled in.
export function RoomSpecs({ room }: { room: PublicRoomDetailDto }) {
  const specs: { key: string; icon: typeof Ruler; label: string }[] = []

  const area = areaLabel(room.areaSquareMeters)
  if (area) specs.push({ key: 'area', icon: Ruler, label: area })

  const bathrooms = bathroomLabel(room.bathroomCount)
  if (bathrooms) specs.push({ key: 'bathrooms', icon: Bath, label: bathrooms })

  const capacity = capacityLabel(room.capacityMin, room.capacityMax)
  if (capacity) specs.push({ key: 'capacity', icon: Users, label: capacity })

  for (const amenity of room.amenities) {
    specs.push({ key: amenity, icon: amenityIcon(amenity), label: amenityLabel(amenity) })
  }

  if (specs.length === 0) return null

  return (
    <ul className="room-specs" data-testid="room-specs">
      {specs.map(({ key, icon: Icon, label }) => (
        <li className="room-spec" key={key}>
          <Icon size={22} aria-hidden="true" />
          <span>{label}</span>
        </li>
      ))}
    </ul>
  )
}
