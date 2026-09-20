import { useLocation } from 'react-router-dom'

// The same catalogue serves two audiences through two routes. `/totem/...` is the kiosk
// standing in the building: it shows the clock, and it must not offer WhatsApp, because
// tapping it there lands the visitor on a screen they cannot use. `/salas...` is the same
// catalogue in someone's own browser, reached from the landing page, where WhatsApp is the
// fastest way to talk to the reception and the kiosk chrome is noise.
//
// The route decides, not a device flag: a flag has to be set on the tablet and can be lost
// by a cache clear or a reconfiguration, and it would silently put the wrong screen in the
// lobby. A URL cannot drift.
export type RoomsSurface = 'kiosk' | 'public'

export function useRoomsSurface(): RoomsSurface {
  const { pathname } = useLocation()
  return pathname.startsWith('/totem') ? 'kiosk' : 'public'
}

/** Where a link from this surface should point, so the visitor stays on their own side. */
export function roomsBasePath(surface: RoomsSurface): string {
  return surface === 'kiosk' ? '/totem/salas' : '/salas'
}
