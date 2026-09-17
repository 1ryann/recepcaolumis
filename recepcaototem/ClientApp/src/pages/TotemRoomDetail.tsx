import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { totemRoomApi, type PublicRoomAvailability, type PublicRoomDetailDto } from '../api/modules'
import { RoomInterestModal } from '../components/RoomInterestModal'
import { RoomPhotoGallery } from '../components/RoomPhotoGallery'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { KioskClock } from '../features/totem/KioskClock'
import { LumisLogo } from '../theme/LumisLogo'

// `/totem/salas/:id` — reached from a card on `/totem/salas` (Task 9). Shows the room's
// full photo gallery, description (no price/tariff, ever — the backend DTO never carries
// one) and availability at all times, plus a "Tenho interesse" button that opens
// `RoomInterestModal` (Task 4, room-rental UX fixes — the inquiry form used to be inlined
// here, swapping out for the button; it is now a modal so the room's info never disappears
// while the visitor fills it in). A successful submit hands the backend's
// `whatsappUrl`/`presentedAvailabilityLabel` to `/totem/salas/{id}/interesse` via
// navigation state only (never storage/URL) — see TotemRoomInterestSuccess.tsx.
//
// The Now/Soon label rendered here is a client-side reformat of the structured
// `availability`/`availableFrom` fields (the detail DTO has no pre-formatted label — that
// only exists on the POST response as `presentedAvailabilityLabel`). The date-splitting
// approach mirrors TotemRoomsCatalog.tsx's `dateLabel` exactly (never `new Date` on a bare
// "YYYY-MM-DD"); it is duplicated here rather than extracted to a shared module because
// TotemRoomsCatalog.tsx is outside this task's file list and the duplication is a single
// one-line pure function.
type Phase = 'loading' | 'ready' | 'error' | 'notFound'

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

  const goBack = () => navigate('/totem/salas')

  return (
    <main className="totem-room-detail">
      <LumisBackground />

      <header className="totem-room-detail-bar">
        <button
          type="button"
          className="totem-room-detail-logo-link"
          onClick={goBack}
          aria-label="Voltar para salas"
        >
          <LumisLogo
            className="totem-room-detail-logo"
            alt="LUMIS"
            width={132}
            height={40}
          />
        </button>
        <KioskClock />
      </header>

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

            <div className="totem-room-detail-info">
              <h1 className="totem-room-detail-title">{room.name}</h1>
              {room.description && <p className="totem-room-detail-description">{room.description}</p>}
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

              <RoomInterestModal
                open={showInterestModal}
                onClose={() => setShowInterestModal(false)}
                roomId={room.id}
                roomName={room.name}
                onSuccess={(state) => {
                  setShowInterestModal(false)
                  navigate(`/totem/salas/${encodeURIComponent(room.id)}/interesse`, { state })
                }}
              />
            </div>
          </div>
        )}
      </div>
    </main>
  )
}
