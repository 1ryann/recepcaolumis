import type { PublicRoomDetailDto } from '../../api/modules'
import { formatBrl } from './money'

// The price block on the detail page: the monthly figure in the lead, with the hourly and
// daily rates beneath it in smaller type, because a room is let by contract and taken by
// the hour. Anything the room does not carry is left out — the backend already reports a
// rate of zero as absent, so "R$ 0,00/hora" never reaches here.
export function RoomPrice({ room }: { room: PublicRoomDetailDto }) {
  const perUse = [
    room.hourlyRate === null ? null : `${formatBrl(room.hourlyRate)}/hora`,
    room.dailyRate === null ? null : `${formatBrl(room.dailyRate)}/dia`,
  ].filter((entry): entry is string => entry !== null)

  if (room.monthlyRate === null && perUse.length === 0) return null

  return (
    <div className="room-price" data-testid="room-price">
      {room.monthlyRate !== null && (
        <p className="room-price-main">
          {formatBrl(room.monthlyRate)}
          <span className="room-price-unit"> /mês</span>
        </p>
      )}
      {perUse.length > 0 && (
        <p className="room-price-secondary">{perUse.join(' · ')}</p>
      )}
    </div>
  )
}
