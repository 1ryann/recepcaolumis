import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'
import type { TotemProfessionalCardDto } from '../../api/modules'
import { professionalInitials } from './professionalInitials'
import { BorderBeam } from './magic/BorderBeam'
import { ProgressiveBlur } from './magic/ProgressiveBlur'
import { usePrefersReducedMotion } from './magic/usePrefersReducedMotion'

// The public Totem professional carousel: a horizontal, scroll-snapped strip of cards with a
// larger centred active card and its neighbours partly visible. It is driven purely by
// `activeIndex` state, so the big prev/next arrows, the dots, and keyboard navigation
// (ArrowLeft/ArrowRight/Home/End on the listbox) all work even where there is no layout
// (jsdom). Touch swipe rides the browser's native scroll-snap; a mouse-drag fallback moves
// `scrollLeft` directly and, past a small threshold, suppresses the click that would
// otherwise select the card under the pointer. `onScroll` only *reads* the nearest-centre
// card to keep `activeIndex` in sync — it never scrolls back, so there is no feedback loop.
// Status is conveyed as a dot *and* a word (never colour alone); the active card alone wears
// the BorderBeam, and ProgressiveBlur softens both edges.

type ProfessionalStatus = TotemProfessionalCardDto['status']

const STATUS_LABEL: Record<ProfessionalStatus, string> = {
  AVAILABLE: 'Disponível',
  IN_SERVICE: 'Em atendimento',
  UNAVAILABLE: 'Indisponível',
}

const STATUS_CLASS: Record<ProfessionalStatus, string> = {
  AVAILABLE: 'totem-status-ok',
  IN_SERVICE: 'totem-status-busy',
  UNAVAILABLE: 'totem-status-muted',
}

const DRAG_THRESHOLD_PX = 6

interface TotemProfessionalCarouselProps {
  professionals: TotemProfessionalCardDto[]
  onActiveChange: (professional: TotemProfessionalCardDto) => void
}

