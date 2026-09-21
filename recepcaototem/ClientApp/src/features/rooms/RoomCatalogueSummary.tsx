import { CircleAlert } from 'lucide-react'
import type { RoomDto } from '../../api/modules'
import { amenityLabel, areaLabel, bathroomLabel, capacityLabel, categoryLabel } from './roomFeatures'

// What the public catalogue will show for this room, summarised on its admin card.
//
// These attributes are filled in one room at a time, over days, by someone walking the
// building with a tape measure and a camera. Without this the only way to see whether a
// room had been described was to open its form, so the list could not answer the question
// the work actually asks: which rooms are still missing something?
export function RoomCatalogueSummary({ room }: { room: RoomDto }) {
  const parts = [
    areaLabel(room.areaSquareMeters),
    capacityLabel(room.capacityMin, room.capacityMax),
    bathroomLabel(room.bathroomCount),
    categoryLabel(room.category),
    ...room.amenities.map(amenityLabel),
  ].filter((part): part is string => part !== null)

  if (parts.length === 0) {
    return (
      <p className="room-admin-undescribed">
        <CircleAlert size={14} aria-hidden="true" />
        Ainda sem dados de catálogo
      </p>
    )
  }

  return (
    <ul className="room-admin-summary">
      {parts.map(part => <li key={part}>{part}</li>)}
    </ul>
  )
}
