import { DoorOpen } from 'lucide-react'
import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { totemRoomApi, type PublicRoomAvailability, type PublicRoomDetailDto } from '../api/modules'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { KioskClock } from '../features/totem/KioskClock'

// `/totem/salas/:id` — reached from a card on `/totem/salas` (Task 9). Shows the room's
// full photo gallery and description (no price/tariff, ever — the backend DTO never
// carries one) plus a "Tenho interesse" inquiry form. A successful submit hands the
// backend's `whatsappUrl`/`presentedAvailabilityLabel` to `/totem/salas/{id}/interesse`
// via navigation state only (never storage/URL) — see TotemRoomInterestSuccess.tsx.
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

type InquiryForm = {
  fullName: string
  whatsApp: string
  professionOrCompany: string
  note: string
}

const emptyForm: InquiryForm = { fullName: '', whatsApp: '', professionOrCompany: '', note: '' }

function isAbortError(error: unknown) {
  return (error as { name?: string } | null)?.name === 'AbortError'
}

export function TotemRoomDetail() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const [phase, setPhase] = useState<Phase>('loading')
  const [room, setRoom] = useState<PublicRoomDetailDto | null>(null)
  const [selectedPhoto, setSelectedPhoto] = useState(0)
  const [showForm, setShowForm] = useState(false)
  const [form, setForm] = useState<InquiryForm>(emptyForm)
  const [formError, setFormError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const load = useCallback(() => {
    const controller = new AbortController()
    setPhase('loading')
    totemRoomApi
      .detail(id, controller.signal)
      .then((detail) => {
        setRoom(detail)
        setSelectedPhoto(0)
        setShowForm(false)
        setForm(emptyForm)
        setFormError('')
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

  const change = (field: keyof InquiryForm, value: string) =>
    setForm((current) => ({ ...current, [field]: value }))

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting || !room) return
    setFormError('')
    if (!form.fullName.trim() || !form.whatsApp.trim() || !form.professionOrCompany.trim()) {
      setFormError('Preencha nome, WhatsApp e profissão/empresa.')
      return
    }
    setSubmitting(true)
    try {
      const result = await totemRoomApi.createInquiry(room.id, {
        fullName: form.fullName.trim(),
        whatsApp: form.whatsApp.trim(),
        professionOrCompany: form.professionOrCompany.trim(),
        note: form.note.trim() || null,
      })
      navigate(`/totem/salas/${encodeURIComponent(room.id)}/interesse`, {
        state: {
          roomName: room.name,
          whatsappUrl: result.whatsappUrl,
          presentedAvailabilityLabel: result.presentedAvailabilityLabel,
        },
      })
    } catch (error) {
      setFormError(
        error instanceof ApiError
          ? error.message
          : 'Não foi possível enviar seu interesse. Tente novamente.',
      )
    } finally {
      setSubmitting(false)
    }
  }

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
          <img
            className="totem-room-detail-logo"
            src="/lumis-logo-transparent.png"
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
            <div className="totem-room-detail-gallery">
              {room.photoUrls.length > 0 ? (
                <>
                  <img
                    className="totem-room-detail-photo"
                    src={room.photoUrls[selectedPhoto]}
                    alt={`Foto da sala ${room.name}`}
                  />
                  {room.photoUrls.length > 1 && (
                    <div className="totem-room-detail-thumbs">
                      {room.photoUrls.map((url, index) => (
                        <button
                          key={url}
                          type="button"
                          className={`totem-room-detail-thumb${index === selectedPhoto ? ' is-selected' : ''}`}
                          aria-label={`Ver foto ${index + 1} de ${room.name}`}
                          aria-pressed={index === selectedPhoto}
                          onClick={() => setSelectedPhoto(index)}
                        >
                          <img src={url} alt="" />
                        </button>
                      ))}
                    </div>
                  )}
                </>
              ) : (
                <div className="totem-room-detail-fallback" aria-hidden="true">
                  <DoorOpen size={40} />
                </div>
              )}
            </div>

            <div className="totem-room-detail-info">
              <h1 className="totem-room-detail-title">{room.name}</h1>
              {room.description && <p className="totem-room-detail-description">{room.description}</p>}
              <p className="totem-room-detail-availability">
                {availabilityLabel(room.availability, room.availableFrom)}
              </p>

              {!showForm && (
                <button
                  type="button"
                  className="totem-btn totem-btn-primary"
                  onClick={() => setShowForm(true)}
                >
                  Tenho interesse
                </button>
              )}

              {showForm && (
                <form className="totem-room-detail-form" onSubmit={submit} noValidate>
                  <label className="field-label" htmlFor="totem-room-detail-name">
                    Nome
                    <input
                      id="totem-room-detail-name"
                      className="field-input"
                      value={form.fullName}
                      required
                      onChange={(e) => change('fullName', e.target.value)}
                    />
                  </label>
                  <label className="field-label" htmlFor="totem-room-detail-whatsapp">
                    WhatsApp
                    <input
                      id="totem-room-detail-whatsapp"
                      className="field-input"
                      value={form.whatsApp}
                      required
                      onChange={(e) => change('whatsApp', e.target.value)}
                    />
                  </label>
                  <label className="field-label" htmlFor="totem-room-detail-profession">
                    Profissão/Empresa
                    <input
                      id="totem-room-detail-profession"
                      className="field-input"
                      value={form.professionOrCompany}
                      required
                      onChange={(e) => change('professionOrCompany', e.target.value)}
                    />
                  </label>
                  <label className="field-label" htmlFor="totem-room-detail-note">
                    Observação (opcional)
                    <textarea
                      id="totem-room-detail-note"
                      className="field-input"
                      rows={3}
                      value={form.note}
                      onChange={(e) => change('note', e.target.value)}
                    />
                  </label>

                  {formError && (
                    <div className="form-error" role="alert">{formError}</div>
                  )}

                  <div className="totem-room-detail-form-actions">
                    <button type="submit" className="totem-btn totem-btn-primary" disabled={submitting}>
                      {submitting ? 'Enviando…' : 'Enviar interesse'}
                    </button>
                    <button
                      type="button"
                      className="totem-btn totem-btn-ghost"
                      disabled={submitting}
                      onClick={() => { setShowForm(false); setFormError('') }}
                    >
                      Cancelar
                    </button>
                  </div>
                </form>
              )}
            </div>
          </div>
        )}
      </div>
    </main>
  )
}
