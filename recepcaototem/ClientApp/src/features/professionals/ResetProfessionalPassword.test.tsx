import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { vi } from 'vitest'
import { ResetProfessionalPassword } from './ResetProfessionalPassword'
import { apiClient } from '../../api/client'

vi.mock('../../api/client', () => ({ apiClient: { post: vi.fn() } }))

test('shows the reset action and only calls the API after an explicit confirmation', async () => {
 vi.mocked(apiClient.post).mockResolvedValue({ userId: 'user-1', temporaryPassword: 'Temporary-test-only!' })
 render(<ResetProfessionalPassword userId="user-1" />)

 const trigger = screen.getByRole('button', { name: 'Redefinir senha' })
 fireEvent.click(trigger)
 expect(screen.getByText('Tem certeza que deseja redefinir a senha deste usuário?')).toBeInTheDocument()
 expect(apiClient.post).not.toHaveBeenCalled()
 expect(screen.queryByText('Temporary-test-only!')).not.toBeInTheDocument()

 fireEvent.click(screen.getByRole('button', { name: 'Confirmar redefinição' }))
 expect(await screen.findByText('Temporary-test-only!')).toBeInTheDocument()
 expect(apiClient.post).toHaveBeenCalledWith('/api/admin/users/user-1/reset-password', {})
})

test('states the password is shown once and discards it from state when the dialog is closed', async () => {
 vi.mocked(apiClient.post).mockResolvedValue({ userId: 'user-1', temporaryPassword: 'Temporary-test-only!' })
 render(<ResetProfessionalPassword userId="user-1" />)
 fireEvent.click(screen.getByRole('button', { name: 'Redefinir senha' }))
 fireEvent.click(screen.getByRole('button', { name: 'Confirmar redefinição' }))
 await screen.findByText('Temporary-test-only!')

 expect(screen.getByText(/não será exibida novamente/i)).toBeInTheDocument()
 expect(screen.getByText(/obrigado a criar uma nova senha no próximo acesso/i)).toBeInTheDocument()

 fireEvent.click(screen.getByRole('button', { name: 'Fechar' }))
 expect(screen.queryByText('Temporary-test-only!')).not.toBeInTheDocument()
 expect(screen.getByRole('button', { name: 'Redefinir senha' })).toBeInTheDocument()
 expect(localStorage.length).toBe(0)
})

test('handles an API error without exposing a password or crashing the view', async () => {
 vi.mocked(apiClient.post).mockRejectedValue(new Error('boom'))
 render(<ResetProfessionalPassword userId="user-1" />)
 fireEvent.click(screen.getByRole('button', { name: 'Redefinir senha' }))
 fireEvent.click(screen.getByRole('button', { name: 'Confirmar redefinição' }))

 expect(await screen.findByRole('alert')).toHaveTextContent(/não foi possível redefinir a senha/i)
 expect(screen.queryByRole('status')).not.toBeInTheDocument()
 await waitFor(() => expect(screen.getByRole('button', { name: 'Redefinir senha' })).toBeInTheDocument())
})
