import { CalendarDays, Download, Search, UserRoundSearch } from 'lucide-react'
import { useMemo, useState } from 'react'
import { EmptyState, PageHeader } from '../../components/PageElements'
import { useAppStore } from '../../dev/AppStore'

export function Visits() {
  const { visits, professionals } = useAppStore()
  const [query, setQuery] = useState('')
  const filtered = useMemo(() => visits.filter((visit) => { const professional = professionals.find((item) => item.id === visit.professionalId); return `${visit.visitor} ${professional?.name} ${visit.room}`.toLowerCase().includes(query.toLowerCase()) }), [visits, professionals, query])
  const professionalFor = (id: string) => professionals.find((item) => item.id === id)
  const exportVisits = () => {
    const rows = [['Data', 'Horário', 'Visitante', 'Profissional', 'Sala'], ...filtered.map((visit) => [visit.date, visit.time, visit.visitor, professionalFor(visit.professionalId)?.name ?? '', visit.room])]
    const csv = rows.map((row) => row.map((value) => `"${value}"`).join(';')).join('\n')
    const link = document.createElement('a'); link.href = URL.createObjectURL(new Blob([`\ufeff${csv}`], { type: 'text/csv;charset=utf-8' })); link.download = 'visitas-lumis.csv'; link.click(); URL.revokeObjectURL(link.href)
  }

  return (
    <div className="page-enter">
      <PageHeader eyebrow="Histórico de entradas" title="Visitas" description="Consulte todas as chegadas registradas pela recepção." action={<button className="secondary-button" onClick={exportVisits}><Download size={18} /> Exportar lista</button>} />
      <section className="panel table-panel">
        <div className="table-toolbar visits-toolbar"><div className="search-field"><Search size={18} /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Buscar visitante ou profissional" aria-label="Buscar visitas" /></div><div className="date-filter"><CalendarDays size={17} /><span>Últimos 30 dias</span></div></div>
        {filtered.length ? <div className="table-scroll"><table className="data-table"><thead><tr><th>Data</th><th>Horário</th><th>Visitante</th><th>Profissional visitado</th><th>Sala</th></tr></thead><tbody>{filtered.map((visit) => { const professional = professionalFor(visit.professionalId); return <tr key={visit.id}><td><span className="date-main">{new Date(`${visit.date}T12:00:00`).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short', year: 'numeric' })}</span></td><td><span className="time-pill">{visit.time}</span></td><td><div className="visitor-name-cell"><span>{visit.visitor.split(' ').map((part) => part[0]).slice(0, 2).join('')}</span><strong>{visit.visitor}</strong></div></td><td><div className="person-cell compact"><img src={professional?.photo} alt="" /><span><strong>{professional?.name}</strong><small>{professional?.profession}</small></span></div></td><td><span className="room-tag">Sala {visit.room}</span></td></tr>})}</tbody></table></div> : <EmptyState><UserRoundSearch size={30} />Nenhuma visita encontrada para “{query}”.</EmptyState>}
      </section>
    </div>
  )
}
