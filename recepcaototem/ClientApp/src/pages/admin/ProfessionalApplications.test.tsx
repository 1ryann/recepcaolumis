import { fireEvent, render, screen } from '@testing-library/react'
import { vi } from 'vitest'
import { professionalRegistrationApi } from '../../api/modules'
import { ProfessionalApplications } from './ProfessionalApplications'
vi.mock('../../api/modules', () => ({ professionalRegistrationApi: { list: vi.fn(), approve: vi.fn(), reject: vi.fn() } }))
test('manager can approve a pending application with its concurrency token', async () => {
 const value = { id:'a', name:'Ana', profession:'Fisio', description:'Clínica', status:'PENDING' as const, createdAt:'2026-09-07T12:00:00Z', reviewedAt:null, concurrencyToken:'token' }
 vi.mocked(professionalRegistrationApi.list).mockResolvedValue({ items:[value], page:1, pageSize:20, totalCount:1 })
 vi.mocked(professionalRegistrationApi.approve).mockResolvedValue({ ...value, status:'APPROVED' })
 render(<ProfessionalApplications />)
 fireEvent.click(await screen.findByText('Aprovar'))
 expect(professionalRegistrationApi.approve).toHaveBeenCalledWith('a','token')
})
