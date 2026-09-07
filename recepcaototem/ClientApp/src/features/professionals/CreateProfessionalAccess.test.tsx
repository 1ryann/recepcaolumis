import { fireEvent, render, screen } from '@testing-library/react'
import { vi } from 'vitest'
import { CreateProfessionalAccess } from './CreateProfessionalAccess'
import { apiClient } from '../../api/client'
vi.mock('../../api/client', () => ({ apiClient: { post: vi.fn() } }))
test('creates a professional account through administrative provisioning and exposes its temporary password only in the dialog', async () => {
 vi.mocked(apiClient.post).mockResolvedValue({ userId: 'new-user', temporaryPassword: 'Temporary-test-only!' })
 const created = vi.fn()
 render(<CreateProfessionalAccess name="Profissional teste" onCreated={created} />)
 fireEvent.change(screen.getByLabelText('E-mail da nova conta'), { target: { value: 'professional@example.test' } })
 fireEvent.click(screen.getByText('Criar acesso profissional'))
 expect(await screen.findByText('Temporary-test-only!')).toBeInTheDocument()
 expect(apiClient.post).toHaveBeenCalledWith('/api/admin/users', { displayName: 'Profissional teste', email: 'professional@example.test', role: 'PROFISSIONAL' })
 expect(created).toHaveBeenCalledWith('new-user')
 expect(localStorage.length).toBe(0)
})
