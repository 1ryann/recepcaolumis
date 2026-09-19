import { MessageCircle } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import type { WhatsAppOptInDto } from '../../api/modules'
import { OPT_IN_SCOPE_NOTE } from './optInText'
import './whatsapp-opt-in.css'

/**
 * The explicit opt-in: unticked by default, the full wording next to it, never pre-checked. Unticked means "no
 * decision", never a withdrawal: the caller sends `whatsAppOptIn` only when this is ticked.
 */
export function WhatsAppOptInCheckbox({ text, checked, onChange, disabled }: {
  text: string, checked: boolean, onChange: (checked: boolean) => void, disabled?: boolean
}) {
  return (
    <label className="whatsapp-opt-in-check">
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} />
      <span>{text}<small className="whatsapp-opt-in-note">{OPT_IN_SCOPE_NOTE}</small></span>
    </label>
  )
}

function dateLabel(value: string | null) {
  return value ? new Date(value).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' }) : ''
}

export function optInStatusLabel(optIn: WhatsAppOptInDto) {
  if (optIn.status === 'GRANTED') return `Você recebe avisos por WhatsApp desde ${dateLabel(optIn.changedAt)}.`
  if (optIn.status === 'REVOKED') return `Avisos por WhatsApp cancelados em ${dateLabel(optIn.changedAt)}.`
  return 'Você ainda não autorizou avisos por WhatsApp.'
}

/**
 * Current decision and the switch for it, in the person's own area. Granting shows the exact wording being agreed
 * to; withdrawing is one click and always available.
 */
export function WhatsAppOptInPanel({ text, load, save }: {
  text: string
  load: (signal?: AbortSignal) => Promise<WhatsAppOptInDto>
  save: (optIn: boolean) => Promise<WhatsAppOptInDto>
}) {
  const [optIn, setOptIn] = useState<WhatsAppOptInDto | null>(null)
  const [agreed, setAgreed] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const reload = useCallback((signal?: AbortSignal) => load(signal).then(setOptIn), [load])

  useEffect(() => {
    const controller = new AbortController()
    reload(controller.signal).catch(() => { if (!controller.signal.aborted) setError('Não foi possível carregar sua preferência de WhatsApp.') })
    return () => controller.abort()
  }, [reload])

  const change = async (next: boolean) => {
    setSaving(true); setError('')
    try { setOptIn(await save(next)); setAgreed(false) }
    catch { setError('Não foi possível salvar agora. Tente novamente.') }
    finally { setSaving(false) }
  }

  return (
    <div className="whatsapp-opt-in-panel">
      {optIn && <p className="whatsapp-opt-in-state"><MessageCircle size={16} /> {optInStatusLabel(optIn)}</p>}
      {error && <div className="form-error" role="alert">{error}</div>}
      {optIn?.status === 'GRANTED' && (
        <div className="whatsapp-opt-in-actions">
          <button className="secondary-button" type="button" disabled={saving} onClick={() => void change(false)}>
            {saving ? 'Salvando…' : 'Parar de receber avisos'}
          </button>
        </div>
      )}
      {optIn && optIn.status !== 'GRANTED' && (
        <>
          <WhatsAppOptInCheckbox text={text} checked={agreed} onChange={setAgreed} disabled={saving} />
          <div className="whatsapp-opt-in-actions">
            <button className="primary-button" type="button" disabled={!agreed || saving} onClick={() => void change(true)}>
              {saving ? 'Salvando…' : 'Autorizar avisos por WhatsApp'}
            </button>
          </div>
        </>
      )}
    </div>
  )
}
