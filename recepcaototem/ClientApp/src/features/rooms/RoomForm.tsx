import { type FormEvent, useEffect, useState } from 'react'
import type { RoomAmenity, RoomCategory, RoomDto, RoomInput } from '../../api/modules'
import { parseRoomRate } from './money'
import { ROOM_AMENITIES, ROOM_CATEGORIES, amenityLabel, categoryLabel } from './roomFeatures'
import '../../styles.room-form.css'

// Name and the two rates are required, as they always were. Everything the public
// catalogue advertises — the monthly price, the size, the capacity, the category and the
// comforts — is optional: a room gets registered the day it exists and described later,
// once someone has measured it and been to the site with a camera. A blank field is
// stored as "unknown" and simply does not appear on the catalogue.
type FormValues = {
  name: string
  description: string
  hourlyRate: string
  dailyRate: string
  monthlyRate: string
  areaSquareMeters: string
  bathroomCount: string
  capacityMin: string
  capacityMax: string
  category: RoomCategory | ''
  amenities: RoomAmenity[]
}

const emptyValues: FormValues = {
  name: '', description: '', hourlyRate: '', dailyRate: '', monthlyRate: '',
  areaSquareMeters: '', bathroomCount: '', capacityMin: '', capacityMax: '',
  category: '', amenities: [],
}

function rateInput(value: number) {
  return value.toLocaleString('pt-BR', { useGrouping: false, minimumFractionDigits: 0, maximumFractionDigits: 2 })
}

function optionalRateInput(value: number | null) {
  return value === null ? '' : rateInput(value)
}

function optionalCountInput(value: number | null) {
  return value === null ? '' : String(value)
}

/** A blank field means "not informed" and is sent as null, never as zero. */
function parseOptionalRate(raw: string): number | null | 'invalid' {
  if (raw.trim() === '') return null
  const parsed = parseRoomRate(raw)
  return parsed === null ? 'invalid' : parsed
}

