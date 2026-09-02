import { Eye, MoreHorizontal, Pencil, Plus, Search, UserMinus, UserPlus } from 'lucide-react'
import { type FormEvent, useMemo, useState } from 'react'
import { Modal } from '../../components/Modal'
import { EmptyState, PageHeader, StatusBadge } from '../../components/PageElements'
import type { Professional } from '../../data/mock'
import { useAppStore } from '../../store/AppStore'

const emptyForm = { name: '', profession: '', phone: '', room: '', photo: '', active: true }

export function Professionals() {
  const { professionals, rooms, saveProfessional, toggleProfessional } = useAppStore()
  const [query, setQuery] = useState('')
  const [editing, setEditing] = useState<Professional | null>(null)
  const [viewing, setViewing] = useState<Professional | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [form, setForm] = useState(emptyForm)
  const [saved, setSaved] = useState(false)
  const filtered = useMemo(() => professionals.filter((item) => `${item.name} ${item.profession} ${item.room}`.toLowerCase().includes(query.toLowerCase())), [professionals, query])

  const openNew = () => { setEditing(null); setForm(emptyForm); setFormOpen(true); setSaved(false) }
  const openEdit = (professional: Professional) => { setEditing(professional); setForm({ name: professional.name, profession: professional.profession, phone: professional.phone, room: professional.room, photo: professional.photo, active: professional.active }); setFormOpen(true); setSaved(false) }
  const submit = (event: FormEvent) => { event.preventDefault(); const fallbackPhoto = `https://ui-avatars.com/api/?name=${encodeURIComponent(form.name)}&background=E1EEEF&color=0B3B49&size=512`; saveProfessional(editing ? { ...form, id: editing.id, photo: form.photo || fallbackPhoto } : { ...form, photo: form.photo || fallbackPhoto }); setSaved(true); window.setTimeout(() => setFormOpen(false), 650) }
  const uploadPhoto = (file?: File) => { if (!file) return; const reader = new FileReader(); reader.onload = () => setForm((current) => ({ ...current, photo: String(reader.result) })); reader.readAsDataURL(file) }

  return (
    <div className="page-enter">
      <PageHeader eyebrow="Equipe do edifício" title="Profissionais" description="Gerencie quem atende no LUMIS e suas informações." action={<button className="primary-button" onClick={openNew}><Plus size={18} /> Novo profissional</button>} />
      <section className="panel table-panel">
        <div className="table-toolbar"><div className="search-field"><Search size={18} /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Buscar por nome, profissão ou sala" aria-label="Buscar profissionais" /></div><span>{filtered.length} profissionais</span></div>
        {filtered.length ? <div className="table-scroll"><table className="data-table"><thead><tr><th>Profissional</th><th>Profissão</th><th>Sala</th><th>WhatsApp</th><th>Status</th><th className="actions-column">Ações</th></tr></thead><tbody>{filtered.map((professional) => <tr key={professional.id}><td><div className="person-cell"><img src={professional.photo} alt="" /><span><strong>{professional.name}</strong><small>Atendimento presencial</small></span></div></td><td>{professional.profession}</td><td><span className="room-tag">Sala {professional.room || '—'}</span></td><td>{professional.phone}</td><td><StatusBadge status={professional.active ? 'active' : 'inactive'} /></td><td><div className="row-actions"><button onClick={() => setViewing(professional)} title="Visualizar"><Eye size={17} /></button><button onClick={() => openEdit(professional)} title="Editar"><Pencil size={17} /></button><button onClick={() => toggleProfessional(professional.id)} title={professional.active ? 'Desativar' : 'Ativar'}>{professional.active ? <UserMinus size={17} /> : <UserPlus size={17} />}</button></div></td></tr>)}</tbody></table></div> : <EmptyState>Nenhum profissional encontrado para “{query}”.</EmptyState>}
      </section>

      <Modal open={formOpen} onClose={() => setFormOpen(false)} title={editing ? 'Editar profissional' : 'Novo profissional'} subtitle="Preencha as informações exibidas na recepção." size="large"><form className="form-grid" onSubmit={submit}>
        <div className="photo-upload"><div>{form.photo ? <img src={form.photo} alt="Prévia" /> : <span>{form.name ? form.name.slice(0, 1).toUpperCase() : '?'}</span>}</div><label className="secondary-button">Escolher foto<input type="file" accept="image/*" onChange={(event) => uploadPhoto(event.target.files?.[0])} /></label><small>JPG ou PNG. A imagem será exibida na recepção.</small></div>
        <div className="fields-area"><label className="field-label span-2">Nome completo<input className="field-input" required value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} placeholder="Ex.: Dra. Helena Martins" /></label><label className="field-label">Profissão<input className="field-input" required value={form.profession} onChange={(event) => setForm({ ...form, profession: event.target.value })} placeholder="Ex.: Psicóloga clínica" /></label><label className="field-label">Telefone / WhatsApp<input className="field-input" required value={form.phone} onChange={(event) => setForm({ ...form, phone: event.target.value })} placeholder="(92) 99999-9999" /></label><label className="field-label">Sala<select className="field-input" required value={form.room} onChange={(event) => setForm({ ...form, room: event.target.value })}><option value="">Selecione</option>{rooms.map((room) => <option key={room.number} value={room.number}>Sala {room.number} · {room.status === 'available' ? 'Disponível' : 'Ocupada'}</option>)}</select></label><label className="field-label">Status<select className="field-input" value={form.active ? 'active' : 'inactive'} onChange={(event) => setForm({ ...form, active: event.target.value === 'active' })}><option value="active">Ativo</option><option value="inactive">Inativo</option></select></label></div>
        <div className="modal-actions span-all"><button className="ghost-button" type="button" onClick={() => setFormOpen(false)}>Cancelar</button><button className="primary-button" type="submit">{saved ? 'Salvo com sucesso!' : editing ? 'Salvar alterações' : 'Cadastrar profissional'}</button></div>
      </form></Modal>

      <Modal open={!!viewing} onClose={() => setViewing(null)} title="Detalhes do profissional">{viewing && <div className="professional-detail"><div className="tenant-profile"><img src={viewing.photo} alt={`Foto de ${viewing.name}`} /><span><StatusBadge status={viewing.active ? 'active' : 'inactive'} /><strong>{viewing.name}</strong><p>{viewing.profession}</p></span></div><div className="profile-facts"><div><small>Sala</small><strong>{viewing.room ? `Sala ${viewing.room}` : 'Não definida'}</strong></div><div><small>WhatsApp</small><strong>{viewing.phone}</strong></div></div><button className="secondary-button full-button" onClick={() => { setViewing(null); openEdit(viewing) }}><Pencil size={17} /> Editar informações</button></div>}</Modal>
    </div>
  )
}
