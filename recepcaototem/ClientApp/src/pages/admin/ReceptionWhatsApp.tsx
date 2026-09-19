import { ArrowLeft, MessageCircle, Search } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { whatsAppOptInApi, type WhatsAppOptInRecordDto } from '../../api/modules'
import { CUSTOMER_OPT_IN_TEXT } from '../../features/whatsapp/optInText'
import '../../features/whatsapp/whatsapp-opt-in.css'

const statusLabel: Record<string, string> = {
  GRANTED: 'Autorizado',
  REVOKED: 'Cancelado',
  NOT_RECORDED: 'Sem autorização',
}

/**
 * Reception desk: WhatsApp opt-in by phone number, for people standing at the desk — including customers created at
 * the Totem, who have no account to do it themselves. Only masked names are shown. Recording an opt-in requires the
 * attendant to confirm the person heard the wording and agreed (the attendant is audited as the one who recorded it);
 * professionals are never opted in here. A withdrawal is always accepted.
 */
export function ReceptionWhatsApp() {
  const location = useLocation()
  const prefix = location.pathname.startsWith('/admin') ? '/admin' : '/recepcao'
  const [phone, setPhone] = useState('')
  const [searched, setSearched] = useState('')
  const [records, setRecords] = useState<WhatsAppOptInRecordDto[] | null>(null)
  const [attested, setAttested] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const lookup = async (number: string) => {
    const result = await whatsAppOptInApi.receptionLookup(number)
    setRecords(result.records)
    setSearched(number)
  }

  const run = async (action: () => Promise<void>) => {
    setBusy(true); setError(''); setNotice('')
    try { await action() }
    catch (caught) {
      setError(caught instanceof ApiError && caught.code === 'WHATSAPP_RECIPIENT_INVALID'
        ? 'Informe um número de WhatsApp válido.'
        : 'Não foi possível concluir agora. Tente novamente.')
    } finally { setBusy(false) }
  }

  const search = (event: FormEvent) => {
    event.preventDefault()
    setAttested(false)
    void run(() => lookup(phone))
  }

  const grant = () => run(async () => {
    const result = await whatsAppOptInApi.receptionGrant(searched)
    setNotice(result.customers > 0 ? 'Autorização registrada.' : 'O cliente já estava autorizado.')
    setAttested(false)
    await lookup(searched)
  })

  const optOut = () => run(async () => {
    const result = await whatsAppOptInApi.receptionOptOut(searched)
    setNotice(result.customers + result.professionals > 0 ? 'Cancelamento registrado. Nenhum aviso será enviado a este número.' : 'Este número já não recebia avisos.')
    await lookup(searched)
  })

  const customers = records?.filter((record) => record.kind === 'CUSTOMER' && record.isActive) ?? []
  const canGrant = customers.some((record) => record.optIn.status !== 'GRANTED')
  const canOptOut = (records ?? []).some((record) => record.optIn.status !== 'REVOKED')

  return (
    <section className="page-enter">
      <div className="page-header">
        <div><span className="page-eyebrow">Recepção</span><h1>WhatsApp dos clientes</h1><p>Registre a autorização ou o cancelamento dos avisos por WhatsApp de quem está no balcão.</p></div>
        <div className="page-header-actions"><Link className="secondary-button" to={prefix === '/admin' ? '/admin/recepcao' : '/recepcao'}><ArrowLeft size={16} /> Voltar à recepção</Link></div>
      </div>

      <div className="panel whatsapp-opt-in-panel">
        <form className="whatsapp-opt-in-lookup" onSubmit={search}>
          <label className="field-label">WhatsApp do cliente
            <input className="field-input" inputMode="tel" autoComplete="off" value={phone} onChange={(event) => setPhone(event.target.value)} placeholder="(69) 99999-9999" />
          </label>
          <button className="primary-button" type="submit" disabled={busy || phone.trim().length < 8}><Search size={16} /> Consultar</button>
        </form>
        {error && <div className="form-error" role="alert">{error}</div>}
        {notice && <p className="whatsapp-opt-in-state" role="status"><MessageCircle size={16} /> {notice}</p>}

        {records && records.length === 0 && <p className="whatsapp-opt-in-text">Nenhum cadastro com este número. Um cancelamento ainda pode ser registrado para garantir que nada seja enviado.</p>}
        {records && records.length > 0 && (
          <ul className="whatsapp-opt-in-records" aria-label="Cadastros com este número">
            {records.map((record) => (
              <li key={`${record.kind}-${record.id}`}>
                <strong>{record.maskedName}</strong>
                <span>{record.kind === 'CUSTOMER' ? 'Cliente' : 'Profissional'}{record.hasAccount ? ' · com conta' : ' · sem conta'}{record.isActive ? '' : ' · inativo'}</span>
                <b>{statusLabel[record.optIn.status]}</b>
              </li>
            ))}
          </ul>
        )}

        {records && canGrant && (
          <>
            <p className="whatsapp-opt-in-text"><strong>Leia para o cliente:</strong> “{CUSTOMER_OPT_IN_TEXT}”</p>
            <label className="whatsapp-opt-in-check">
              <input type="checkbox" checked={attested} disabled={busy} onChange={(event) => setAttested(event.target.checked)} />
              <span>O cliente está presente, ouviu o texto acima e concordou em receber os avisos.</span>
            </label>
          </>
        )}

        {records && (
          <div className="whatsapp-opt-in-actions">
            {canGrant && <button className="primary-button" type="button" disabled={busy || !attested} onClick={() => void grant()}>Registrar autorização</button>}
            <button className="secondary-button" type="button" disabled={busy || (records.length > 0 && !canOptOut)} onClick={() => void optOut()}>Registrar cancelamento dos avisos</button>
          </div>
        )}
      </div>
    </section>
  )
}
