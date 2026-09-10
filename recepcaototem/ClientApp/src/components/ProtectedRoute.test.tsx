import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useEffect, useState } from 'react'
import { MemoryRouter, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
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

test('a background session revalidation keeps the protected subtree mounted and its edits', async () => {
 vi.mocked(apiClient.get).mockResolvedValue(identity('ADMINISTRADOR'))
 let mounts = 0
 function Child() {
  useEffect(() => { mounts += 1 }, [])
  const [text, setText] = useState('')
  return <input aria-label="draft" value={text} onChange={(event) => setText(event.target.value)} />
 }
 render(<MemoryRouter initialEntries={['/admin']}><SessionProvider><Routes>
  <Route element={<ProtectedRoute allowedRoles={['ADMINISTRADOR']} />}>
   <Route path="/admin" element={<Child />} />
  </Route>
 </Routes></SessionProvider></MemoryRouter>)
 const input = await screen.findByLabelText<HTMLInputElement>('draft')
 fireEvent.change(input, { target: { value: 'unsaved work' } })
 expect(mounts).toBe(1)
 const before = vi.mocked(apiClient.get).mock.calls.length
 fireEvent(window, new Event('focus'))
 await waitFor(() => expect(vi.mocked(apiClient.get).mock.calls.length).toBeGreaterThan(before))
 await waitFor(() => expect(screen.getByLabelText('draft')).toHaveValue('unsaved work'))
 expect(mounts).toBe(1)
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

// --- Sidebar/shell must not remount on internal navigation (staging white-flash fix) ---

function Shell({ onMount }: { onMount: () => void }) {
 useEffect(() => { onMount() }, [onMount])
 return <div data-testid="admin-shell">shell<Outlet /></div>
}
function GoTo({ to }: { to: string }) {
 const navigate = useNavigate()
 return <button onClick={() => navigate(to)}>go {to}</button>
}
function renderAdminShell(mountSpy: () => void) {
 return render(
  <MemoryRouter initialEntries={['/admin/salas']}><SessionProvider><Routes>
   <Route element={<ProtectedRoute allowedRoles={['ADMINISTRADOR']} />}>
    <Route path="/admin" element={<Shell onMount={mountSpy} />}>
     <Route path="salas" element={<><p>salas page</p><GoTo to="/admin/visitas" /><GoTo to="/admin/reservas" /></>} />
     <Route path="visitas" element={<><p>visitas page</p><GoTo to="/admin/reservas" /></>} />
     <Route path="reservas" element={<><p>reservas page</p><GoTo to="/admin/profissionais" /></>} />
     <Route path="profissionais" element={<p>profissionais page</p>} />
    </Route>
   </Route>
   <Route path="/login" element={<p>login page</p>} />
   <Route path="/change-password" element={<p>change password page</p>} />
  </Routes></SessionProvider></MemoryRouter>,
 )
}

test('first protected load blocks with route-loading until the session is validated', async () => {
 const pending: Array<(value: unknown) => void> = []
 vi.mocked(apiClient.get).mockImplementation(() => new Promise(resolve => { pending.push(resolve as (value: unknown) => void) }))
 renderAdminShell(() => {})
 expect(screen.getByRole('status')).toHaveTextContent('Carregando')
 expect(screen.queryByText('salas page')).not.toBeInTheDocument()
 expect(screen.queryByTestId('admin-shell')).not.toBeInTheDocument()
 await act(async () => { pending.forEach(resolve => resolve(identity('ADMINISTRADOR'))) })
 expect(await screen.findByText('salas page')).toBeInTheDocument()
 expect(screen.getByTestId('admin-shell')).toBeInTheDocument()
})

test('navigating between child routes keeps the shell mounted, shows no route-loading, and does at most one background refresh per navigation', async () => {
 vi.mocked(apiClient.get).mockResolvedValue(identity('ADMINISTRADOR'))
 let shellMounts = 0
 renderAdminShell(() => { shellMounts += 1 })
 await screen.findByText('salas page')
 expect(shellMounts).toBe(1)
 const callsAfterFirstLoad = vi.mocked(apiClient.get).mock.calls.length

 fireEvent.click(screen.getByText('go /admin/visitas'))
 expect(await screen.findByText('visitas page')).toBeInTheDocument()
 expect(screen.queryByRole('status')).not.toBeInTheDocument()
 expect(screen.getByTestId('admin-shell')).toBeInTheDocument()
 expect(shellMounts).toBe(1)

 fireEvent.click(screen.getByText('go /admin/reservas'))
 expect(await screen.findByText('reservas page')).toBeInTheDocument()
 expect(screen.queryByRole('status')).not.toBeInTheDocument()
 expect(shellMounts).toBe(1)

 fireEvent.click(screen.getByText('go /admin/profissionais'))
 expect(await screen.findByText('profissionais page')).toBeInTheDocument()
 expect(screen.queryByRole('status')).not.toBeInTheDocument()
 expect(shellMounts).toBe(1)

 // exactly one /api/auth/session per navigation, no duplicate for the already-validated first key
 await waitFor(() => expect(vi.mocked(apiClient.get).mock.calls.length).toBe(callsAfterFirstLoad + 3))
})

test('an internal navigation triggers at most one background session refresh', async () => {
 vi.mocked(apiClient.get).mockResolvedValue(identity('ADMINISTRADOR'))
 renderAdminShell(() => {})
 await screen.findByText('salas page')
 const before = vi.mocked(apiClient.get).mock.calls.length
 fireEvent.click(screen.getByText('go /admin/visitas'))
 await screen.findByText('visitas page')
 await waitFor(() => expect(vi.mocked(apiClient.get).mock.calls.length).toBeGreaterThan(before))
 // settle any trailing microtasks, then assert it never exceeded one extra call
 await act(async () => {})
 expect(vi.mocked(apiClient.get).mock.calls.length).toBe(before + 1)
})

test('a background refresh that returns 401 redirects to /login and drops the protected shell', async () => {
 vi.mocked(apiClient.get).mockResolvedValue(identity('ADMINISTRADOR'))
 renderAdminShell(() => {})
 await screen.findByText('salas page')
 vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
 fireEvent.click(screen.getByText('go /admin/visitas'))
 expect(await screen.findByText('login page')).toBeInTheDocument()
 expect(screen.queryByText('visitas page')).not.toBeInTheDocument()
 expect(screen.queryByTestId('admin-shell')).not.toBeInTheDocument()
})

test('mustChangePassword still redirects out of the protected area', async () => {
 vi.mocked(apiClient.get).mockResolvedValue({ ...identity('ADMINISTRADOR'), mustChangePassword: true })
 renderAdminShell(() => {})
 expect(await screen.findByText('change password page')).toBeInTheDocument()
 expect(screen.queryByText('salas page')).not.toBeInTheDocument()
 expect(screen.queryByTestId('admin-shell')).not.toBeInTheDocument()
})

test('role guard still redirects a disallowed role after the one-time validation', async () => {
 vi.mocked(apiClient.get).mockResolvedValue(identity('CUSTOMER'))
 render(<MemoryRouter initialEntries={['/admin/salas']}><SessionProvider><Routes>
  <Route element={<ProtectedRoute allowedRoles={['ADMINISTRADOR']} />}>
   <Route path="/admin" element={<Shell onMount={() => {}} />}>
    <Route path="salas" element={<p>salas page</p>} />
   </Route>
  </Route>
  <Route path="/cliente" element={<p>customer home</p>} />
 </Routes></SessionProvider></MemoryRouter>)
 expect(await screen.findByText('customer home')).toBeInTheDocument()
 expect(screen.queryByText('salas page')).not.toBeInTheDocument()
 expect(screen.queryByTestId('admin-shell')).not.toBeInTheDocument()
})

// --- returnUrl is carried into the customer login redirect, never the staff one ---

test('anonymous on a /cliente route redirects to /cliente/login with an encoded returnUrl', async () => {
 vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
 function CustomerLoginSink() {
  const location = useLocation()
  return <p data-testid="customer-login-sink">{location.search}</p>
 }
 render(<MemoryRouter initialEntries={['/cliente/agendar?professionalId=abc']}><SessionProvider><Routes>
  <Route element={<ProtectedRoute allowedRoles={['CUSTOMER']} />}>
   <Route path="/cliente/agendar" element={<p>booking content</p>} />
  </Route>
  <Route path="/cliente/login" element={<CustomerLoginSink />} />
 </Routes></SessionProvider></MemoryRouter>)
 const sink = await screen.findByTestId('customer-login-sink')
 expect(sink.textContent).toBe('?returnUrl=%2Fcliente%2Fagendar%3FprofessionalId%3Dabc')
 expect(decodeURIComponent(new URLSearchParams(sink.textContent ?? '').get('returnUrl') ?? '')).toBe('/cliente/agendar?professionalId=abc')
 expect(screen.queryByText('booking content')).not.toBeInTheDocument()
})

test('anonymous on a staff route redirects to /login WITHOUT returnUrl', async () => {
 vi.mocked(apiClient.get).mockRejectedValue(new ApiError(401, 'UNAUTHORIZED', ''))
 function StaffLoginSink() {
  const location = useLocation()
  return <p data-testid="staff-login-sink">{`search=${location.search}`}</p>
 }
 render(<MemoryRouter initialEntries={['/admin']}><SessionProvider><Routes>
  <Route element={<ProtectedRoute allowedRoles={['ADMINISTRADOR']} />}>
   <Route path="/admin" element={<p>admin content</p>} />
  </Route>
  <Route path="/login" element={<StaffLoginSink />} />
 </Routes></SessionProvider></MemoryRouter>)
 const sink = await screen.findByTestId('staff-login-sink')
 expect(sink.textContent).toBe('search=')
 expect(sink.textContent).not.toContain('returnUrl')
 expect(screen.queryByText('admin content')).not.toBeInTheDocument()
})
