import QRCode from 'qrcode'
import { ArrowLeft, CalendarDays, Check, Clock3, Copy, QrCode, UserRound } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { customerApi, type ReservationDto } from '../../api/modules'

function dateLabel(value: string) { return new Date(value).toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: 'long' }) }
function timeLabel(value: string) { return new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }) }

export function CustomerReservationDetail() {
  const { id = '' } = useParams()
  const [reservation, setReservation] = useState<ReservationDto | null>(null)
  const [token, setToken] = useState('')
  const [manualCode, setManualCode] = useState('')
  const [qrDataUrl, setQrDataUrl] = useState('')
  const [expiresAt, setExpiresAt] = useState('')
  const [loading, setLoading] = useState(true)
  const [issuing, setIssuing] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!id) return
    customerApi.reservation(id).then(setReservation).catch(() => setError('Agendamento não encontrado.')).finally(() => setLoading(false))
  }, [id])

  const issueToken = async () => {
    setIssuing(true); setError('')
    try {
      const result = await customerApi.issueCheckInToken(id)
      setToken(result.token)
      setManualCode(result.manualCode)
      setExpiresAt(result.expiresAt)
      setQrDataUrl(await QRCode.toDataURL(result.token, { margin: 1, width: 280, color: { dark: '#292929', light: '#ffffff' } }))
    } catch (caught) {
      setError(caught instanceof ApiError && caught.code === 'CHECK_IN_NOT_ELIGIBLE' ? 'O QR Code fica disponível uma hora antes do horário e até o fim do atendimento.' : 'Não foi possível gerar o QR Code agora.')
    } finally { setIssuing(false) }
  }

  const copyToken = async () => { if (token) await navigator.clipboard?.writeText(token) }
  if (loading) return <section className="customer-section"><div className="customer-loading" role="status">Carregando agendamento…</div></section>
  if (!reservation) return <section className="customer-section"><div className="form-error" role="alert">{error || 'Agendamento não encontrado.'}</div><Link className="secondary-button" to="/cliente/agendamentos">Voltar</Link></section>
  const statusLabel = reservation.status === 'APPROVED' ? 'Confirmado' : reservation.status === 'CANCELLED' ? 'Cancelado' : reservation.status
  return <section className="customer-section page-enter"><Link className="customer-back-link" to="/cliente/agendamentos"><ArrowLeft size={16} /> Voltar para meus agendamentos</Link><div className="customer-detail-heading"><div><span className="eyebrow">Detalhes do agendamento</span><h1>{reservation.professionalName}</h1><p>{reservation.roomName} · <span className={`customer-status status-${reservation.status.toLowerCase()}`}>{statusLabel}</span></p></div><div className="customer-detail-icon"><CalendarDays size={25} /></div></div>{error && <div className="form-error" role="alert">{error}</div>}<div className="customer-detail-grid"><div className="customer-detail-card panel"><div className="customer-detail-row"><CalendarDays size={18} /><span><small>Data</small><strong>{dateLabel(reservation.startAt)}</strong></span></div><div className="customer-detail-row"><Clock3 size={18} /><span><small>Horário</small><strong>{timeLabel(reservation.startAt)} – {timeLabel(reservation.endAt)}</strong></span></div><div className="customer-detail-row"><UserRound size={18} /><span><small>Profissional</small><strong>{reservation.professionalName}</strong></span></div></div><div className="customer-qr-card panel"><div className="panel-header"><div><h2><QrCode size={18} /> Check-in</h2><p>Apresente este QR Code no Totem quando chegar.</p></div></div>{qrDataUrl ? <><img className="customer-qr-image" src={qrDataUrl} alt="QR Code de check-in" /><div className="customer-checkin-code"><small>Código</small><strong aria-label={`Código ${manualCode.split('').join(' ')}`}>{manualCode}</strong><span>Use este código no Totem.</span></div><small className="customer-qr-expiry">Válido até {new Date(expiresAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</small><button className="secondary-button" type="button" onClick={copyToken}><Copy size={15} /> Copiar código de desenvolvimento</button></> : <div className="customer-qr-empty"><QrCode size={28} /><strong>Seu QR Code de chegada</strong><span>Disponível uma hora antes do horário reservado.</span><button className="primary-button" type="button" onClick={issueToken} disabled={issuing}>{issuing ? 'Gerando…' : 'Gerar QR Code'} <Check size={16} /></button></div>}</div></div></section>
}
