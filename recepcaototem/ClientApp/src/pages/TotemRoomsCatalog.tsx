import { DoorOpen } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { totemRoomApi, type PublicRoomCardDto } from '../api/modules'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { KioskClock } from '../features/totem/KioskClock'
import { BlurFade } from '../features/totem/magic/BlurFade'

// The `/totem/salas` screen — the public room-rental catalog reached from the "Alugar
// sala" CTA on `/totem/profissionais`. Anyone can view it without a check-in code; all
// data comes from the anonymous `GET /api/totem/rooms` endpoint, which already hides
// Tenant/Professional/ContractedRate/HourlyRate/DailyRate — this page must never display
// a price or tariff of any kind. Rooms are split into two sections: "Disponíveis agora"
// (shown first) and "Disponíveis em breve" (ordered by availableFrom then name); a
// section is rendered only when it has at least one room. Tapping a card navigates to
// `/totem/salas/{id}` (built in a later task — this screen only issues the navigation).
// Four phases share the kiosk shell, mirroring TotemProfessionals:
//   loading -> shimmer skeleton cards
//   ready   -> the Now/Soon sections
//   empty   -> "Nenhuma sala disponível no momento." + Tentar novamente + Voltar
//   error   -> "Não foi possível carregar as salas." + the same two buttons
// The LUMIS logo doubles as the way back to `/totem`, same as every other Totem screen.
type Phase = 'loading' | 'ready' | 'empty' | 'error'

// DateOnly comes over the wire as a bare "YYYY-MM-DD" string. Parsing it with `new Date`
// would read it as UTC midnight and can roll to the previous/next day once converted to
// local time — split it by hand instead to build the PT-BR "DD/MM/AAAA" label.
function dateLabel(value: string) {
  const [year, month, day] = value.split('-')
  return `${day}/${month}/${year}`
}

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
        {room.description && <p className="totem-rooms-card-description">{room.description}</p>}
        {room.availability === 'AVAILABLE_SOON' && room.availableFrom && (
          <p className="totem-rooms-card-availability">
            {`Disponível em breve — a partir de ${dateLabel(room.availableFrom)}`}
          </p>
        )}
      </div>
    </button>
  )
}

export function TotemRoomsCatalog() {
  const navigate = useNavigate()
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
  const goToDetail = (id: string) => navigate(`/totem/salas/${id}`)

  return (
    <main className="totem-rooms">
      <LumisBackground />

      <header className="totem-rooms-bar">
        <button
          type="button"
          className="totem-rooms-logo-link"
          onClick={() => navigate('/totem')}
          aria-label="Voltar ao início"
        >
          <img
            className="totem-rooms-logo"
            src="/lumis-logo-transparent.png"
            alt="LUMIS"
            width={132}
            height={40}
          />
        </button>
        <KioskClock />
      </header>

      <BlurFade>
        <div className="totem-rooms-inner">
          <h1 className="totem-rooms-title">Alugar sala</h1>

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
                  <div className="totem-rooms-grid">
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
              <button type="button" className="totem-btn totem-btn-ghost" onClick={() => navigate('/totem')}>
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
              <button type="button" className="totem-btn totem-btn-ghost" onClick={() => navigate('/totem')}>
                Voltar
              </button>
            </div>
          )}
        </div>
      </BlurFade>
    </main>
  )
}
