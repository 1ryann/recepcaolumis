import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { expect, test, vi, beforeEach } from 'vitest'
import { professionalProfileApi } from '../../api/modules'
import { ProfessionalProfile } from './ProfessionalProfile'

vi.mock('../../api/modules', async (orig) => ({
  ...(await orig<typeof import('../../api/modules')>()),
  professionalProfileApi: { get: vi.fn(), update: vi.fn(), uploadPhoto: vi.fn(), deletePhoto: vi.fn() },
}))

beforeEach(() => {
  vi.mocked(professionalProfileApi.get).mockResolvedValue({
    name: 'Maria Clara', profession: 'Psicóloga', description: 'Atendimento humanizado.',
    whatsApp: '+5511999998888', hasPhoto: false, photoUrl: null, concurrencyToken: 'tok-1',
  })
})

test('renders read-only name/profession and editable WhatsApp/description', async () => {
  render(<ProfessionalProfile />)
  expect(await screen.findByText('Maria Clara')).toBeInTheDocument()
  expect(screen.getByText('Psicóloga')).toBeInTheDocument()
  expect(screen.getByDisplayValue('+5511999998888')).toBeInTheDocument()
  expect(screen.getByDisplayValue('Atendimento humanizado.')).toBeInTheDocument()
})

test('saves WhatsApp/description via PUT and reflects the returned profile', async () => {
  vi.mocked(professionalProfileApi.update).mockResolvedValue({
    name: 'Maria Clara', profession: 'Psicóloga', description: 'Nova descrição.',
    whatsApp: '+5511988887777', hasPhoto: false, photoUrl: null, concurrencyToken: 'tok-2',
  })
  render(<ProfessionalProfile />)
  await screen.findByDisplayValue('+5511999998888')
  fireEvent.change(screen.getByLabelText(/whatsapp/i), { target: { value: '11988887777' } })
  fireEvent.click(screen.getByRole('button', { name: /salvar/i }))
  await waitFor(() => expect(professionalProfileApi.update).toHaveBeenCalledWith({
    whatsApp: '11988887777', description: 'Atendimento humanizado.', concurrencyToken: 'tok-1',
  }))
  expect(await screen.findByDisplayValue('+5511988887777')).toBeInTheDocument()
})

test('the photo <img> is cache-busted with the current concurrencyToken so a replacement photo is not stale', async () => {
  vi.mocked(professionalProfileApi.get).mockResolvedValue({
    name: 'Maria Clara', profession: 'Psicóloga', description: 'Atendimento humanizado.',
    whatsApp: '+5511999998888', hasPhoto: true, photoUrl: '/api/professional/me/photo', concurrencyToken: 'tok-9',
  })
  render(<ProfessionalProfile />)
  const img = await screen.findByAltText('Foto de Maria Clara')
  expect(img).toHaveAttribute('src', '/api/professional/me/photo?v=tok-9')
})