export function TotemProfessionalCarousel({
  professionals,
  onActiveChange,
}: TotemProfessionalCarouselProps) {
  const [activeIndex, setActiveIndex] = useState(0)
  const reduced = usePrefersReducedMotion()
  const behavior: ScrollBehavior = reduced ? 'auto' : 'smooth'

  const viewportRef = useRef<HTMLDivElement>(null)
  const cardRefs = useRef<Array<HTMLButtonElement | null>>([])
  const didDragRef = useRef(false)
  const pointerActiveRef = useRef(false)
  const dragStartXRef = useRef(0)
  const dragTravelRef = useRef(0)
  const rafRef = useRef<number | null>(null)

  // Latest-value refs so the async onScroll handler never reads a stale render.
  const activeIndexRef = useRef(0)
  activeIndexRef.current = activeIndex
  const onActiveChangeRef = useRef(onActiveChange)
  onActiveChangeRef.current = onActiveChange

  const count = professionals.length

  // The first professional is active whenever the list identity changes (mount included),
  // and that choice is emitted exactly once per change.
  useEffect(() => {
    if (count === 0) return
    setActiveIndex(0)
    activeIndexRef.current = 0
    onActiveChangeRef.current(professionals[0])
  }, [professionals, count])

  // Cancel any pending rAF on unmount.
  useEffect(
    () => () => {
      if (rafRef.current != null) cancelAnimationFrame(rafRef.current)
    },
    [],
  )

  function centre(index: number) {
    const viewport = viewportRef.current
    const card = cardRefs.current[index]
    if (!viewport || !card || typeof viewport.scrollTo !== 'function') return
    const raw = card.offsetLeft - (viewport.clientWidth - card.offsetWidth) / 2
    viewport.scrollTo({ left: Number.isFinite(raw) ? raw : 0, behavior })
  }

  function setActive(index: number) {
    if (count === 0) return
    const clamped = Math.max(0, Math.min(count - 1, index))
    if (clamped === activeIndexRef.current) {
      centre(clamped)
      return
    }
    setActiveIndex(clamped)
    activeIndexRef.current = clamped
    onActiveChangeRef.current(professionals[clamped])
    centre(clamped)
  }

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    switch (event.key) {
      case 'ArrowRight':
        event.preventDefault()
        setActive(activeIndexRef.current + 1)
        break
      case 'ArrowLeft':
        event.preventDefault()
        setActive(activeIndexRef.current - 1)
        break
      case 'Home':
        event.preventDefault()
        setActive(0)
        break
      case 'End':
        event.preventDefault()
        setActive(count - 1)
        break
      default:
    }
  }

  function onPointerDown(event: PointerEvent<HTMLDivElement>) {
    if (event.pointerType !== 'mouse') return
    pointerActiveRef.current = true
    dragStartXRef.current = event.clientX
    dragTravelRef.current = 0
    didDragRef.current = false
  }

  function onPointerMove(event: PointerEvent<HTMLDivElement>) {
    if (event.pointerType !== 'mouse' || !pointerActiveRef.current) return
    const delta = event.clientX - dragStartXRef.current
    dragStartXRef.current = event.clientX
    dragTravelRef.current += Math.abs(delta)
    const viewport = viewportRef.current
    if (viewport) viewport.scrollLeft -= delta
    if (dragTravelRef.current > DRAG_THRESHOLD_PX) didDragRef.current = true
  }

  function onPointerEnd(event: PointerEvent<HTMLDivElement>) {
    if (event.pointerType !== 'mouse') return
    pointerActiveRef.current = false
  }

  // Derive the active card from whichever card centre sits nearest the viewport centre.
  // Guarded so it neither throws nor NaNs under zero geometry, and it never scrolls.
  function onScroll() {
    if (rafRef.current != null) return
    rafRef.current = requestAnimationFrame(() => {
      rafRef.current = null
      const viewport = viewportRef.current
      if (!viewport) return
      const cards = cardRefs.current
      const measurable =
        viewport.clientWidth > 0 && cards.some((card) => card != null && card.offsetWidth > 0)
      if (!measurable) return
      const viewportCentre = viewport.scrollLeft + viewport.clientWidth / 2
      let nearest = activeIndexRef.current
      let bestDistance = Number.POSITIVE_INFINITY
      cards.forEach((card, index) => {
        if (!card) return
        const cardCentre = card.offsetLeft + card.offsetWidth / 2
        const distance = Math.abs(cardCentre - viewportCentre)
        if (distance < bestDistance) {
          bestDistance = distance
          nearest = index
        }
      })
      if (nearest !== activeIndexRef.current) {
        setActiveIndex(nearest)
        activeIndexRef.current = nearest
        onActiveChangeRef.current(professionals[nearest])
      }
    })
  }

  return (
    <div className="totem-carousel">
      <ProgressiveBlur side="left" />
      <ProgressiveBlur side="right" />

      <button
        type="button"
        className="totem-carousel-arrow is-prev"
        aria-label="Anterior"
        onClick={() => setActive(activeIndexRef.current - 1)}
      >
        <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <path
            d="M15 4 7 12l8 8"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      </button>

      <div
        className="totem-carousel-viewport"
        role="listbox"
        aria-label="Profissionais"
        tabIndex={0}
        ref={viewportRef}
        onKeyDown={onKeyDown}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerEnd}
        onPointerCancel={onPointerEnd}
        onPointerLeave={onPointerEnd}
        onScroll={onScroll}
      >
        {professionals.map((professional, index) => {
          const active = index === activeIndex
          return (
            <button
              key={professional.id}
              type="button"
              role="option"
              aria-selected={active}
              aria-label={`${professional.name}, ${professional.profession}, ${STATUS_LABEL[professional.status]}`}
              className={`totem-carousel-card${active ? ' is-active' : ''}`}
              ref={(element) => {
                cardRefs.current[index] = element
              }}
              onClick={() => {
                if (didDragRef.current) {
                  didDragRef.current = false
                  return
                }
                setActive(index)
              }}
            >
              <Photo url={professional.photoUrl} name={professional.name} />
              <strong className="totem-carousel-name">{professional.name}</strong>
              <span className="totem-carousel-profession">{professional.profession}</span>
              <span className={`totem-carousel-status ${STATUS_CLASS[professional.status]}`}>
                <i className="totem-carousel-status-dot" aria-hidden="true" />
                {STATUS_LABEL[professional.status]}
              </span>
              <BorderBeam active={active} />
            </button>
          )
        })}
      </div>

      <button
        type="button"
        className="totem-carousel-arrow is-next"
        aria-label="Próximo"
        onClick={() => setActive(activeIndexRef.current + 1)}
      >
        <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <path
            d="m9 4 8 8-8 8"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      </button>

      <div className="totem-carousel-dots" role="group" aria-label="Selecionar profissional">
        {professionals.map((professional, index) => (
          <button
            key={professional.id}
            type="button"
            className={`totem-carousel-dot${index === activeIndex ? ' is-active' : ''}`}
            aria-label={`Ir para ${professional.name}`}
            aria-current={index === activeIndex}
            onClick={() => setActive(index)}
          />
        ))}
      </div>
    </div>
  )
}

function Photo({ url, name }: { url: string | null; name: string }) {
  const [broken, setBroken] = useState(false)

  useEffect(() => {
    setBroken(false)
  }, [url])

  if (!url || broken) {
    return (
      <span className="totem-carousel-photo totem-carousel-initials" aria-hidden="true">
        {professionalInitials(name)}
      </span>
    )
  }

  return (
    <img
      className="totem-carousel-photo"
      src={url}
      alt=""
      role="img"
      loading="lazy"
      draggable={false}
      onError={() => setBroken(true)}
    />
  )
}
