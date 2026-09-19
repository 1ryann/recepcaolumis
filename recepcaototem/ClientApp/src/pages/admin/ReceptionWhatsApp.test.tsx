import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, expect, test, vi } from 'vitest'
import { whatsAppOptInApi, type WhatsAppOptInRecordDto } from '../../api/modules'
import { ReceptionWhatsApp } from './ReceptionWhatsApp'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  whatsAppOptInApi: { receptionLookup: vi.fn(), receptionGrant: vi.fn(), receptionOptOut: vi.fn() },
}))

const record = (overrides: Partial<WhatsAppOptInRecordDto>): WhatsAppOptInRecordDto => ({
  id: 'c1', kind: 'CUSTOMER', maskedName: 'Maria S.', hasAccount: false, isActive: true,
  optIn: { status: 'NOT_RECORDED', changedAt: null, source: null, textVersion: null }, ...overrides,
})

function renderPage() {
  return render(<MemoryRouter initialEntries={['/recepcao/whatsapp']}><ReceptionWhatsApp /></MemoryRouter>)
}

async function search(phone = '(69) 98111-0001') {
  fireEvent.change(screen.getByLabelText('WhatsApp do cliente'), { target: { value: phone } })
  fireEvent.click(screen.getByRole('button', { name: /consultar/i }))
}

beforeEach(() => {
  vi.mocked(whatsAppOptInApi.receptionLookup).mockReset()
  vi.mocked(whatsAppOptInApi.receptionGrant).mockReset()
  vi.mocked(whatsAppOptInApi.receptionOptOut).mockReset()
})

test('shows only masked names and whether each record has an account', async () => {
  vi.mocked(whatsAppOptInApi.receptionLookup).mockResolvedValue({ records: [record({})] })
  renderPage()

  await search()

  expect(await screen.findByText('Maria S.')).toBeInTheDocument()
  expect(screen.getByText(/Cliente · sem conta/)).toBeInTheDocument()
  expect(screen.getByText('Sem autorização')).toBeInTheDocument()
  expect(whatsAppOptInApi.receptionLookup).toHaveBeenCalledWith('(69) 98111-0001')
})

test('an opt-in is recorded only after the attendant confirms the person heard the text and agreed', async () => {
  vi.mocked(whatsAppOptInApi.receptionLookup)
    .mockResolvedValueOnce({ records: [record({})] })
    .mockResolvedValueOnce({ records: [record({ optIn: { status: 'GRANTED', changedAt: '2026-09-19T12:00:00Z', source: 'RECEPTION', textVersion: 'whatsapp-operacional-v1' } })] })
  vi.mocked(whatsAppOptInApi.receptionGrant).mockResolvedValue({ customers: 1 })
  renderPage()
  await search()

  expect(await screen.findByText(/Leia para o cliente/)).toBeInTheDocument()
  const grant = screen.getByRole('button', { name: 'Registrar autorização' })
  expect(grant).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox', { name: /está presente, ouviu o texto/ }))
  fireEvent.click(grant)

  expect(await screen.findByText('Autorização registrada.')).toBeInTheDocument()
  expect(whatsAppOptInApi.receptionGrant).toHaveBeenCalledWith('(69) 98111-0001')
  expect(await screen.findByText('Autorizado')).toBeInTheDocument()
})

test('professionals are never opted in here and a withdrawal is always offered', async () => {
  vi.mocked(whatsAppOptInApi.receptionLookup)
    .mockResolvedValueOnce({ records: [record({ kind: 'PROFESSIONAL', maskedName: 'Ana P.', hasAccount: true, optIn: { status: 'GRANTED', changedAt: '2026-09-19T12:00:00Z', source: 'PROFESSIONAL_PORTAL', textVersion: 'whatsapp-operacional-v1' } })] })
    .mockResolvedValueOnce({ records: [record({ kind: 'PROFESSIONAL', maskedName: 'Ana P.', optIn: { status: 'REVOKED', changedAt: '2026-09-19T13:00:00Z', source: 'RECEPTION', textVersion: null } })] })
  vi.mocked(whatsAppOptInApi.receptionOptOut).mockResolvedValue({ customers: 0, professionals: 1 })
  renderPage()
  await search()

  expect(await screen.findByText('Ana P.')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Registrar autorização' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Registrar cancelamento dos avisos' }))

  expect(await screen.findByText(/Cancelamento registrado/)).toBeInTheDocument()
  expect(whatsAppOptInApi.receptionOptOut).toHaveBeenCalledWith('(69) 98111-0001')
})

test('an invalid number is reported by the backend rule, not guessed here', async () => {
  const { ApiError } = await import('../../api/client')
  vi.mocked(whatsAppOptInApi.receptionLookup).mockRejectedValue(
    new ApiError(400, 'WHATSAPP_RECIPIENT_INVALID', 'Informe um número de WhatsApp válido.'))
  renderPage()

  await search('12345678')

  expect(await screen.findByRole('alert')).toHaveTextContent('Informe um número de WhatsApp válido.')
})
