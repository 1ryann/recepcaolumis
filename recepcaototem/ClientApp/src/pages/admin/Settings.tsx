import { CalendarClock, Check, DoorOpen, Plus } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { operatingHoursApi, roomBlocksApi, roomsApi, type OperatingHoursDayDto, type OperatingHoursDto, type RoomBlockDto, type RoomDto } from '../../api/modules'
import { PageHeader } from '../../components/PageElements'
import { OperatingHoursEditor } from '../../features/availability/OperatingHoursEditor'

type RoomBlockDraft = { roomId: string, startAt: string, endAt: string, reason: string }

const localDateTimeToIso = (value: string) => value ? new Date(value).toISOString() : ''
const errorText = (reason: unknown) => {
  if (reason instanceof ApiError) {
    if (reason.code === 'RESOURCE_MODIFIED') return 'Este registro foi alterado em outra sessão. Atualizamos os dados para você.'
    if (reason.code === 'OPERATING_HOURS_CONFLICT') return 'O novo horário deixaria uma reserva ou ocupação válida fora do funcionamento. Ajuste os períodos e tente novamente.'
    return reason.message
  }
  return reason instanceof Error ? reason.message : 'Não foi possível concluir a operação.'
}

export function Settings() {
  const [hours, setHours] = useState<OperatingHoursDto | null>(null)
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
    setRooms(roomResponse.items)
    setBlocks(blockResponse.items)
    setBlockDraft((current) => ({ ...current, roomId: current.roomId || roomResponse.items[0]?.id || '' }))
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal).catch((reason) => { if (!controller.signal.aborted) setError(errorText(reason)) }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [load])

  const saveHours = async (days: OperatingHoursDayDto[]) => {
    if (!hours) return
    setPending(true); setError(''); setNotice('')
    try {
      const updated = await operatingHoursApi.update({ days, concurrencyToken: hours.concurrencyToken })
      setHours(updated); setNotice('Horário do estabelecimento atualizado.')
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

  return <div className="page-enter settings-page"><PageHeader eyebrow="Operação" title="Configurações" description="Defina o horário do estabelecimento e as indisponibilidades das salas." />{error && <div className="form-error" role="alert">{error}</div>}{notice && <div className="availability-success" role="status"><Check size={15} /> {notice}</div>}{loading ? <div className="empty-state" role="status">Carregando configurações…</div> : <div className="settings-real-grid"><section className="operating-hours-page panel settings-card"><div className="settings-title"><span><CalendarClock size={20} /></span><div><h2>Horário do estabelecimento</h2><p>O expediente que orienta os novos agendamentos.</p></div></div>{hours && <OperatingHoursEditor value={hours} pending={pending} onSave={saveHours} />}</section><section className="room-blocks-panel panel settings-card"><div className="settings-title"><span><DoorOpen size={20} /></span><div><h2>Bloqueios de salas</h2><p>Reserve períodos para manutenção, limpeza ou uso interno.</p></div></div><div className="room-blocks-toolbar"><label className="field-label">Sala<select className="field-input" aria-label="Sala" value={blockDraft.roomId} onChange={(event) => setBlockDraft((current) => ({ ...current, roomId: event.target.value }))}><option value="">Selecione</option>{rooms.map((room) => <option key={room.id} value={room.id}>{room.name}</option>)}</select></label><label className="field-label">Início<input className="field-input" aria-label="Início" type="datetime-local" value={blockDraft.startAt} onChange={(event) => setBlockDraft((current) => ({ ...current, startAt: event.target.value }))} /></label><label className="field-label">Fim<input className="field-input" aria-label="Fim" type="datetime-local" value={blockDraft.endAt} onChange={(event) => setBlockDraft((current) => ({ ...current, endAt: event.target.value }))} /></label><label className="field-label">Motivo<input className="field-input" aria-label="Motivo" value={blockDraft.reason} onChange={(event) => setBlockDraft((current) => ({ ...current, reason: event.target.value }))} /></label><button className="primary-button" type="button" disabled={pending} onClick={() => void createBlock()}><Plus size={16} /> Criar bloqueio</button></div>{blocks.length === 0 ? <div className="availability-empty">Nenhum bloqueio ativo.</div> : <div className="room-block-list">{blocks.map((block) => <article className="room-block-row" key={block.id}><div><strong>{block.roomName}</strong><span>{new Date(block.startAt).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })} – {new Date(block.endAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</span></div><small>{block.reason}</small><div className="row-actions">{block.status === 'ACTIVE' ? <button type="button" disabled={pending} onClick={() => void cancelBlock(block)}>Cancelar</button> : <span className="customer-status status-cancelled">Cancelado</span>}</div></article>)}</div>}</section></div>}</div>
}
