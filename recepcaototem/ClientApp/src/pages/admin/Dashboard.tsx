import { ArrowRight, CalendarClock, CircleDollarSign, DoorOpen, Sparkles, UserRoundCheck, Users } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useAppStore } from '../../dev/AppStore'
import { PageHeader, StatusBadge } from '../../components/PageElements'

export function Dashboard() {
  const { rooms, professionals, visits, leases } = useAppStore()
  const occupied = rooms.filter((room) => room.status === 'occupied').length
  const today = new Date().toISOString().slice(0, 10)
  const todayVisits = visits.filter((visit) => visit.date === today).length || visits.filter((visit) => visit.date === '2026-09-02').length
  const dueSoon = leases.filter((lease) => lease.status !== 'paid').length
  const professionalFor = (id: string) => professionals.find((item) => item.id === id)
  const greeting = new Date().getHours() < 12 ? 'Bom dia' : new Date().getHours() < 18 ? 'Boa tarde' : 'Boa noite'

  const metrics = [
    { label: 'Salas ocupadas', value: occupied, detail: `${rooms.length} salas no total`, icon: DoorOpen, tone: 'green' },
    { label: 'Salas disponíveis', value: rooms.length - occupied, detail: 'Prontas para locação', icon: Sparkles, tone: 'blue' },
    { label: 'Profissionais', value: professionals.filter((item) => item.active).length, detail: 'Ativos no edifício', icon: Users, tone: 'navy' },
    { label: 'Visitantes hoje', value: todayVisits, detail: 'Entradas registradas', icon: UserRoundCheck, tone: 'amber' },
  ]

  return (
    <div className="page-enter">
      <PageHeader eyebrow="Hoje, 2 de setembro" title={`${greeting}, Administrador`} description="Aqui está um resumo do movimento do LUMIS hoje." />
      <section className="metrics-grid">{metrics.map(({ label, value, detail, icon: Icon, tone }) => <article className="metric-card" key={label}><span className={`metric-icon tone-${tone}`}><Icon size={21} /></span><div><small>{label}</small><strong>{value}</strong><p>{detail}</p></div></article>)}</section>
      <section className="dashboard-grid">
        <article className="panel recent-panel">
          <div className="panel-header"><div><h2>Visitas recentes</h2><p>Últimas pessoas que passaram pela recepção</p></div><Link to="/admin/visitas" className="text-link">Ver histórico <ArrowRight size={16} /></Link></div>
          <div className="recent-list">{visits.slice(0, 5).map((visit) => { const professional = professionalFor(visit.professionalId); return <div className="recent-item" key={visit.id}><div className="visit-time"><strong>{visit.time}</strong><small>{visit.date === '2026-09-02' || visit.date === today ? 'Hoje' : 'Ontem'}</small></div><div className="visitor-avatar">{visit.visitor.split(' ').map((part) => part[0]).slice(0, 2).join('')}</div><div className="visit-copy"><strong>{visit.visitor}</strong><span>Visitou {professional?.name}</span></div><span className="room-tag">Sala {visit.room}</span></div>})}</div>
        </article>
        <div className="dashboard-side">
          <article className="panel due-panel"><div className="panel-header"><div><h2>Próximos vencimentos</h2><p>Acompanhe os aluguéis</p></div><span className="count-bubble">{dueSoon}</span></div><div className="due-list">{leases.filter((lease) => lease.status !== 'paid').slice(0, 3).map((lease) => <div key={lease.id}><span className="due-date"><CalendarClock size={17} />{new Date(`${lease.dueDate}T12:00:00`).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })}</span><span><strong>{professionalFor(lease.professionalId)?.name}</strong><small>Sala {lease.room} · {lease.amount.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}</small></span><StatusBadge status={lease.status} /></div>)}</div><Link to="/admin/locacoes" className="panel-link">Ver todas as locações <ArrowRight size={16} /></Link></article>
          <article className="occupancy-card"><div><span><CircleDollarSign size={20} /></span><p>Ocupação do edifício</p><strong>{Math.round((occupied / rooms.length) * 100)}%</strong></div><div className="occupancy-bar"><i style={{ width: `${(occupied / rooms.length) * 100}%` }} /></div><small>{occupied} de {rooms.length} salas estão locadas</small></article>
        </div>
      </section>
    </div>
  )
}
