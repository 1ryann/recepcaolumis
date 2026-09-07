import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { vi } from 'vitest'
import { professionalRegistrationApi } from '../../api/modules'
import { ProfessionalRegistration } from './ProfessionalRegistration'
import { ProfessionalApplicationStatus } from './ProfessionalApplicationStatus'
vi.mock('../../api/modules', () => ({ professionalRegistrationApi: { register: vi.fn(), me: vi.fn() } }))
vi.mock('../../auth/SessionProvider', () => ({ useSession: () => ({ logout: vi.fn() }) }))
test('submits the approved public professional fields without a role', async () => {
 vi.mocked(professionalRegistrationApi.register).mockResolvedValue({ id: 'a', name: 'Ana', profession: 'Fisio', description: null, status: 'PENDING', createdAt: '2026-09-07T12:00:00Z', reviewedAt: null, concurrencyToken: 'x' })
 render(<MemoryRouter><ProfessionalRegistration /></MemoryRouter>)
 for (const [label, value] of [['Nome completo','Ana'],['Profissão / especialidade','Fisio'],['WhatsApp','69999999999'],['E-mail','ana@example.test'],['Senha','Valid-Password-123!'],['Confirmar senha','Valid-Password-123!']]) fireEvent.change(screen.getByLabelText(label), { target: { value } })
 fireEvent.click(screen.getByText('Enviar solicitação'))
 expect(await screen.findByText('Cadastro enviado')).toBeInTheDocument()
 expect(professionalRegistrationApi.register).toHaveBeenCalledWith(expect.not.objectContaining({ role: expect.anything() }))
})
test.each([['PENDING','Cadastro em análise'],['REJECTED','Cadastro não aprovado'],['APPROVED','Cadastro aprovado']])('shows %s without professional features', async (status, heading) => {
 vi.mocked(professionalRegistrationApi.me).mockResolvedValue({ id: 'a', name: 'Ana', profession: 'Fisio', description: null, status: status as 'PENDING'|'REJECTED'|'APPROVED', createdAt: '2026-09-07T12:00:00Z', reviewedAt: null, concurrencyToken: 'x' })
 render(<MemoryRouter><ProfessionalApplicationStatus /></MemoryRouter>)
 expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument()
 expect(screen.queryByText('Agenda')).not.toBeInTheDocument()
})
