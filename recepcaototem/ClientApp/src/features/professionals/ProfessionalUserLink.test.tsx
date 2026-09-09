import { render, screen } from '@testing-library/react'
import { vi } from 'vitest'
import { ProfessionalUserLink } from './ProfessionalUserLink'

vi.mock('../../api/client', () => ({ apiClient: { post: vi.fn() } }))

const baseProps = {
 professionalName: 'Dra. Helena',
 eligible: [],
 pending: false,
 onSearch: vi.fn(),
 onSave: vi.fn(),
 onRemove: vi.fn(),
}

test('offers the administrative password reset only when an account is linked', () => {
 const { rerender } = render(<ProfessionalUserLink {...baseProps} link={{ linked: false }} />)
 expect(screen.queryByRole('button', { name: 'Redefinir senha' })).not.toBeInTheDocument()

 rerender(<ProfessionalUserLink {...baseProps} link={{ linked: true, userId: 'user-1', displayName: 'Dra. Helena', email: 'helena@lumis.test' }} />)
 expect(screen.getByRole('button', { name: 'Redefinir senha' })).toBeInTheDocument()
})
