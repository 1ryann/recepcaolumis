import { ArrowRight, Building, CalendarDays, CircleDollarSign, DoorOpen, Phone, Plus, UserRound } from 'lucide-react'
import { useState } from 'react'
import type { Room } from '../../data/mock'
import { useAppStore } from '../../store/AppStore'
import { Modal } from '../../components/Modal'
import { PageHeader, StatusBadge } from '../../components/PageElements'
import { useNavigate } from 'react-router-dom'

export function Rooms() {
  const { rooms, professionals, leases } = useAppStore()
  const [selected, setSelected] = useState<Room | null>(null)
  const navigate = useNavigate()
  const professional = selected?.professionalId ? professionals.find((item) => item.id === selected.professionalId) : undefined
  const lease = selected ? leases.find((item) => item.room === selected.number) : undefined
  const occupied = rooms.filter((room) => room.status === 'occupied').length

  return (
    <div className="page-enter">
      <PageHeader eyebrow="Mapa do edifício" title="Salas" description="Visualize a ocupação e os detalhes de cada espaço." action={<button className="secondary-button" onClick={() => navigate('/admin/locacoes')}><Plus size={18} /> Nova locação</button>} />
      <div className="room-summary"><span><i className="legend-dot occupied" />{occupied} ocupadas</span><span><i className="legend-dot available" />{rooms.length - occupied} disponíveis</span><b>{rooms.length} salas no total</b></div>
      <section className="rooms-grid">{rooms.map((room) => { const tenant = room.professionalId ? professionals.find((item) => item.id === room.professionalId) : undefined; return <button className={`room-card room-${room.status}`} key={room.number} onClick={() => setSelected(room)} type="button"><span className="room-card-top"><span className="room-door"><DoorOpen size={22} /></span><StatusBadge status={room.status} /></span><span className="room-number"><small>Sala</small><strong>{room.number}</strong></span><span className="room-tenant">{tenant ? <><img src={tenant.photo} alt="" /><span><strong>{tenant.name}</strong><small>{tenant.profession}</small></span></> : <><span className="available-icon"><Plus size={18} /></span><span><strong>Disponível para locação</strong><small>{room.floor}</small></span></>}</span><span className="room-card-footer"><span>{room.floor}</span><ArrowRight size={17} /></span></button>})}</section>

      <Modal open={!!selected} onClose={() => setSelected(null)} title={selected ? `Sala ${selected.number}` : 'Sala'} subtitle={selected?.floor}>
        {selected?.status === 'occupied' && professional && lease ? <div className="room-detail"><div className="tenant-profile"><img src={professional.photo} alt={`Foto de ${professional.name}`} /><span><small>Profissional responsável</small><strong>{professional.name}</strong><p>{professional.profession}</p></span></div><div className="detail-grid"><div><Phone size={18} /><span><small>Telefone</small><strong>{professional.phone}</strong></span></div><div><CircleDollarSign size={18} /><span><small>Valor do aluguel</small><strong>{lease.amount.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}</strong></span></div><div><CalendarDays size={18} /><span><small>Próximo vencimento</small><strong>{new Date(`${lease.dueDate}T12:00:00`).toLocaleDateString('pt-BR')}</strong></span></div><div><Building size={18} /><span><small>Início da locação</small><strong>{new Date(`${lease.startDate}T12:00:00`).toLocaleDateString('pt-BR')}</strong></span></div></div><div className="payment-row"><span><small>Status do pagamento</small><strong>Referente ao mês atual</strong></span><StatusBadge status={lease.status} /></div><button className="secondary-button full-button" onClick={() => { setSelected(null); navigate('/admin/locacoes') }}>Ver detalhes da locação <ArrowRight size={17} /></button></div> : <div className="available-detail"><span className="available-detail-icon"><DoorOpen size={34} /></span><h3>Esta sala está disponível</h3><p>Registre uma nova locação para vincular um profissional a este espaço.</p><button className="primary-button" onClick={() => { setSelected(null); navigate('/admin/locacoes', { state: { newLease: true, room: selected?.number } }) }}><Plus size={18} /> Registrar nova locação</button></div>}
      </Modal>
    </div>
  )
}
