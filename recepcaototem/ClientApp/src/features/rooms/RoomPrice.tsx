import type { PublicRoomDetailDto } from '../../api/modules'
import { formatBrl } from './money'

// The price block in the detail page's sidebar: the monthly figure in the lead, with the
// per-use rates beneath it, because a room is let by contract and taken by the hour.
//
// The per-use rates are two list items rather than one joined string, so the sidebar can
// set them side by side with a rule between them. A room let only by the hour therefore
// shows one cell and no stray separator.
//
// Anything the room does not carry is left out — the backend already reports a rate of
// zero as absent, so "R$ 0,00/hora" never reaches here.
export function RoomPrice({ room }: { room: PublicRoomDetailDto }) {
  const rates = [
    room.hourlyRate === null ? null : { key: 'hourly', amount: formatBrl(room.hourlyRate), unit: '/hora' },
    room.dailyRate === null ? null : { key: 'daily', amount: formatBrl(room.dailyRate), unit: '/dia' },
  ].filter((rate): rate is { key: string; amount: string; unit: string } => rate !== null)

  if (room.monthlyRate === null && rates.length === 0) return null

  return (
    <div className="room-price" data-testid="room-price">
      {room.monthlyRate !== null && (
        <p className="room-price-main" data-testid="room-price-main">
          {formatBrl(room.monthlyRate)}
          <span className="room-price-unit"> /mês</span>
        </p>
      )}
      {rates.length > 0 && (
        <ul className="room-price-rates" data-testid="room-price-rates">
          {rates.map(({ key, amount, unit }) => (
            <li className="room-price-rate" key={key}>
              <strong>{amount}</strong>
              <span className="room-price-unit">{unit}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