function parseOptionalCount(raw: string, maximum: number): number | null | 'invalid' {
  if (raw.trim() === '') return null
  if (!/^\d{1,4}$/.test(raw.trim())) return 'invalid'
  const parsed = Number(raw.trim())
  return parsed > maximum ? 'invalid' : parsed
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
    setValues(room ? {
      name: room.name,
      description: room.description ?? '',
      hourlyRate: rateInput(room.hourlyRate),
      dailyRate: rateInput(room.dailyRate),
      monthlyRate: optionalRateInput(room.monthlyRate),
      areaSquareMeters: optionalRateInput(room.areaSquareMeters),
      bathroomCount: optionalCountInput(room.bathroomCount),
      capacityMin: optionalCountInput(room.capacityMin),
      capacityMax: optionalCountInput(room.capacityMax),
      category: room.category ?? '',
      amenities: room.amenities ?? [],
    } : emptyValues)
    setError(null)
  }, [room])

  const set = <K extends keyof FormValues>(key: K, value: FormValues[K]) =>
    setValues(current => ({ ...current, [key]: value }))

  const toggleAmenity = (amenity: RoomAmenity) =>
    setValues(current => ({
      ...current,
      amenities: current.amenities.includes(amenity)
        ? current.amenities.filter(item => item !== amenity)
        : [...current.amenities, amenity],
    }))

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const hourlyRate = parseRoomRate(values.hourlyRate)
    const dailyRate = parseRoomRate(values.dailyRate)
    if (hourlyRate === null || dailyRate === null) {
      setError('Informe tarifas brasileiras válidas, sem mais de duas casas decimais.'); return
    }

    const monthlyRate = parseOptionalRate(values.monthlyRate)
    const areaSquareMeters = parseOptionalRate(values.areaSquareMeters)
    if (monthlyRate === 'invalid' || areaSquareMeters === 'invalid') {
      setError('O valor mensal e a área precisam ser números válidos, com até duas casas decimais.'); return
    }
    if (areaSquareMeters !== null && areaSquareMeters <= 0) {
      setError('A área precisa ser maior que zero, ou ficar em branco.'); return
    }

    const bathroomCount = parseOptionalCount(values.bathroomCount, 20)
    const capacityMin = parseOptionalCount(values.capacityMin, 200)
    const capacityMax = parseOptionalCount(values.capacityMax, 200)
    if (bathroomCount === 'invalid' || capacityMin === 'invalid' || capacityMax === 'invalid') {
      setError('Banheiros e capacidade precisam ser números inteiros dentro do limite.'); return
    }
    // A maximum on its own says nothing, and a maximum below the minimum is a typo.
    if (capacityMax !== null && capacityMin === null) {
      setError('Informe a capacidade mínima antes da máxima.'); return
    }
    if (capacityMin !== null && capacityMax !== null && capacityMax < capacityMin) {
      setError('A capacidade máxima não pode ser menor que a mínima.'); return
    }
    if (capacityMin !== null && capacityMin < 1) {
      setError('A capacidade mínima precisa ser de pelo menos uma pessoa.'); return
    }

    try {
      setError(null)
      await onSubmit({
        name: values.name,
        description: values.description.trim() || null,
        hourlyRate,
        dailyRate,
        monthlyRate,
        areaSquareMeters,
        bathroomCount,
        capacityMin,
        capacityMax,
        category: values.category === '' ? null : values.category,
        amenities: values.amenities,
      })
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Não foi possível salvar a sala.') }
  }

  return <form className="form-grid simple-form" onSubmit={submit}>
    <div className="fields-area full-fields">
      <label className="field-label span-2">Nome da sala
        <input className="field-input" required maxLength={100} value={values.name} onChange={event => set('name', event.target.value)} placeholder="Ex.: Sala 101" />
      </label>
      <label className="field-label span-2">Descrição
        <textarea className="field-input field-textarea" maxLength={500} value={values.description} onChange={event => set('description', event.target.value)} placeholder="Informação opcional sobre o espaço" />
      </label>
      <label className="field-label">Tarifa por hora
        <input className="field-input" required inputMode="decimal" value={values.hourlyRate} onChange={event => set('hourlyRate', event.target.value)} placeholder="0,00" />
      </label>
      <label className="field-label">Tarifa diária
        <input className="field-input" required inputMode="decimal" value={values.dailyRate} onChange={event => set('dailyRate', event.target.value)} placeholder="0,00" />
      </label>

      <p className="field-section span-2">Divulgação no catálogo <small>Tudo opcional — o que ficar em branco não aparece.</small></p>

      <label className="field-label">Valor mensal
        <input className="field-input" inputMode="decimal" value={values.monthlyRate} onChange={event => set('monthlyRate', event.target.value)} placeholder="Ex.: 3.100,00" />
      </label>
      <label className="field-label">Área (m²)
        <input className="field-input" inputMode="decimal" value={values.areaSquareMeters} onChange={event => set('areaSquareMeters', event.target.value)} placeholder="Ex.: 25" />
      </label>
      <label className="field-label">Banheiros
        <input className="field-input" inputMode="numeric" value={values.bathroomCount} onChange={event => set('bathroomCount', event.target.value)} placeholder="Ex.: 1" />
      </label>
      <label className="field-label">Categoria
        <select className="field-input" value={values.category} onChange={event => set('category', event.target.value as RoomCategory | '')}>
          <option value="">Não informada</option>
          {ROOM_CATEGORIES.map(category => (
            <option key={category} value={category}>{categoryLabel(category)}</option>
          ))}
        </select>
      </label>
      <label className="field-label">Capacidade mínima
        <input className="field-input" inputMode="numeric" value={values.capacityMin} onChange={event => set('capacityMin', event.target.value)} placeholder="Ex.: 4" />
      </label>
      <label className="field-label">Capacidade máxima
        <input className="field-input" inputMode="numeric" value={values.capacityMax} onChange={event => set('capacityMax', event.target.value)} placeholder="Ex.: 8" />
      </label>

      <fieldset className="field-checks span-2">
        <legend>Comodidades</legend>
        {ROOM_AMENITIES.map(amenity => (
          <label className="field-check" key={amenity}>
            <input
              type="checkbox"
              checked={values.amenities.includes(amenity)}
              onChange={() => toggleAmenity(amenity)}
            />
            {amenityLabel(amenity)}
          </label>
        ))}
      </fieldset>
    </div>
    {error && <p className="form-error" role="alert">{error}</p>}
    <div className="modal-actions span-all"><button className="ghost-button" type="button" onClick={onCancel} disabled={pending}>Cancelar</button><button className="primary-button" type="submit" disabled={pending}>{pending ? 'Salvando…' : room ? 'Salvar alterações' : 'Cadastrar sala'}</button></div>
  </form>
}
