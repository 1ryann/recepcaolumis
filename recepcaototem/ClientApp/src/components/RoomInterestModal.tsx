import { type FormEvent, useEffect, useState } from 'react'
import { ApiError } from '../api/client'
import { totemRoomApi } from '../api/modules'
import { Modal } from './Modal'

// Extracted from TotemRoomDetail.tsx's inline "Tenho interesse" form (Task 4, room-rental
// UX fixes) so the room-detail page can keep showing photos/name/availability at all times
// instead of swapping them out for a form. This component owns its own form state,
// client-side validation, and submit handling — the only thing it hands back to the caller
// is `onSuccess`, called with EXACTLY the same shape TotemRoomDetail.tsx has always passed
// to `navigate(..., { state })`. TotemRoomInterestSuccess.tsx validates that shape strictly
// and has no notion of `desiredStartDate`/`desiredEndDate`, so those two new fields never
// leave this component.
export type RoomInterestSuccessState = {
  roomName: string
  whatsappUrl: string
  presentedAvailabilityLabel: string
}

type InquiryForm = {
  fullName: string
  whatsApp: string
  professionOrCompany: string
  note: string
  desiredStartDate: string
  desiredEndDate: string
}

const emptyForm: InquiryForm = {
  fullName: '',
  whatsApp: '',
  professionOrCompany: '',
  note: '',
  desiredStartDate: '',
  desiredEndDate: '',
}

export function RoomInterestModal({
  open,
  onClose,
  roomId,
  roomName,
  onSuccess,
}: {
  open: boolean
  onClose: () => void
  roomId: string
  roomName: string
  onSuccess: (state: RoomInterestSuccessState) => void
}) {
  const [form, setForm] = useState<InquiryForm>(emptyForm)
  const [formError, setFormError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  // Every time the modal opens it starts from a clean slate — closing it (X, backdrop,
  // Escape, or Cancel, all of which funnel through `onClose`/re-render with `open=false`)
  // never has to remember to reset state for next time.
  useEffect(() => {
    if (open) {
      setForm(emptyForm)
      setFormError('')
    }
  }, [open])

  const change = (field: keyof InquiryForm, value: string) =>
    setForm((current) => ({ ...current, [field]: value }))

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting) return
    setFormError('')
    if (
      !form.fullName.trim() || !form.whatsApp.trim() || !form.professionOrCompany.trim() ||
      !form.desiredStartDate || !form.desiredEndDate
    ) {
      setFormError('Preencha nome, WhatsApp, profissão/empresa e as datas desejadas.')
      return
    }
    if (form.desiredEndDate < form.desiredStartDate) {
      setFormError('A data final não pode ser anterior à data inicial.')
      return
    }
    setSubmitting(true)
    try {
      const result = await totemRoomApi.createInquiry(roomId, {
        fullName: form.fullName.trim(),
        whatsApp: form.whatsApp.trim(),
        professionOrCompany: form.professionOrCompany.trim(),
        note: form.note.trim() || null,
        desiredStartDate: form.desiredStartDate,
        desiredEndDate: form.desiredEndDate,
      })
      onSuccess({
        roomName,
        whatsappUrl: result.whatsappUrl,
        presentedAvailabilityLabel: result.presentedAvailabilityLabel,
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

  return (
    <Modal open={open} title="Tenho interesse" subtitle={roomName} onClose={onClose}>
      <form className="totem-room-detail-form" onSubmit={submit} noValidate>
        <label className="field-label" htmlFor="room-interest-name">
          Nome
          <input
            id="room-interest-name"
            className="field-input"
            autoComplete="name"
            value={form.fullName}
            required
            onChange={(e) => change('fullName', e.target.value)}
          />
        </label>
        <label className="field-label" htmlFor="room-interest-whatsapp">
          WhatsApp
          <input
            id="room-interest-whatsapp"
            className="field-input"
            inputMode="tel"
            autoComplete="tel"
            placeholder="(69) 99999-9999"
            value={form.whatsApp}
            required
            onChange={(e) => change('whatsApp', e.target.value)}
          />
        </label>
        <label className="field-label" htmlFor="room-interest-profession">
          Profissão/Empresa
          <input
            id="room-interest-profession"
            className="field-input"
            value={form.professionOrCompany}
            required
            onChange={(e) => change('professionOrCompany', e.target.value)}
          />
        </label>

        <div className="room-interest-modal-dates">
          <label className="field-label" htmlFor="room-interest-start-date">
            Data de início desejada
            <input
              id="room-interest-start-date"
              className="field-input"
              type="date"
              value={form.desiredStartDate}
              required
              onChange={(e) => change('desiredStartDate', e.target.value)}
            />
          </label>
          <label className="field-label" htmlFor="room-interest-end-date">
            Data de término desejada
            <input
              id="room-interest-end-date"
              className="field-input"
              type="date"
              value={form.desiredEndDate}
              required
              onChange={(e) => change('desiredEndDate', e.target.value)}
            />
          </label>
        </div>

        <label className="field-label" htmlFor="room-interest-note">
          Observação (opcional)
          <textarea
            id="room-interest-note"
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
            onClick={onClose}
          >
            Cancelar
          </button>
        </div>
      </form>
    </Modal>
  )
}
