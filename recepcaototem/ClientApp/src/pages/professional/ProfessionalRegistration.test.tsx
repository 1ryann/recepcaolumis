import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { vi } from 'vitest'
import { professionalRegistrationApi } from '../../api/modules'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { ProfessionalRegistration } from './ProfessionalRegistration'
import { ProfessionalApplicationStatus } from './ProfessionalApplicationStatus'
vi.mock('../../api/modules', () => ({ professionalRegistrationApi: { register: vi.fn(), me: vi.fn() } }))
vi.mock('../../auth/SessionProvider', () => ({ useSession: () => ({ logout: vi.fn() }) }))
test('submits the approved public professional fields without a role', async () => {
 vi.mocked(professionalRegistrationApi.register).mockResolvedValue({ id: 'a', name: 'Ana', profession: 'Fisio', description: null, status: 'PENDING', createdAt: '2026-09-07T12:00:00Z', reviewedAt: null, concurrencyToken: 'x' })
 render(<ThemeProvider><MemoryRouter><ProfessionalRegistration /></MemoryRouter></ThemeProvider>)
 for (const [label, value] of [['Nome completo','Ana'],['Profissão / especialidade','Fisio'],['WhatsApp','69999999999'],['E-mail','ana@example.test'],['Senha','Valid-Password-123!'],['Confirmar senha','Valid-Password-123!']]) fireEvent.change(screen.getByLabelText(label), { target: { value } })
 fireEvent.click(screen.getByText('Enviar solicitação'))
 expect(await screen.findByText('Cadastro enviado')).toBeInTheDocument()
 expect(professionalRegistrationApi.register).toHaveBeenCalledWith(expect.not.objectContaining({ role: expect.anything() }))
})
function fillRequired() {
 for (const [label, value] of [['Nome completo','Ana'],['Profissão / especialidade','Fisio'],['E-mail','ana@example.test'],['Senha','Valid-Password-123!'],['Confirmar senha','Valid-Password-123!']]) fireEvent.change(screen.getByLabelText(label), { target: { value } })
}

test('masks the WhatsApp field while typing and sends only the digits to the API', async () => {
 vi.mocked(professionalRegistrationApi.register).mockResolvedValue({ id: 'a', name: 'Ana', profession: 'Fisio', description: null, status: 'PENDING', createdAt: '2026-09-07T12:00:00Z', reviewedAt: null, concurrencyToken: 'x' })
 render(<ThemeProvider><MemoryRouter><ProfessionalRegistration /></MemoryRouter></ThemeProvider>)
 fillRequired()
 const field = screen.getByLabelText('WhatsApp') as HTMLInputElement
 fireEvent.change(field, { target: { value: '69993182032' } })
 expect(field.value).toBe('(69) 99318-2032')
 expect(field).toHaveAttribute('inputMode', 'numeric')
 fireEvent.click(screen.getByText('Enviar solicitação'))
 expect(await screen.findByText('Cadastro enviado')).toBeInTheDocument()
 expect(professionalRegistrationApi.register).toHaveBeenCalledWith(expect.objectContaining({ whatsApp: '69993182032' }))
})

test('accepts a pasted formatted number and normalises it before submit', async () => {
 vi.mocked(professionalRegistrationApi.register).mockResolvedValue({ id: 'a', name: 'Ana', profession: 'Fisio', description: null, status: 'PENDING', createdAt: '2026-09-07T12:00:00Z', reviewedAt: null, concurrencyToken: 'x' })
 render(<ThemeProvider><MemoryRouter><ProfessionalRegistration /></MemoryRouter></ThemeProvider>)
 fillRequired()
 const field = screen.getByLabelText('WhatsApp') as HTMLInputElement
 fireEvent.change(field, { target: { value: '(69) 99318-2032' } })
 expect(field.value).toBe('(69) 99318-2032')
 fireEvent.change(field, { target: { value: 'abc(69) 99318-2032xyz' } })
 expect(field.value).toBe('(69) 99318-2032')
 fireEvent.click(screen.getByText('Enviar solicitação'))
 await screen.findByText('Cadastro enviado')
 expect(professionalRegistrationApi.register).toHaveBeenCalledWith(expect.objectContaining({ whatsApp: '69993182032' }))
})

test('blocks submit with a clear message when the WhatsApp digit count is invalid', async () => {
 render(<ThemeProvider><MemoryRouter><ProfessionalRegistration /></MemoryRouter></ThemeProvider>)
 fillRequired()
 fireEvent.change(screen.getByLabelText('WhatsApp'), { target: { value: '69993' } })
 fireEvent.click(screen.getByText('Enviar solicitação'))
 expect(await screen.findByText('Informe um WhatsApp válido com DDD.')).toBeInTheDocument()
 expect(professionalRegistrationApi.register).not.toHaveBeenCalled()
})

test.each([['PENDING','Cadastro em análise'],['REJECTED','Cadastro não aprovado'],['APPROVED','Cadastro aprovado']])('shows %s without professional features', async (status, heading) => {
 vi.mocked(professionalRegistrationApi.me).mockResolvedValue({ id: 'a', name: 'Ana', profession: 'Fisio', description: null, status: status as 'PENDING'|'REJECTED'|'APPROVED', createdAt: '2026-09-07T12:00:00Z', reviewedAt: null, concurrencyToken: 'x' })
 render(<ThemeProvider><MemoryRouter><ProfessionalApplicationStatus /></MemoryRouter></ThemeProvider>)
 expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument()
 expect(screen.queryByText('Agenda')).not.toBeInTheDocument()
})
