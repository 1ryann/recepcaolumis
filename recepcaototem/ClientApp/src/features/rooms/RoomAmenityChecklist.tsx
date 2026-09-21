import { Check } from 'lucide-react'
import type { RoomAmenity } from '../../api/modules'
import { amenityLabel } from './roomFeatures'

// The ticked list of comforts under the room's description. Split out of RoomSpecs, which
// now carries only the measurements: the detail page has two distinct places for a room's
// features — the sidebar card, where a handful of numbers must stay scannable, and this
// wider strip under the prose, where a long list costs nothing.
//
// A room with nothing ticked renders nothing at all, so the description panel does not end
// on an empty heading.
export function RoomAmenityChecklist({ amenities }: { amenities: RoomAmenity[] }) {
  if (amenities.length === 0) return null

  return (
    <ul className="room-amenities" data-testid="room-amenities">
      {amenities.map((amenity) => (
        <li className="room-amenity" key={amenity}>
          <Check size={16} aria-hidden="true" />
          <span>{amenityLabel(amenity)}</span>
        </li>
      ))}
    </ul>
  )
}
