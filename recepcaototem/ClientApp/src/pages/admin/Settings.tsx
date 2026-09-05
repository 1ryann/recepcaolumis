import { Camera, Check, ChevronRight, MessageCircle, Save, Settings2 } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { PageHeader } from '../../components/PageElements'
import { useAppStore } from '../../dev/AppStore'

export function Settings() {
  const { settings, saveSettings } = useAppStore()
  const [form, setForm] = useState(settings)
  const [saved, setSaved] = useState(false)
  const [integrationMessage, setIntegrationMessage] = useState(false)
  const submit = (event: FormEvent) => { event.preventDefault(); saveSettings(form); setSaved(true); window.setTimeout(() => setSaved(false), 2200) }

  return (
    <div className="page-enter settings-page">
      <PageHeader eyebrow="Preferências" title="Configurações" description="Ajuste os dados gerais e dispositivos da recepção." />
      <form onSubmit={submit}>
        <section className="settings-grid">
          <article className="panel settings-card"><div className="settings-title"><span><Settings2 size={20} /></span><div><h2>Informações gerais</h2><p>Dados de identificação do edifício</p></div></div><div className="settings-fields"><label className="field-label">Nome do estabelecimento<input className="field-input" value={form.buildingName} onChange={(event) => setForm({ ...form, buildingName: event.target.value })} /></label><label className="field-label">WhatsApp da recepção<input className="field-input" value={form.receptionWhatsapp} onChange={(event) => setForm({ ...form, receptionWhatsapp: event.target.value })} /></label></div></article>
          <article className="panel settings-card"><div className="settings-title"><span><Camera size={20} /></span><div><h2>Configuração da câmera</h2><p>Dispositivo usado no autoatendimento</p></div></div><div className="settings-fields"><label className="field-label">Câmera preferencial<select className="field-input" value={form.camera} onChange={(event) => setForm({ ...form, camera: event.target.value })}><option>Câmera padrão do dispositivo</option><option>Webcam USB — HD</option><option>Câmera integrada</option></select></label><div className="device-status"><span><i /> Dispositivo pronto</span><small>A câmera será solicitada ao iniciar um novo atendimento.</small></div></div></article>
          <article className="panel settings-card integration-card"><div className="settings-title"><span><MessageCircle size={20} /></span><div><h2>Integração WhatsApp</h2><p>Notificações automáticas de visitantes</p></div></div><div className="integration-content"><div className="whatsapp-brand"><span><MessageCircle size={25} /></span><div><strong>WhatsApp Business</strong><small>Avise os profissionais quando um visitante chegar.</small></div></div><div className="demo-status"><span>Status</span><strong><i /> Modo demonstração</strong></div><button className="secondary-button full-button" type="button" onClick={() => { setIntegrationMessage(true); window.setTimeout(() => setIntegrationMessage(false), 2500) }}>Configurar integração <ChevronRight size={18} /></button>{integrationMessage && <div className="inline-notice"><Check size={16} /> A configuração será disponibilizada na versão integrada.</div>}</div></article>
        </section>
        <div className="settings-save"><span>{saved && <><Check size={17} /> Alterações salvas</>}</span><button className="primary-button" type="submit"><Save size={18} /> Salvar configurações</button></div>
      </form>
    </div>
  )
}
