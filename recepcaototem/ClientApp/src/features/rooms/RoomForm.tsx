import { type FormEvent, useEffect, useState } from 'react'
import type { RoomDto, RoomInput } from '../../api/modules'
import { parseRoomRate } from './money'

type FormValues = { name: string, description: string, hourlyRate: string, dailyRate: string }
const emptyValues: FormValues = { name: '', description: '', hourlyRate: '', dailyRate: '' }

function rateInput(value: number) {
  return value.toLocaleString('pt-BR', { useGrouping: false, minimumFractionDigits: 0, maximumFractionDigits: 2 })
}

export function RoomForm({ room, pending, onCancel, onSubmit }: {
  room: RoomDto | null
  pending: boolean
  onCancel(): void
  onSubmit(input: RoomInput): Promise<void>
}) {
  const [values, setValues] = useState<FormValues>(emptyValues)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    setValues(room ? { name: room.name, description: room.description ?? '', hourlyRate: rateInput(room.hourlyRate), dailyRate: rateInput(room.dailyRate) } : emptyValues)
    setError(null)
  }, [room])
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const hourlyRate = parseRoomRate(values.hourlyRate)
    const dailyRate = parseRoomRate(values.dailyRate)
    if (hourlyRate === null || dailyRate === null) {
      setError('Informe tarifas brasileiras válidas, sem mais de duas casas decimais.'); return
    }
    try {
      setError(null)
      await onSubmit({ name: values.name, description: values.description.trim() || null, hourlyRate, dailyRate })
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Não foi possível salvar a sala.') }
  }
  return <form className="form-grid simple-form" onSubmit={submit}>
    <div className="fields-area full-fields">
      <label className="field-label span-2">Nome da sala
        <input className="field-input" required maxLength={100} value={values.name} onChange={event => setValues(current => ({ ...current, name: event.target.value }))} placeholder="Ex.: Sala 101" />
      </label>
      <label className="field-label span-2">Descrição
        <textarea className="field-input field-textarea" maxLength={500} value={values.description} onChange={event => setValues(current => ({ ...current, description: event.target.value }))} placeholder="Informação opcional sobre o espaço" />
      </label>
      <label className="field-label">Tarifa por hora
        <input className="field-input" required inputMode="decimal" value={values.hourlyRate} onChange={event => setValues(current => ({ ...current, hourlyRate: event.target.value }))} placeholder="0,00" />
      </label>
      <label className="field-label">Tarifa diária
        <input className="field-input" required inputMode="decimal" value={values.dailyRate} onChange={event => setValues(current => ({ ...current, dailyRate: event.target.value }))} placeholder="0,00" />
      </label>
    </div>
    {error && <p className="form-error" role="alert">{error}</p>}
    <div className="modal-actions span-all"><button className="ghost-button" type="button" onClick={onCancel} disabled={pending}>Cancelar</button><button className="primary-button" type="submit" disabled={pending}>{pending ? 'Salvando…' : room ? 'Salvar alterações' : 'Cadastrar sala'}</button></div>
  </form>
}
