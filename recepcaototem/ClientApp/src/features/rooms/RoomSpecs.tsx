import { Bath, Ruler, Users, type LucideIcon } from 'lucide-react'
import type { PublicRoomDetailDto } from '../../api/modules'
import { areaLabel, capacityLabel } from './roomFeatures'

// The measurements of the room — how big, how many bathrooms, how many people — as the
// grid of icons in the detail page's sidebar card. Each entry names the attribute above
// its value, because "2" or "25m²" on its own leaves the reader to guess what was
// measured.
//
// Amenities deliberately do NOT appear here any more; they are the checklist under the
// description (RoomAmenityChecklist). The two used to share one flat list, which meant
// the sidebar grew without bound on a well-described room and the checklist zone of the
// layout had nothing to show.
//
// Each entry appears only when the room carries that value, so a room still waiting to be
// measured shows a shorter grid rather than a row of blanks, and the grid disappears
// entirely for a room with none of the three.
function bathroomValue(count: number | null): string | null {
  if (count === null) return null
  return count === 0 ? 'Nenhum' : String(count)
}

export function RoomSpecs({ room }: { room: PublicRoomDetailDto }) {
  const specs: { key: string; icon: LucideIcon; title: string; value: string }[] = []

  const area = areaLabel(room.areaSquareMeters)
  if (area) specs.push({ key: 'area', icon: Ruler, title: 'Área', value: area })

  const bathrooms = bathroomValue(room.bathroomCount)
  if (bathrooms) specs.push({ key: 'bathrooms', icon: Bath, title: 'Banheiros', value: bathrooms })

  const capacity = capacityLabel(room.capacityMin, room.capacityMax)
  if (capacity) specs.push({ key: 'capacity', icon: Users, title: 'Capacidade', value: capacity })

  if (specs.length === 0) return null

  return (
    <ul className="room-specs" data-testid="room-specs">
      {specs.map(({ key, icon: Icon, title, value }) => (
        <li className="room-spec" key={key}>
          <Icon size={22} aria-hidden="true" />
          <span className="room-spec-title">{title}</span>
          <span className="room-spec-value">{value}</span>
        </li>
      ))}
    </ul>
  )
}
