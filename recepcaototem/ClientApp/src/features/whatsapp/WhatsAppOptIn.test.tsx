import { fireEvent, render, screen } from '@testing-library/react'
import { expect, test, vi } from 'vitest'
import type { WhatsAppOptInDto } from '../../api/modules'
import { WhatsAppOptInCheckbox, WhatsAppOptInPanel } from './WhatsAppOptIn'
import { CUSTOMER_OPT_IN_TEXT } from './optInText'

const notRecorded: WhatsAppOptInDto = { status: 'NOT_RECORDED', changedAt: null, source: null, textVersion: null }
const granted: WhatsAppOptInDto = { status: 'GRANTED', changedAt: '2026-09-19T12:00:00Z', source: 'CUSTOMER_PORTAL', textVersion: 'whatsapp-operacional-v1' }
const revoked: WhatsAppOptInDto = { status: 'REVOKED', changedAt: '2026-09-20T12:00:00Z', source: 'CUSTOMER_PORTAL', textVersion: null }

test('the checkbox is never pre-ticked and shows the full wording and scope', () => {
  const onChange = vi.fn()
  render(<WhatsAppOptInCheckbox text={CUSTOMER_OPT_IN_TEXT} checked={false} onChange={onChange} />)

  const box = screen.getByRole('checkbox')
  expect(box).not.toBeChecked()
  expect(screen.getByText(/Quero receber no WhatsApp deste número avisos do LUMIS/)).toBeInTheDocument()
  expect(screen.getByText(/Nunca enviamos propaganda/)).toBeInTheDocument()
  fireEvent.click(box)
  expect(onChange).toHaveBeenCalledWith(true)
})

test('granting requires ticking the wording first, then records it', async () => {
  const save = vi.fn().mockResolvedValue(granted)
  render(<WhatsAppOptInPanel text={CUSTOMER_OPT_IN_TEXT} load={vi.fn().mockResolvedValue(notRecorded)} save={save} />)

  expect(await screen.findByText('Você ainda não autorizou avisos por WhatsApp.')).toBeInTheDocument()
  const authorize = screen.getByRole('button', { name: 'Autorizar avisos por WhatsApp' })
  expect(authorize).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox'))
  fireEvent.click(authorize)

  expect(save).toHaveBeenCalledWith(true)
  expect(await screen.findByText(/Você recebe avisos por WhatsApp desde/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Parar de receber avisos' })).toBeInTheDocument()
})

test('withdrawing is a single click and always available once granted', async () => {
  const save = vi.fn().mockResolvedValue(revoked)
  render(<WhatsAppOptInPanel text={CUSTOMER_OPT_IN_TEXT} load={vi.fn().mockResolvedValue(granted)} save={save} />)

  fireEvent.click(await screen.findByRole('button', { name: 'Parar de receber avisos' }))

  expect(save).toHaveBeenCalledWith(false)
  expect(await screen.findByText(/Avisos por WhatsApp cancelados em/)).toBeInTheDocument()
  expect(screen.getByRole('checkbox')).not.toBeChecked()                     // re-granting needs a new explicit tick
})

test('a load failure is reported instead of pretending a state', async () => {
  render(<WhatsAppOptInPanel text={CUSTOMER_OPT_IN_TEXT} load={vi.fn().mockRejectedValue(new Error('down'))} save={vi.fn()} />)

  expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível carregar sua preferência de WhatsApp.')
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
})
