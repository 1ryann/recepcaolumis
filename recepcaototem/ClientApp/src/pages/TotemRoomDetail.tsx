import { MessageCircle } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { totemRoomApi, type PublicRoomAvailability, type PublicRoomDetailDto } from '../api/modules'
import { RoomInterestModal } from '../components/RoomInterestModal'
import { RoomPhotoGallery } from '../components/RoomPhotoGallery'
import { RoomsTopBar } from '../components/RoomsTopBar'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { RoomAmenityChecklist } from '../features/rooms/RoomAmenityChecklist'
import { RoomPrice } from '../features/rooms/RoomPrice'
import { RoomSpecs } from '../features/rooms/RoomSpecs'
import { categoryLabel } from '../features/rooms/roomFeatures'
import { roomsBasePath, useRoomsSurface } from '../features/rooms/useRoomsSurface'
import '../styles.totem-room.css'

// A single room, on `/totem/salas/:id` at the kiosk and `/salas/:id` in a browser. Shows
// the photo gallery, the price, the icon strip of features, the description and the
// availability, plus "Tenho interesse", which opens `RoomInterestModal` so the room's
// information never disappears while the visitor fills the form in. A successful submit
// hands the backend's `whatsappUrl`/`presentedAvailabilityLabel` to the `/interesse` page
// via navigation state only (never storage or the URL) — see TotemRoomInterestSuccess.
//
// Two things differ by surface, both from useRoomsSurface: the kiosk clock, and the
// "Falar pelo WhatsApp" button, which only a browser gets. That button is a plain link to
// a URL the *server* assembled — the browser never knows the reception's number.
//
// This page used to be forbidden from showing any price. That was reversed deliberately;
// what is still never shown is who occupies the room and on what terms.
type Phase = 'loading' | 'ready' | 'error' | 'notFound'

// Never `new Date` on a bare "YYYY-MM-DD": it reads as UTC midnight and can roll a day
// once converted to local time.
function dateLabel(value: string) {
  const [year, month, day] = value.split('-')
  return `${day}/${month}/${year}`
}

function availabilityLabel(availability: PublicRoomAvailability, availableFrom: string | null) {
  if (availability === 'AVAILABLE_NOW') return 'Disponível agora'
  return availableFrom
    ? `Disponível em breve — a partir de ${dateLabel(availableFrom)}`
    : 'Disponível em breve'
}

function isAbortError(error: unknown) {
  return (error as { name?: string } | null)?.name === 'AbortError'
}

export function TotemRoomDetail() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const surface = useRoomsSurface()
  const basePath = roomsBasePath(surface)
  const [phase, setPhase] = useState<Phase>('loading')
  const [room, setRoom] = useState<PublicRoomDetailDto | null>(null)
  const [showInterestModal, setShowInterestModal] = useState(false)

  const load = useCallback(() => {
    const controller = new AbortController()
    setPhase('loading')
    totemRoomApi
      .detail(id, controller.signal)
      .then((detail) => {
        setRoom(detail)
        setShowInterestModal(false)
        setPhase('ready')
      })
      .catch((error: unknown) => {
        if (isAbortError(error)) return
        setPhase(error instanceof ApiError && error.status === 404 ? 'notFound' : 'error')
      })
    return controller
  }, [id])

  useEffect(() => {
    const controller = load()
    return () => controller.abort()
  }, [load])

  const goBack = () => navigate(basePath)

  return (
    <main className="totem-room-detail">
      <LumisBackground />

      <RoomsTopBar surface={surface} />

      <div className="totem-room-detail-inner">
        {phase === 'loading' && (
          <div className="totem-room-detail-slot" data-testid="totem-room-detail-loading">
            <div className="totem-skeleton-card" />
          </div>
        )}

        {phase === 'notFound' && (
          <div className="totem-room-detail-slot">
            <p className="totem-rooms-message">Não encontramos essa sala.</p>
            <button type="button" className="totem-btn totem-btn-ghost" onClick={goBack}>
              Voltar
            </button>
          </div>
        )}

        {phase === 'error' && (
          <div className="totem-room-detail-slot">
            <p className="totem-rooms-message">Não foi possível carregar esta sala.</p>
            <button type="button" className="totem-btn totem-btn-primary" onClick={load}>
              Tentar novamente
            </button>
            <button type="button" className="totem-btn totem-btn-ghost" onClick={goBack}>
              Voltar
            </button>
          </div>
        )}

        {phase === 'ready' && room && (
          <div className="totem-room-detail-content">
            <RoomPhotoGallery photoUrls={room.photoUrls} roomName={room.name} />

            {/* The sidebar carries only what someone deciding needs: what the room is
                called and is for, what it costs, when it frees up, and how to ask for it.
                The prose and the comforts sit below, where length costs nothing. */}
            <aside className="totem-room-detail-info">
              <h1 className="totem-room-detail-title">{room.name}</h1>
              {categoryLabel(room.category) && (
                <p className="totem-room-detail-category" data-testid="totem-room-detail-category">
                  {categoryLabel(room.category)}
                </p>
              )}
              <RoomPrice room={room} />
              <p className="totem-room-detail-availability">
                {availabilityLabel(room.availability, room.availableFrom)}
              </p>

              <button
                type="button"
                className="totem-btn totem-btn-primary"
                onClick={() => setShowInterestModal(true)}
              >
                Tenho interesse
              </button>

              {/* Only in a browser, and only when a number is configured. On the kiosk the
                  link would land the visitor on a WhatsApp they cannot use. */}
              {surface === 'public' && room.whatsappUrl && (
                <a
                  className="totem-btn totem-room-detail-whatsapp"
                  href={room.whatsappUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <MessageCircle size={20} aria-hidden="true" />
                  Falar pelo WhatsApp
                </a>
              )}

              <RoomSpecs room={room} />
            </aside>

            {/* Two panels across the foot of the page. Each appears only when it has
                something to say, and the band collapses to whichever one remains rather
                than leaving a titled box with nothing in it. */}
            {(room.description || room.amenities.length > 0) && (
              <div className="totem-room-detail-about">
                {room.description && (
                  <section
                    className="totem-room-detail-panel"
                    data-testid="totem-room-detail-description-panel"
                  >
                    <h2 className="totem-room-detail-panel-title">Descrição</h2>
                    <p className="totem-room-detail-description">{room.description}</p>
                  </section>
                )}
                {room.amenities.length > 0 && (
                  <section
                    className="totem-room-detail-panel"
                    data-testid="totem-room-detail-amenities-panel"
                  >
                    <h2 className="totem-room-detail-panel-title">Comodidades</h2>
                    <RoomAmenityChecklist amenities={room.amenities} />
                  </section>
                )}
              </div>
            )}

            <RoomInterestModal
              open={showInterestModal}
              onClose={() => setShowInterestModal(false)}
              roomId={room.id}
              roomName={room.name}
              onSuccess={(state) => {
                setShowInterestModal(false)
                navigate(`${basePath}/${encodeURIComponent(room.id)}/interesse`, { state })
              }}
            />
          </div>
        )}
      </div>
    </main>
  )
}
