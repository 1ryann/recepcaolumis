import { ChevronLeft, ChevronRight, DoorOpen, X } from 'lucide-react'
import { useEffect, useState } from 'react'

// Self-contained photo gallery for the public room-detail page (Task 3 of the room-rental
// UX-fixes plan). Extracted out of TotemRoomDetail.tsx, which previously inlined this same
// state (`selectedPhoto`/`failedPhotos`) directly — see that file's git history. Owns:
// - `currentIndex`: the photo shown large (and the one the lightbox opens on).
// - `failedPhotos`: indices whose <img> fired onError, shared between the main photo, its
//   matching thumbnail, and the lightbox — a single source of truth so a broken photo never
//   shows a live spinner/broken-image icon in one place and a fallback in another.
// - `lightboxOpen`: the enlarged, full-photo overlay.
// - `autoplayActive`: see the note above the autoplay effect below for why manual
//   interaction of any kind stops it for good rather than pausing/resuming it.
//
// Class names keep the existing `totem-room-detail-*` prefix (rather than a new
// `room-photo-gallery-*` one) because this is still visually/semantically "the room detail
// page's gallery" — only its code moved into its own file — and because the pre-existing
// `.totem-room-detail-photo`/`.totem-room-detail-thumb*`/`.totem-room-detail-fallback`
// selectors and test ids are preserved byte-for-byte so TotemRoomDetail.test.tsx did not
// need to change its expectations for the parts of the gallery that did not change
// behaviour (only its interaction tests moved here, onto the new component, since clicking
// a thumbnail now also opens the lightbox).
const AUTOPLAY_INTERVAL_MS = 5000

