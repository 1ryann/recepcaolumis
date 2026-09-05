import { createContext, type ReactNode, useContext, useEffect, useMemo, useState } from 'react'
import { apiClient } from '../api/client'

export type SessionUser = { userId: string; displayName: string; email: string; roles: string[]; mustChangePassword: boolean }
type SessionStatus = 'loading' | 'anonymous' | 'mustChangePassword' | 'authenticated'
type SessionContextValue = {
  status: SessionStatus
  user: SessionUser | null
  refresh(): Promise<void>
  login(email: string, password: string): Promise<void>
  logout(): Promise<void>
  changePassword(currentPassword: string, newPassword: string, confirmation: string): Promise<void>
}

const SessionContext = createContext<SessionContextValue | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<SessionStatus>('loading')
  const [user, setUser] = useState<SessionUser | null>(null)

  const refresh = async () => {
    try {
      const current = await apiClient.get<SessionUser>('/api/auth/session')
      setUser(current)
      setStatus(current.mustChangePassword ? 'mustChangePassword' : 'authenticated')
    } catch {
      setUser(null)
      setStatus('anonymous')
    }
  }
  const login = async (email: string, password: string) => { await apiClient.post('/api/auth/login', { email, password }); await refresh() }
  const logout = async () => { await apiClient.post('/api/auth/logout', {}); setUser(null); setStatus('anonymous') }
  const changePassword = async (currentPassword: string, newPassword: string, confirmation: string) => {
    await apiClient.post('/api/auth/change-password', { currentPassword, newPassword, confirmation })
    await refresh()
  }

  useEffect(() => { void refresh() }, [])
  const value = useMemo(() => ({ status, user, refresh, login, logout, changePassword }), [status, user])
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}

export function useSession() {
  const value = useContext(SessionContext)
  if (!value) throw new Error('useSession must be used inside SessionProvider')
  return value
}
