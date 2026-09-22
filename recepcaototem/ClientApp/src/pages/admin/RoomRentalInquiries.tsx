import { displayWhatsApp } from '../../utils/whatsappMask'
import { CheckCircle2, MessageSquareText } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { type PagedResponse, type RoomRentalInquiryAdminDto, roomRentalInquiriesApi } from '../../api/modules'
import { Modal } from '../../components/Modal'
import { EmptyState, PageHeader, StatusBadge, countLabel } from '../../components/PageElements'

const pageSize = 20
const empty: PagedResponse<RoomRentalInquiryAdminDto> = { items: [], page: 1, pageSize, totalCount: 0 }

const formatDateTime = (value: string) => new Date(value).toLocaleString('pt-BR', {
  timeZone: 'America/Porto_Velho', dateStyle: 'short', timeStyle: 'short',
})

// DateOnly comes over the wire as a bare "YYYY-MM-DD" string. Parsing it with `new Date`
// would read it as UTC midnight and can roll to the previous/next day once converted to
// local time — split it by hand instead, matching TotemRoomsCatalog.tsx's/TotemRoomDetail.tsx's
// `dateLabel` (final fix wave, room-rental UX fixes).
const dateLabel = (value: string) => {
  const [year, month, day] = value.split('-')
  return `${day}/${month}/${year}`
}
const desiredPeriodLabel = (inquiry: RoomRentalInquiryAdminDto) =>
  inquiry.desiredStartDate && inquiry.desiredEndDate
    ? `${dateLabel(inquiry.desiredStartDate)} até ${dateLabel(inquiry.desiredEndDate)}`
    : 'Não informado'

export function RoomRentalInquiries() {
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(empty)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [detail, setDetail] = useState<RoomRentalInquiryAdminDto | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      setError(null)
      setResult(await roomRentalInquiriesApi.list({ page, pageSize }, signal))
    } catch (reason) {
      if ((reason as DOMException).name !== 'AbortError') setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os interesses de locação.')
    }
  }, [page])
  useEffect(() => {
    const controller = new AbortController()
    setLoading(result.items.length === 0); setRefreshing(result.items.length > 0)
    void load(controller.signal).finally(() => { if (!controller.signal.aborted) { setLoading(false); setRefreshing(false) } })
    return () => controller.abort()
  }, [load])

  const refresh = async () => { await load() }
  const showDetail = async (inquiry: RoomRentalInquiryAdminDto) => {
    setDetailLoading(true)
    try { setDetail(await roomRentalInquiriesApi.get(inquiry.id)) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Não foi possível carregar o interesse.') }
    finally { setDetailLoading(false) }
  }
  const createLease = (inquiry: RoomRentalInquiryAdminDto) => navigate(`/admin/locacoes?inquiryId=${inquiry.id}`)
  const viewLease = (inquiry: RoomRentalInquiryAdminDto) => navigate(`/admin/locacoes?leaseId=${inquiry.leaseId}`)
  const pages = Math.max(1, Math.ceil(result.totalCount / pageSize))

  return <div className="page-enter">
    <PageHeader eyebrow="Interesses recebidos pelo totem" title="Interesses de locação"
      description="Consulte quem demonstrou interesse em alugar uma sala e inicie a locação quando fizer sentido." />
    <section className="panel table-panel">
      <div className="table-toolbar"><span>{countLabel(result.totalCount, 'interesse', 'interesses')}</span></div>
      {loading ? <div className="empty-state" role="status">Carregando interesses…</div>
        : error && result.items.length === 0 ? <EmptyState><p>{error}</p><button className="secondary-button" onClick={() => void refresh()}>Tentar novamente</button></EmptyState>
          : result.items.length === 0 ? <EmptyState>Nenhum interesse encontrado.</EmptyState>
            : <div className="table-scroll"><table className="data-table"><thead><tr>
                <th>Nome</th><th>Sala</th><th>Disponibilidade</th><th>Status</th><th>Recebido em</th><th>Ações</th>
              </tr></thead><tbody>
                {result.items.map(inquiry => <tr key={inquiry.id}>
                  <td><strong>{inquiry.fullName}</strong></td>
                  <td><a className="text-link" href="/admin/salas">{inquiry.roomName}</a></td>
                  <td>{inquiry.presentedAvailabilityLabel}</td>
                  <td><StatusBadge tone={inquiry.status === 'NEW' ? 'pending' : 'active'} label={inquiry.status === 'NEW' ? 'Novo' : 'Convertido'} />
                    {inquiry.status === 'CONVERTED' && <div className="inquiry-converted-note"><CheckCircle2 size={14} /> Convertido em locação</div>}
                  </td>
                  <td>{formatDateTime(inquiry.createdAt)}</td>
                  <td><div className="room-admin-actions">
                    <button className="secondary-button" onClick={() => void showDetail(inquiry)}>Ver detalhes</button>
                    {inquiry.status === 'NEW'
                      ? <button className="primary-button" onClick={() => createLease(inquiry)}>Criar locação</button>
                      : <button className="secondary-button" onClick={() => viewLease(inquiry)}>Ver locação</button>}
                  </div></td>
                </tr>)}
              </tbody></table></div>}
      {error && result.items.length > 0 && <p className="form-error" role="alert">{error}</p>}{refreshing && <p className="list-refreshing" role="status">Atualizando lista…</p>}
      {result.totalCount > pageSize && <div className="pagination"><button className="secondary-button" disabled={page <= 1 || refreshing} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {pages}</span><button className="secondary-button" disabled={page >= pages || refreshing} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
    </section>

    <Modal open={detail !== null} onClose={() => setDetail(null)} title="Detalhes do interesse" subtitle={detailLoading ? 'Carregando…' : undefined}>
      {detail && <dl className="room-rates">
        <div><dt>Nome</dt><dd>{detail.fullName}</dd></div>
        <div><dt>WhatsApp</dt><dd>{displayWhatsApp(detail.whatsApp)}</dd></div>
        <div><dt>Profissão/Empresa</dt><dd>{detail.professionOrCompany}</dd></div>
        <div><dt>Sala</dt><dd><a className="text-link" href="/admin/salas">{detail.roomName}</a></dd></div>
        <div><dt>Disponibilidade</dt><dd>{detail.presentedAvailabilityLabel}</dd></div>
        <div><dt>Período desejado</dt><dd>{desiredPeriodLabel(detail)}</dd></div>
        <div><dt>Observação</dt><dd>{detail.note || 'Sem observação.'}</dd></div>
        <div><dt>Recebido em</dt><dd>{formatDateTime(detail.createdAt)}</dd></div>
        {detail.status === 'CONVERTED' && detail.convertedAt && <div><dt>Convertido em</dt><dd>{formatDateTime(detail.convertedAt)}</dd></div>}
      </dl>}
      <div className="modal-actions">
        <button className="ghost-button" type="button" onClick={() => setDetail(null)}>Fechar</button>
        {detail && (detail.status === 'NEW'
          ? <button className="primary-button" onClick={() => createLease(detail)}><MessageSquareText size={16} /> Criar locação</button>
          : <button className="secondary-button" onClick={() => viewLease(detail)}>Ver locação</button>)}
      </div>
    </Modal>
  </div>
}
