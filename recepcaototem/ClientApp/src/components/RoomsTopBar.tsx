import { Link } from 'react-router-dom'
import { KioskClock } from '../features/totem/KioskClock'
import { roomsBasePath, type RoomsSurface } from '../features/rooms/useRoomsSurface'
import { LumisLogo } from '../theme/LumisLogo'

// The bar across the top of the public room pages: the mark, and the way back to the
// catalogue. Nothing else.
//
// There is deliberately no account control. The page is a public listing someone landed on
// to look at a room, and a sign-in button in the corner is the one thing on the screen that
// leads away from it. There is no "Favoritos" either — the system has no favourites, and a
// link that leads nowhere is worse than an absent one.
//
// The kiosk gets the mark and the clock, no links: it stands in the lobby, and every
// destination is a place the person standing at it cannot come back from.
//
// The logo is a real <Link> rather than a button so it behaves like a link (middle-click,
// open in a new tab, a visible target in the status bar) and so assistive tech announces
// where it goes — the previous button gave no hint that clicking the logo was the only way
// back to the list.
export function RoomsTopBar({ surface }: { surface: RoomsSurface }) {
  const basePath = roomsBasePath(surface)

  return (
    <header className="totem-room-detail-bar">
      <div className="rooms-top-brand">
        <Link to={basePath} className="totem-room-detail-logo-link" aria-label="Voltar para salas">
          <LumisLogo className="totem-room-detail-logo" alt="LUMIS" width={132} height={40} />
        </Link>

        {surface === 'public' && (
          <nav className="rooms-top-nav" aria-label="Navegação principal">
            <Link className="rooms-top-link" to="/salas">Salas</Link>
          </nav>
        )}
      </div>

      {surface === 'kiosk' && <KioskClock />}
    </header>
  )
}