export function RoomPhotoGallery({ photoUrls, roomName }: { photoUrls: string[]; roomName: string }) {
  const [currentIndex, setCurrentIndex] = useState(0)
  const [failedPhotos, setFailedPhotos] = useState<Set<number>>(new Set())
  const [lightboxOpen, setLightboxOpen] = useState(false)
  const [autoplayActive, setAutoplayActive] = useState(true)

  const hasPhotos = photoUrls.length > 0
  const hasMultiple = photoUrls.length > 1

  // Autoplay: advances one slide every 5 s while there is more than one photo and no
  // manual interaction has happened yet. There is deliberately no "resume after a period
  // of inactivity" here — the task brief offers a choice between that and stopping for
  // good once the visitor has touched the gallery, and this picks the latter: it is the
  // simpler state machine (one boolean, never flipped back to true) and the more
  // predictable one for a visitor who deliberately picked a photo to linger on — nothing
  // should later yank it away to resume the slideshow.
  useEffect(() => {
    if (!hasMultiple || !autoplayActive) return
    const id = setInterval(() => {
      setCurrentIndex((current) => (current + 1) % photoUrls.length)
    }, AUTOPLAY_INTERVAL_MS)
    return () => clearInterval(id)
  }, [hasMultiple, autoplayActive, photoUrls.length])

  // Escape/ArrowLeft/ArrowRight while the lightbox is open. Mirrors Modal.tsx's own
  // document-level Escape listener rather than reusing Modal — see RoomPhotoGallery's
  // task report for why a dedicated overlay was built instead of Modal.
  useEffect(() => {
    if (!lightboxOpen) return
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setLightboxOpen(false)
      else if (hasMultiple && event.key === 'ArrowRight') showNext()
      else if (hasMultiple && event.key === 'ArrowLeft') showPrev()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lightboxOpen, hasMultiple])

  const markPhotoFailed = (index: number) =>
    setFailedPhotos((current) => {
      if (current.has(index)) return current
      const next = new Set(current)
      next.add(index)
      return next
    })

  const stopAutoplay = () => setAutoplayActive(false)

  const selectThumbnail = (index: number) => {
    stopAutoplay()
    setCurrentIndex(index)
    setLightboxOpen(true)
  }

  const openLightboxOnCurrent = () => {
    stopAutoplay()
    setLightboxOpen(true)
  }

  const closeLightbox = () => setLightboxOpen(false)

  const showNext = () => {
    stopAutoplay()
    setCurrentIndex((current) => (current + 1) % photoUrls.length)
  }

  const showPrev = () => {
    stopAutoplay()
    setCurrentIndex((current) => (current - 1 + photoUrls.length) % photoUrls.length)
  }

  if (!hasPhotos) {
    return (
      <div className="totem-room-detail-gallery">
        <div className="totem-room-detail-fallback" data-testid="totem-room-detail-fallback" aria-hidden="true">
          <DoorOpen size={40} />
        </div>
      </div>
    )
  }

  return (
    <div className="totem-room-detail-gallery">
      <button
        type="button"
        className="totem-room-detail-photo-trigger"
        aria-label={`Ampliar foto ${currentIndex + 1} de ${roomName}`}
        onClick={openLightboxOnCurrent}
      >
        {failedPhotos.has(currentIndex) ? (
          <div className="totem-room-detail-fallback" data-testid="totem-room-detail-fallback" aria-hidden="true">
            <DoorOpen size={40} />
          </div>
        ) : (
          <img
            className="totem-room-detail-photo"
            src={photoUrls[currentIndex]}
            alt={`Foto da sala ${roomName}`}
            onError={() => markPhotoFailed(currentIndex)}
          />
        )}
      </button>

      {hasMultiple && (
        <div className="totem-room-detail-dots" role="group" aria-label="Selecionar foto">
          {photoUrls.map((_, index) => (
            <button
              key={index}
              type="button"
              className={`totem-room-detail-dot${index === currentIndex ? ' is-active' : ''}`}
              aria-label={`Ir para foto ${index + 1}`}
              aria-current={index === currentIndex}
              onClick={() => { stopAutoplay(); setCurrentIndex(index) }}
            />
          ))}
        </div>
      )}

      {hasMultiple && (
        <div className="totem-room-detail-thumbs">
          {photoUrls.map((url, index) => (
            <button
              key={url}
              type="button"
              className={`totem-room-detail-thumb${index === currentIndex ? ' is-selected' : ''}`}
              aria-label={`Ver foto ${index + 1} de ${roomName}`}
              aria-pressed={index === currentIndex}
              onClick={() => selectThumbnail(index)}
            >
              {failedPhotos.has(index) ? (
                <div className="totem-room-detail-thumb-fallback" data-testid="totem-room-detail-thumb-fallback" aria-hidden="true">
                  <DoorOpen size={16} />
                </div>
              ) : (
                <img src={url} alt="" onError={() => markPhotoFailed(index)} />
              )}
            </button>
          ))}
        </div>
      )}

      {lightboxOpen && (
        <div
          className="totem-room-detail-lightbox-backdrop"
          role="presentation"
          onMouseDown={(event) => event.target === event.currentTarget && closeLightbox()}
        >
          <section
            className="totem-room-detail-lightbox"
            role="dialog"
            aria-modal="true"
            aria-label={`Foto ampliada da sala ${roomName}`}
          >
            <button
              type="button"
              className="totem-room-detail-lightbox-close icon-button"
              onClick={closeLightbox}
              aria-label="Fechar"
            >
              <X size={22} />
            </button>

            {hasMultiple && (
              <button
                type="button"
                className="totem-room-detail-lightbox-nav totem-room-detail-lightbox-prev"
                onClick={showPrev}
                aria-label="Foto anterior"
              >
                <ChevronLeft size={28} />
              </button>
            )}

            {failedPhotos.has(currentIndex) ? (
              <div
                className="totem-room-detail-lightbox-fallback"
                data-testid="totem-room-detail-lightbox-fallback"
                aria-hidden="true"
              >
                <DoorOpen size={64} />
              </div>
            ) : (
              <img
                className="totem-room-detail-lightbox-photo"
                src={photoUrls[currentIndex]}
                alt={`Foto ampliada da sala ${roomName}`}
                onError={() => markPhotoFailed(currentIndex)}
              />
            )}

            {hasMultiple && (
              <button
                type="button"
                className="totem-room-detail-lightbox-nav totem-room-detail-lightbox-next"
                onClick={showNext}
                aria-label="Próxima foto"
              >
                <ChevronRight size={28} />
              </button>
            )}
          </section>
        </div>
      )}
    </div>
  )
}
