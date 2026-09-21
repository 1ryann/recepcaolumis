import { Banknote, Tag, Users } from 'lucide-react'
import type { PublicRoomCardDto } from '../../api/modules'
import { formatBrlCompact } from './money'
import { capacityChip, categoryLabel } from './roomFeatures'

// The line of chips under a room's name on a catalogue card: who fits, what kind of room
// it is, and what it costs a month. A chip whose value is missing is not rendered — an
// undescribed room shows its name over its photo and nothing else, which is honest, while
// an empty chip or a "R$ 0,00" would be an advertisement for something untrue.
export function RoomChips({ room }: { room: PublicRoomCardDto }) {
  const capacity = capacityChip(room.capacityMin, room.capacityMax)
  const category = categoryLabel(room.category)
  const price = room.monthlyRate === null ? null : `${formatBrlCompact(room.monthlyRate)}/mês`

  // The row is rendered even with nothing in it. Its height is fixed at two lines in CSS,
  // which is what keeps every room name in a grid row at the same height — a card with
  // three chips, one that wraps to two lines and one with nothing all reserve the same
  // space. Returning null here left those names stepping up and down across the grid.
  return (
    <div className="room-chips">
      {capacity && (
        <span className="room-chip"><Users size={14} aria-hidden="true" />{capacity}</span>
      )}
      {category && (
        <span className="room-chip"><Tag size={14} aria-hidden="true" />{category}</span>
      )}
      {price && (
        <span className="room-chip room-chip-price"><Banknote size={14} aria-hidden="true" />{price}</span>
      )}
    </div>
  )
}
