import { render, screen, waitFor } from '@testing-library/react'
import { vi } from 'vitest'
import { SessionProvider, useSession } from './SessionProvider'

function Consumer() {
  const session = useSession()
  return <div>{session.status}:{session.user?.displayName ?? 'none'}</div>
}

test('loads the authenticated session from the same-origin API', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
    userId: 'u1', displayName: 'Admin Real', email: 'admin@lumis.test', roles: ['ADMINISTRADOR'], mustChangePassword: false,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
  render(<SessionProvider><Consumer /></SessionProvider>)
  await waitFor(() => expect(screen.getByText('authenticated:Admin Real')).toBeInTheDocument())
  expect(fetch).toHaveBeenCalledWith('/api/auth/session', expect.objectContaining({ credentials: 'same-origin' }))
  expect(localStorage.getItem('atrium_session')).toBeNull()
})

test('represents a forced password change separately from a normal session', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
    userId: 'u2', displayName: 'New User', email: 'new@lumis.test', roles: ['GERENTE'], mustChangePassword: true,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
  render(<SessionProvider><Consumer /></SessionProvider>)
  await waitFor(() => expect(screen.getByText('mustChangePassword:New User')).toBeInTheDocument())
})
