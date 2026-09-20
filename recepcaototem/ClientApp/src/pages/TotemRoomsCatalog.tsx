import { DoorOpen } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { totemRoomApi, type PublicRoomCardDto } from '../api/modules'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { KioskClock } from '../features/totem/KioskClock'
import { BlurFade } from '../features/totem/magic/BlurFade'
import { RoomChips } from '../features/rooms/RoomChips'
import { availabilityLabel } from '../features/rooms/roomAvailability'
import { roomsBasePath, useRoomsSurface } from '../features/rooms/useRoomsSurface'
import { LumisLogo } from '../theme/LumisLogo'
import '../styles.totem-rooms.css'

// The room-rental catalogue, served on two routes by one component: `/totem/salas` on the
// kiosk in the lobby, and `/salas` in a visitor's own browser from the landing page. See
// useRoomsSurface for why the route decides rather than a device flag; here the surface
// only changes the chrome (the kiosk clock) and where "back" leads.
//
// All data comes from the anonymous `GET /api/totem/rooms`. That endpoint used to hide
// every price on purpose; it now advertises the monthly one, because a catalogue whose
// prices are secret makes the visitor write in to ask. What it still hides is who occupies
// a room and on what terms.
//
// Rooms are split into "Disponíveis agora" (a grid, shown first) and "Disponíveis em
// breve" (a row that scrolls sideways, ordered by availableFrom then name); a section is
// rendered only when it has at least one room.
// Four phases share the shell, mirroring TotemProfessionals:
//   loading -> shimmer skeleton cards
//   ready   -> the Now/Soon sections
//   empty   -> "Nenhuma sala disponível no momento." + Tentar novamente + Voltar
//   error   -> "Não foi possível carregar as salas." + the same two buttons
// The LUMIS logo doubles as the way back — to `/totem` on the kiosk, to the landing page
// in a browser.
type Phase = 'loading' | 'ready' | 'empty' | 'error'

function groupRooms(rooms: PublicRoomCardDto[]) {
  const now = rooms.filter((room) => room.availability === 'AVAILABLE_NOW')
  const soon = rooms
    .filter((room) => room.availability === 'AVAILABLE_SOON')
    .slice()
    .sort((a, b) => (a.availableFrom ?? '').localeCompare(b.availableFrom ?? '') || a.name.localeCompare(b.name))
  return { now, soon }
}

function RoomCard({ room, onSelect }: { room: PublicRoomCardDto, onSelect: () => void }) {
  const [imageFailed, setImageFailed] = useState(false)
  const showPhoto = Boolean(room.coverPhotoUrl) && !imageFailed

  // The photo fills the card and the name sits over it. The description moved to the
  // detail page: at this size it was a paragraph nobody reads, and the chips say more in
  // less space. A room with no photo keeps the door icon behind the same overlay, so the
  // grid stays even while the photos are still being taken.
  return (
    <button type="button" className="totem-rooms-card" onClick={onSelect}>
      <div className="totem-rooms-card-media">
        {showPhoto ? (
          <img
            className="totem-rooms-card-photo"
            src={room.coverPhotoUrl ?? undefined}
            alt={`Foto da sala ${room.name}`}
            onError={() => setImageFailed(true)}
          />
        ) : (
          <div className="totem-rooms-card-fallback" data-testid="totem-rooms-card-fallback" aria-hidden="true">
            <DoorOpen size={32} />
          </div>
        )}
      </div>
      <div className="totem-rooms-card-body">
        <h3 className="totem-rooms-card-name">{room.name}</h3>
        <RoomChips room={room} />
        {room.availability === 'AVAILABLE_SOON' && room.availableFrom && (
          <p className="totem-rooms-card-availability">
            {`Próxima disponibilidade: ${availabilityLabel(room.availableFrom)}`}
          </p>
        )}
      </div>
    </button>
  )
}

