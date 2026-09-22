import { type FormEvent, useEffect, useState } from 'react'
import type { ProfessionalDto, ProfessionalInput } from '../../api/modules'
import { formatBrazilWhatsApp } from '../../utils/whatsappMask'

// Stored numbers are E.164 (+55…); the field edits the national number with the same mask
// the table and the registration forms show. The API accepts the formatted national value.
const toNational = (value: string) => formatBrazilWhatsApp(value.replace(/^\+55/, ''))

type FormValues = ProfessionalInput
const emptyValues: FormValues = { name: '', profession: '', whatsApp: '' }

export function ProfessionalForm({
  professional,
  pending,
  onCancel,
  onSubmit,
}: {
  professional: ProfessionalDto | null
  pending: boolean
  onCancel(): void
  onSubmit(values: FormValues): Promise<void>
}) {
  const [values, setValues] = useState<FormValues>(emptyValues)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setValues(professional
      ? { name: professional.name, profession: professional.profession, whatsApp: toNational(professional.whatsApp) }
      : emptyValues)
    setError(null)
  }, [professional])

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    try { await onSubmit(values) } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar o profissional.')
    }
  }

  return <form className="form-grid simple-form" onSubmit={submit}>
    <div className="fields-area full-fields">
      <label className="field-label span-2">Nome completo
        <input className="field-input" required maxLength={200} value={values.name}
          onChange={event => setValues(current => ({ ...current, name: event.target.value }))}
          placeholder="Ex.: Dra. Helena Martins" />
      </label>
      <label className="field-label">Profissão
        <input className="field-input" required maxLength={150} value={values.profession}
          onChange={event => setValues(current => ({ ...current, profession: event.target.value }))}
          placeholder="Ex.: Fisioterapeuta" />
      </label>
      <label className="field-label">WhatsApp
        <input className="field-input" required value={values.whatsApp}
          inputMode="tel" autoComplete="tel-national" maxLength={16}
          onChange={event => setValues(current => ({ ...current, whatsApp: formatBrazilWhatsApp(event.target.value) }))}
          placeholder="(69) 99999-9999" />
      </label>
    </div>
    {error && <p className="form-error" role="alert">{error}</p>}
    <div className="modal-actions span-all">
      <button className="ghost-button" type="button" onClick={onCancel} disabled={pending}>Cancelar</button>
      <button className="primary-button" type="submit" disabled={pending}>
        {pending ? 'Salvando…' : professional ? 'Salvar alterações' : 'Cadastrar profissional'}
      </button>
    </div>
  </form>
}
