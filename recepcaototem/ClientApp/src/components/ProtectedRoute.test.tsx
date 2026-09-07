import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import { SessionProvider, useSession } from '../auth/SessionProvider'
import { apiClient, ApiError } from '../api/client'
import { ProtectedRoute } from './ProtectedRoute'
import { homeForRoles } from '../auth/roleRoutes'
vi.mock('../api/client', async (original) => ({ ...await original<typeof import('../api/client')>(), apiClient: { get: vi.fn(), post: vi.fn() } }))
const identity = (role: string) => ({ userId: role, roles: [role], displayName: role, email: 'test@example.test', mustChangePassword: false })
test.each([
 ['CUSTOMER', '/admin', 'ADMINISTRADOR'], ['PROFISSIONAL', '/admin', 'ADMINISTRADOR'],
 ['PROFISSIONAL', '/cliente', 'CUSTOMER'], ['ADMINISTRADOR', '/cliente', 'CUSTOMER'],
 ['CUSTOMER', '/profissional', 'PROFISSIONAL'], ['GERENTE', '/admin', 'ADMINISTRADOR'],
 ['PROFESSIONAL_APPLICANT', '/profissional', 'PROFISSIONAL'],
 ['PROFESSIONAL_APPLICANT', '/admin', 'ADMINISTRADOR'],
])('%s cannot render %s', async (role, path, allowed) => {
 vi.mocked(apiClient.get).mockResolvedValue(identity(role))
 render(<MemoryRouter initialEntries={[path]}><SessionProvider><Routes><Route element={<ProtectedRoute allowedRoles={[allowed]} />}><Route path={path} element={<p>protected content</p>} /></Route><Route path={homeForRoles([role])} element={<p>own home</p>} /></Routes></SessionProvider></MemoryRouter>)
 expect(screen.queryByText('protected content')).not.toBeInTheDocument()
 expect(await screen.findByText('own home')).toBeInTheDocument()
})
function Switcher() {
 const session = useSession()
 return <><p>{session.status}:{session.user?.userId ?? 'none'}</p><button onClick={() => void session.logout()}>logout</button><button onClick={() => void session.login('test@example.test', 'test-only')}>login</button></>
}
test.each([['ADMINISTRADOR', 'CUSTOMER'], ['CUSTOMER', 'PROFISSIONAL']])('switching %s to %s confirms logout and replaces identity', async (before, after) => {
 vi.mocked(apiClient.get).mockResolvedValue(identity(before))
 vi.mocked(apiClient.post).mockResolvedValue(undefined)
 render(<SessionProvider><Switcher /></SessionProvider>)
 await screen.findByText(`authenticated:${before}`)
 vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
 fireEvent.click(screen.getByText('logout'))
 await screen.findByText('anonymous:none')
 vi.mocked(apiClient.get).mockResolvedValue(identity(after))
 fireEvent.click(screen.getByText('login'))
 await screen.findByText(`authenticated:${after}`)
 expect(screen.queryByText(`authenticated:${before}`)).not.toBeInTheDocument()
 expect(apiClient.post).toHaveBeenCalledWith('/api/auth/logout', {})
})
test('restored browser page revalidates the current cookie and discards old identity', async () => {
 vi.mocked(apiClient.get).mockResolvedValue(identity('ADMINISTRADOR'))
 render(<SessionProvider><Switcher /></SessionProvider>)
 await screen.findByText('authenticated:ADMINISTRADOR')
 vi.mocked(apiClient.get).mockResolvedValue(identity('CUSTOMER'))
 fireEvent(window, new PageTransitionEvent('pageshow', { persisted: true }))
 await waitFor(() => expect(screen.getByText('authenticated:CUSTOMER')).toBeInTheDocument())
 expect(screen.queryByText('authenticated:ADMINISTRADOR')).not.toBeInTheDocument()
})

test('one precedence is used for legacy multi-role homes', () => {
 expect(homeForRoles(['CUSTOMER', 'ADMINISTRADOR'])).toBe('/admin')
 expect(homeForRoles(['PROFISSIONAL', 'GERENTE'])).toBe('/recepcao')
 expect(homeForRoles([])).toBe('/acesso-negado')
 expect(homeForRoles(['PROFESSIONAL_APPLICANT'])).toBe('/profissional/aguardando')
})
test('a delayed old session cannot overwrite a newer session', async () => {
 let finishOld!: (value: ReturnType<typeof identity>) => void
 vi.mocked(apiClient.get).mockImplementationOnce(() => new Promise(resolve => { finishOld = resolve as typeof finishOld }))
 render(<SessionProvider><Switcher /></SessionProvider>)
 vi.mocked(apiClient.get).mockResolvedValue(identity('CUSTOMER'))
 fireEvent(window, new PageTransitionEvent('pageshow', { persisted: true }))
 await screen.findByText('authenticated:CUSTOMER')
 finishOld(identity('ADMINISTRADOR'))
 await waitFor(() => expect(screen.getByText('authenticated:CUSTOMER')).toBeInTheDocument())
})
