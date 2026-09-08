import { CalendarClock, Check, Clock3, DoorOpen, Plus, Save, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { operatingHoursApi, roomBlocksApi, roomsApi, type OperatingHoursDayDto, type OperatingHoursDto, type RoomBlockDto, type RoomDto } from '../../api/modules'
import { PageHeader } from '../../components/PageElements'
import { AVAILABILITY_DAYS, findDayLabel, toTimeInputValue } from '../../features/availability/availabilityFormat'

type OperatingDraftDay = { dayOfWeek: string, intervals: { opensAt: string, closesAt: string }[] }
type RoomBlockDraft = { roomId: string, startAt: string, endAt: string, reason: string }

const emptyHours = (): OperatingDraftDay[] => AVAILABILITY_DAYS.map(({ value }) => ({ dayOfWeek: value, intervals: [] }))
const normalizeHours = (days: OperatingHoursDayDto[] | undefined) => AVAILABILITY_DAYS.map(({ value }) => {
  const source = days?.find((day) => day.dayOfWeek.toUpperCase() === value)
  return { dayOfWeek: value, intervals: (source?.intervals ?? []).map((interval) => ({ opensAt: toTimeInputValue(interval.opensAt), closesAt: toTimeInputValue(interval.closesAt) })) }
})
const localDateTimeToIso = (value: string) => value ? new Date(value).toISOString() : ''
const errorText = (reason: unknown) => reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED'
  ? 'Este registro foi alterado em outra sessão. Atualizamos os dados para você.'
  : reason instanceof Error ? reason.message : 'Não foi possível concluir a operação.'

export function Settings() {
  const [hours, setHours] = useState<OperatingHoursDto | null>(null)
  const [hoursDraft, setHoursDraft] = useState<OperatingDraftDay[]>(emptyHours)
  const [rooms, setRooms] = useState<RoomDto[]>([])
  const [blocks, setBlocks] = useState<RoomBlockDto[]>([])
  const [blockDraft, setBlockDraft] = useState<RoomBlockDraft>({ roomId: '', startAt: '', endAt: '', reason: '' })
  const [loading, setLoading] = useState(true)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const load = useCallback(async (signal?: AbortSignal) => {
    setError('')
    const [hoursResponse, roomResponse, blockResponse] = await Promise.all([
      operatingHoursApi.get(signal),
      roomsApi.list({ search: undefined, status: 'active', page: 1, pageSize: 100 }, signal),
      roomBlocksApi.list({ status: 'ACTIVE', page: 1, pageSize: 100 }, signal),
    ])
    setHours(hoursResponse)
    setHoursDraft(normalizeHours(hoursResponse.days))
    setRooms(roomResponse.items)
    setBlocks(blockResponse.items)
    setBlockDraft((current) => ({ ...current, roomId: current.roomId || roomResponse.items[0]?.id || '' }))
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal).catch((reason) => { if (!controller.signal.aborted) setError(errorText(reason)) }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [load])

  const saveHours = async () => {
    if (!hours) return
    setPending(true); setError(''); setNotice('')
    try {
      const updated = await operatingHoursApi.update({ days: hoursDraft, concurrencyToken: hours.concurrencyToken })
      setHours(updated); setHoursDraft(normalizeHours(updated.days)); setNotice('Horário do estabelecimento atualizado.')
    } catch (reason) { setError(errorText(reason)); if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') await load() }
    finally { setPending(false) }
  }

  const createBlock = async () => {
    if (!blockDraft.roomId || !blockDraft.startAt || !blockDraft.endAt || !blockDraft.reason.trim()) { setError('Informe sala, período e motivo do bloqueio.'); return }
    setPending(true); setError(''); setNotice('')
    try {
      const created = await roomBlocksApi.create({ roomId: blockDraft.roomId, startAt: localDateTimeToIso(blockDraft.startAt), endAt: localDateTimeToIso(blockDraft.endAt), reason: blockDraft.reason.trim() })
      setBlocks((current) => [...current, created].sort((a, b) => a.startAt.localeCompare(b.startAt))); setBlockDraft((current) => ({ ...current, startAt: '', endAt: '', reason: '' })); setNotice('Bloqueio criado.')
    } catch (reason) { setError(errorText(reason)) }
    finally { setPending(false) }
  }

  const cancelBlock = async (block: RoomBlockDto) => {
    setPending(true); setError('')
    try { const cancelled = await roomBlocksApi.cancel(block.id, block.concurrencyToken); setBlocks((current) => current.map((item) => item.id === block.id ? cancelled : item)) }
    catch (reason) { setError(errorText(reason)); if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') await load() }
    finally { setPending(false) }
  }

  const updateHoursInterval = (dayOfWeek: string, index: number, field: 'opensAt' | 'closesAt', value: string) => setHoursDraft((current) => current.map((day) => day.dayOfWeek === dayOfWeek ? { ...day, intervals: day.intervals.map((interval, intervalIndex) => intervalIndex === index ? { ...interval, [field]: value } : interval) } : day))
  const addHoursInterval = (dayOfWeek: string) => setHoursDraft((current) => current.map((day) => day.dayOfWeek === dayOfWeek ? { ...day, intervals: [...day.intervals, { opensAt: '', closesAt: '' }] } : day))
  const removeHoursInterval = (dayOfWeek: string, index: number) => setHoursDraft((current) => current.map((day) => day.dayOfWeek === dayOfWeek ? { ...day, intervals: day.intervals.filter((_, intervalIndex) => intervalIndex !== index) } : day))

  return <div className="page-enter settings-page"><PageHeader eyebrow="Operação" title="Configurações" description="Defina o horário do estabelecimento e as indisponibilidades das salas." />{error && <div className="form-error" role="alert">{error}</div>}{notice && <div className="availability-success" role="status"><Check size={15} /> {notice}</div>}{loading ? <div className="empty-state" role="status">Carregando configurações…</div> : <div className="settings-real-grid"><section className="operating-hours-page panel settings-card"><div className="settings-title"><span><CalendarClock size={20} /></span><div><h2>Horário do estabelecimento</h2><p>O expediente que orienta os novos agendamentos.</p></div></div>{hours && !hours.configured && <div className="settings-unconfigured"><Clock3 size={18} /><div><strong>O horário de funcionamento ainda não foi configurado.</strong><span>Cadastre os intervalos abaixo para liberar horários aos clientes.</span></div></div>}<div className="operating-hours-grid">{hoursDraft.map((day) => <div className="operating-hours-row" key={day.dayOfWeek}><strong>{findDayLabel(day.dayOfWeek)}</strong><div className="operating-hours-intervals">{day.intervals.length === 0 && <span className="operating-hours-empty">Fechado</span>}{day.intervals.map((interval, index) => <div className="operating-hours-interval" key={`${day.dayOfWeek}-${index}`}><input aria-label={`${findDayLabel(day.dayOfWeek)} abertura`} className="field-input" type="time" value={interval.opensAt} onChange={(event) => updateHoursInterval(day.dayOfWeek, index, 'opensAt', event.target.value)} /><span>–</span><input aria-label={`${findDayLabel(day.dayOfWeek)} fechamento`} className="field-input" type="time" value={interval.closesAt} onChange={(event) => updateHoursInterval(day.dayOfWeek, index, 'closesAt', event.target.value)} /><button type="button" className="icon-button" aria-label={`Remover horário de ${findDayLabel(day.dayOfWeek)}`} onClick={() => removeHoursInterval(day.dayOfWeek, index)}><Trash2 size={15} /></button></div>)}<button type="button" className="availability-add-button" onClick={() => addHoursInterval(day.dayOfWeek)}><Plus size={14} /> Adicionar intervalo</button></div></div>)}</div><div className="settings-save"><span /> <button className="primary-button" type="button" disabled={pending} onClick={() => void saveHours()}><Save size={17} /> {pending ? 'Salvando…' : 'Salvar horário'}</button></div></section><section className="room-blocks-panel panel settings-card"><div className="settings-title"><span><DoorOpen size={20} /></span><div><h2>Bloqueios de salas</h2><p>Reserve períodos para manutenção, limpeza ou uso interno.</p></div></div><div className="room-blocks-toolbar"><label className="field-label">Sala<select className="field-input" aria-label="Sala" value={blockDraft.roomId} onChange={(event) => setBlockDraft((current) => ({ ...current, roomId: event.target.value }))}><option value="">Selecione</option>{rooms.map((room) => <option key={room.id} value={room.id}>{room.name}</option>)}</select></label><label className="field-label">Início<input className="field-input" aria-label="Início" type="datetime-local" value={blockDraft.startAt} onChange={(event) => setBlockDraft((current) => ({ ...current, startAt: event.target.value }))} /></label><label className="field-label">Fim<input className="field-input" aria-label="Fim" type="datetime-local" value={blockDraft.endAt} onChange={(event) => setBlockDraft((current) => ({ ...current, endAt: event.target.value }))} /></label><label className="field-label">Motivo<input className="field-input" aria-label="Motivo" value={blockDraft.reason} onChange={(event) => setBlockDraft((current) => ({ ...current, reason: event.target.value }))} /></label><button className="primary-button" type="button" disabled={pending} onClick={() => void createBlock()}><Plus size={16} /> Criar bloqueio</button></div>{blocks.length === 0 ? <div className="availability-empty">Nenhum bloqueio ativo.</div> : <div className="room-block-list">{blocks.map((block) => <article className="room-block-row" key={block.id}><div><strong>{block.roomName}</strong><span>{new Date(block.startAt).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })} – {new Date(block.endAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</span></div><small>{block.reason}</small><div className="row-actions">{block.status === 'ACTIVE' ? <button type="button" disabled={pending} onClick={() => void cancelBlock(block)}>Cancelar</button> : <span className="customer-status status-cancelled">Cancelado</span>}</div></article>)}</div>}</section></div>}</div>
}