export function TotemRoomsCatalog() {
  const navigate = useNavigate()
  const surface = useRoomsSurface()
  const basePath = roomsBasePath(surface)
  // On the kiosk the logo returns to the kiosk's own home; in a browser it returns to the
  // landing page, which is where the visitor came from.
  const homePath = surface === 'kiosk' ? '/totem' : '/'
  const [phase, setPhase] = useState<Phase>('loading')
  const [rooms, setRooms] = useState<PublicRoomCardDto[]>([])

  // A fresh AbortController per call keeps this callable both from the mount effect
  // (which aborts it on cleanup) and from the "Tentar novamente" button.
  const load = useCallback(() => {
    const controller = new AbortController()
    setPhase('loading')
    totemRoomApi
      .list(controller.signal)
      .then((list) => {
        if (list.length) {
          setRooms(list)
          setPhase('ready')
        } else {
          setPhase('empty')
        }
      })
      .catch((error: unknown) => {
        if ((error as { name?: string } | null)?.name === 'AbortError') return
        setPhase('error')
      })
    return controller
  }, [])

  useEffect(() => {
    const controller = load()
    return () => controller.abort()
  }, [load])

  const { now, soon } = groupRooms(rooms)
  const goToDetail = (id: string) => navigate(`${basePath}/${encodeURIComponent(id)}`)

  return (
    <main className="totem-rooms">
      <LumisBackground />

      <header className="totem-rooms-bar">
        <button
          type="button"
          className="totem-rooms-logo-link"
          onClick={() => navigate(homePath)}
          aria-label="Voltar ao início"
        >
          <LumisLogo
            className="totem-rooms-logo"
            alt="LUMIS"
            width={132}
            height={40}
          />
        </button>
        {surface === 'kiosk' && <KioskClock />}
      </header>

      <BlurFade>
        <div className="totem-rooms-inner">
          {/* The cards say what this page is; a banner over them only pushed the grid
              down. The heading stays in the document for screen readers and the outline,
              which would otherwise start at the "Disponíveis agora" section. */}
          <h1 className="sr-only">Salas para alugar</h1>

          {phase === 'loading' && (
            <div className="totem-rooms-skeletons">
              <div data-testid="totem-rooms-skeleton-card" className="totem-skeleton-card" />
              <div data-testid="totem-rooms-skeleton-card" className="totem-skeleton-card" />
              <div data-testid="totem-rooms-skeleton-card" className="totem-skeleton-card" />
            </div>
          )}

          {phase === 'ready' && (
            <div className="totem-rooms-sections">
              {now.length > 0 && (
                <section className="totem-rooms-section">
                  <h2 className="totem-rooms-section-title">Disponíveis agora</h2>
                  <div className="totem-rooms-grid">
                    {now.map((room) => (
                      <RoomCard key={room.id} room={room} onSelect={() => goToDetail(room.id)} />
                    ))}
                  </div>
                </section>
              )}
              {soon.length > 0 && (
                <section className="totem-rooms-section">
                  <h2 className="totem-rooms-section-title">Disponíveis em breve</h2>
                  <div className="totem-rooms-grid is-scroller">
                    {soon.map((room) => (
                      <RoomCard key={room.id} room={room} onSelect={() => goToDetail(room.id)} />
                    ))}
                  </div>
                </section>
              )}
            </div>
          )}

          {phase === 'empty' && (
            <div className="totem-rooms-slot">
              <p className="totem-rooms-message">Nenhuma sala disponível no momento.</p>
              <button type="button" className="totem-btn totem-btn-primary" onClick={load}>
                Tentar novamente
              </button>
              <button type="button" className="totem-btn totem-btn-ghost" onClick={() => navigate(homePath)}>
                Voltar
              </button>
            </div>
          )}

          {phase === 'error' && (
            <div className="totem-rooms-slot">
              <p className="totem-rooms-message">Não foi possível carregar as salas.</p>
              <button type="button" className="totem-btn totem-btn-primary" onClick={load}>
                Tentar novamente
              </button>
              <button type="button" className="totem-btn totem-btn-ghost" onClick={() => navigate(homePath)}>
                Voltar
              </button>
            </div>
          )}
        </div>
      </BlurFade>
    </main>
  )
}
