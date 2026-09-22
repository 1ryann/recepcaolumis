import { AlertTriangle } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { ApiError } from '../../api/client'
import { professionalIncidentsApi, type ProfessionalIncidentType } from '../../api/modules'
import { Modal } from '../../components/Modal'

const REASON_LIMIT = 300
const options: { value: ProfessionalIncidentType; label: string; hint: string }[] = [
  { value: 'NEXT_APPOINTMENT', label: 'Só o próximo atendimento', hint: 'Cancela apenas o seu próximo atendimento de hoje.' },
  { value: 'UNTIL_TIME', label: 'Até um horário', hint: 'Cancela os atendimentos de agora até o horário informado.' },
  { value: 'REST_OF_DAY', label: 'O resto do dia', hint: 'Cancela todos os seus atendimentos restantes de hoje.' },
]

function resultMessage(count: number) {
  if (count === 0) return 'Imprevisto registrado. Nenhum atendimento de hoje foi afetado.'
  return count === 1
    ? 'Imprevisto registrado. 1 atendimento foi cancelado e o cliente foi avisado por WhatsApp.'
    : `Imprevisto registrado. ${count} atendimentos foram cancelados e os clientes foram avisados por WhatsApp.`
}

export function ReportIncident({ onReported }: { onReported?: () => void }) {
  const [open, setOpen] = useState(false)
  const [type, setType] = useState<ProfessionalIncidentType>('NEXT_APPOINTMENT')
  const [untilTime, setUntilTime] = useState('')
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState('')

  const openDialog = () => { setType('NEXT_APPOINTMENT'); setUntilTime(''); setReason(''); setError(''); setResult(''); setOpen(true) }
  const close = () => { if (!saving) setOpen(false) }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (type === 'UNTIL_TIME' && !untilTime) { setError('Informe até que horas você ficará indisponível.'); return }
    setSaving(true)
    setError('')
    try {
      const response = await professionalIncidentsApi.report({
        type, untilTime: type === 'UNTIL_TIME' ? untilTime : null, reason: reason.trim() || null,
      })
      setResult(resultMessage(response.affectedReservationIds.length))
      onReported?.()
    } catch (failure) {
      setError(failure instanceof ApiError && failure.status === 400 ? failure.message : 'Não foi possível registrar o imprevisto. Tente novamente.')
    } finally {
      setSaving(false)
    }
  }

  return <>
    <button className="secondary-button professional-incident-trigger" type="button" onClick={openDialog}><AlertTriangle size={16} /> Registrar imprevisto</button>
    <Modal open={open} onClose={close} title="Registrar imprevisto" subtitle="Os clientes afetados recebem um aviso por WhatsApp com um link para escolher um novo horário.">
      {result
        ? <div className="simple-form">
            <p role="status">{result}</p>
            <div className="modal-actions"><button className="primary-button" type="button" onClick={() => setOpen(false)}>Fechar</button></div>
          </div>
        : <form className="simple-form" onSubmit={submit}>
            <fieldset className="professional-incident-options">
              <legend className="field-label">O que será afetado?</legend>
              {options.map(option => <label className="professional-incident-option" key={option.value}>
                <input type="radio" name="incident-type" value={option.value} checked={type === option.value} onChange={() => setType(option.value)} />
                <span><strong>{option.label}</strong><small>{option.hint}</small></span>
              </label>)}
            </fieldset>
            {type === 'UNTIL_TIME' && <label className="field-label">Indisponível até<input className="field-input" type="time" required value={untilTime} onChange={event => setUntilTime(event.target.value)} /></label>}
            <label className="field-label">Motivo (opcional)<textarea className="field-input" maxLength={REASON_LIMIT} rows={3} value={reason} onChange={event => setReason(event.target.value)} /></label>
            {error && <p className="form-error" role="alert">{error}</p>}
            <div className="modal-actions">
              <button className="ghost-button" type="button" onClick={close} disabled={saving}>Voltar</button>
              <button className="primary-button" type="submit" disabled={saving}>{saving ? 'Registrando…' : 'Confirmar imprevisto'}</button>
            </div>
          </form>}
    </Modal>
  </>
}
