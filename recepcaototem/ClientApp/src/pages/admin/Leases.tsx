import { CalendarDays, CheckCircle2, CircleAlert, Clock3, Plus } from 'lucide-react'
import { type FormEvent, useEffect, useMemo, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { Modal } from '../../components/Modal'
import { EmptyState, PageHeader, StatusBadge } from '../../components/PageElements'
import type { Lease } from '../../dev/mock'
import { useAppStore } from '../../dev/AppStore'

type Filter = 'all' | Lease['status']

export function Leases() {
  const { leases, professionals, rooms, addLease } = useAppStore()
  const location = useLocation()
  const requestedRoom = (location.state as { room?: string; newLease?: boolean } | null)?.room ?? ''
  const [filter, setFilter] = useState<Filter>('all')
  const [open, setOpen] = useState(false)
  const [saved, setSaved] = useState(false)
  const [form, setForm] = useState({ professionalId: '', room: requestedRoom, amount: '2500', dueDate: '2026-10-05', startDate: '2026-09-02', status: 'pending' as Lease['status'] })
  useEffect(() => { if ((location.state as { newLease?: boolean } | null)?.newLease) setOpen(true) }, [location.state])
  const filtered = useMemo(() => filter === 'all' ? leases : leases.filter((lease) => lease.status === filter), [leases, filter])
  const professionalFor = (id: string) => professionals.find((item) => item.id === id)
  const submit = (event: FormEvent) => { event.preventDefault(); addLease({ ...form, amount: Number(form.amount) }); setSaved(true); window.setTimeout(() => setOpen(false), 700) }
  const counts = { paid: leases.filter((item) => item.status === 'paid').length, pending: leases.filter((item) => item.status === 'pending').length, overdue: leases.filter((item) => item.status === 'overdue').length }

  return (
    <div className="page-enter">
      <PageHeader eyebrow="Controle financeiro" title="Locações" description="Acompanhe contratos, valores e vencimentos mensais." action={<button className="primary-button" onClick={() => { setOpen(true); setSaved(false) }}><Plus size={18} /> Nova locação</button>} />
      <section className="lease-summary"><div><span className="lease-summary-icon green"><CheckCircle2 size={20} /></span><span><small>Pagas</small><strong>{counts.paid}</strong></span></div><div><span className="lease-summary-icon amber"><Clock3 size={20} /></span><span><small>Pendentes</small><strong>{counts.pending}</strong></span></div><div><span className="lease-summary-icon red"><CircleAlert size={20} /></span><span><small>Em atraso</small><strong>{counts.overdue}</strong></span></div></section>
      <section className="panel table-panel">
        <div className="filter-tabs" role="tablist">{([['all', 'Todas'], ['paid', 'Pagas'], ['pending', 'Pendentes'], ['overdue', 'Atrasadas']] as const).map(([value, label]) => <button className={filter === value ? 'active' : ''} onClick={() => setFilter(value)} key={value}>{label}{value !== 'all' && <span>{counts[value]}</span>}</button>)}</div>
        {filtered.length ? <div className="table-scroll"><table className="data-table"><thead><tr><th>Locatário</th><th>Sala</th><th>Valor mensal</th><th>Próximo vencimento</th><th>Situação</th></tr></thead><tbody>{filtered.map((lease) => { const professional = professionalFor(lease.professionalId); return <tr key={lease.id}><td><div className="person-cell"><img src={professional?.photo} alt="" /><span><strong>{professional?.name}</strong><small>{professional?.profession}</small></span></div></td><td><span className="room-tag">Sala {lease.room}</span></td><td className="money-cell">{lease.amount.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}</td><td><span className="date-cell"><CalendarDays size={17} />{new Date(`${lease.dueDate}T12:00:00`).toLocaleDateString('pt-BR')}</span></td><td><StatusBadge status={lease.status} /></td></tr>})}</tbody></table></div> : <EmptyState>Não há locações com este status.</EmptyState>}
      </section>

      <Modal open={open} onClose={() => setOpen(false)} title="Nova locação" subtitle="Vincule um profissional a uma sala disponível." size="large"><form className="simple-form" onSubmit={submit}><div className="fields-area full-fields"><label className="field-label span-2">Locatário<select className="field-input" required value={form.professionalId} onChange={(event) => setForm({ ...form, professionalId: event.target.value })}><option value="">Selecione o profissional</option>{professionals.filter((item) => item.active).map((item) => <option key={item.id} value={item.id}>{item.name} · {item.profession}</option>)}</select></label><label className="field-label">Sala<select className="field-input" required value={form.room} onChange={(event) => setForm({ ...form, room: event.target.value })}><option value="">Selecione</option>{rooms.filter((room) => room.status === 'available').map((room) => <option key={room.number} value={room.number}>Sala {room.number} · {room.floor}</option>)}</select></label><label className="field-label">Valor mensal (R$)<input className="field-input" type="number" min="1" required value={form.amount} onChange={(event) => setForm({ ...form, amount: event.target.value })} /></label><label className="field-label">Início da locação<input className="field-input" type="date" required value={form.startDate} onChange={(event) => setForm({ ...form, startDate: event.target.value })} /></label><label className="field-label">Próximo vencimento<input className="field-input" type="date" required value={form.dueDate} onChange={(event) => setForm({ ...form, dueDate: event.target.value })} /></label><label className="field-label span-2">Situação inicial<select className="field-input" value={form.status} onChange={(event) => setForm({ ...form, status: event.target.value as Lease['status'] })}><option value="paid">Pago</option><option value="pending">Pendente</option><option value="overdue">Atrasado</option></select></label></div><div className="modal-actions"><button className="ghost-button" type="button" onClick={() => setOpen(false)}>Cancelar</button><button className="primary-button" type="submit">{saved ? 'Locação registrada!' : 'Registrar locação'}</button></div></form></Modal>
    </div>
  )
}
