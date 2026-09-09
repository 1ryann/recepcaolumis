import { createContext, type ReactNode, useCallback, useContext, useEffect, useRef, useState } from 'react'
import { apiClient, ApiError, resetCsrfToken } from '../api/client'

export type SessionUser = { userId: string; displayName: string; email: string; roles: string[]; mustChangePassword: boolean }
type SessionStatus = 'error' | 'loading' | 'anonymous' | 'mustChangePassword' | 'authenticated'
type RefreshOptions = { background?: boolean }
type SessionContextValue = {
  status: SessionStatus
  user: SessionUser | null
  refresh(options?: RefreshOptions): Promise<SessionUser | null>
  login(email: string, password: string): Promise<SessionUser>
  logout(): Promise<void>
  changePassword(currentPassword: string, newPassword: string, confirmation: string): Promise<SessionUser | null>
}
const SessionContext = createContext<SessionContextValue | null>(null)
export function SessionProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<SessionStatus>('loading')
  const [user, setUser] = useState<SessionUser | null>(null)
  const generation = useRef(0)
  const changingAccount = useRef(false)
  const channel = useRef<BroadcastChannel | null>(null)
  const statusRef = useRef(status)
  const revalidating = useRef(false)
  statusRef.current = status
  const invalidate = useCallback(() => { generation.current++; setUser(null); setStatus('loading') }, [])
  const refresh = useCallback(async (options?: RefreshOptions) => {
    const version = ++generation.current
    // A background revalidation (window focus, bfcache restore, other tab) must not tear
    // the app down to a loading state: keep the current identity on screen until the new
    // response arrives, otherwise every focus event remounts the route and drops unsaved work.
    if (!options?.background) setStatus('loading')
    try {
      const current = await apiClient.get<SessionUser>('/api/auth/session')
      if (version !== generation.current) return null
      setUser(current)
      setStatus(current.mustChangePassword ? 'mustChangePassword' : 'authenticated')
      return current
    } catch (error) {
      if (version === generation.current) { setUser(null); setStatus(error instanceof ApiError && error.status === 401 ? 'anonymous' : 'error') }
      return null
    }
  }, [])
  const logout = async () => {
    changingAccount.current = true
    invalidate()
    try {
      try { await apiClient.post('/api/auth/logout', {}) }
      catch (error) { if (!(error instanceof ApiError) || error.status !== 401) throw error }
      resetCsrfToken()
      // Only an explicit 401 confirms that the browser cookie is no longer authenticated.
      try { await apiClient.get<SessionUser>('/api/auth/session') }
      catch (error) {
        if (error instanceof ApiError && error.status === 401) {
          setStatus('anonymous'); channel.current?.postMessage('session-changed'); return
        }
        throw error
      }
      throw new Error('Logout was not confirmed')
    } catch (error) { setStatus('error'); throw error }
    finally { changingAccount.current = false }
  }
  const login = async (email: string, password: string) => {
    if (user) throw new Error('Sign out before switching accounts')
    changingAccount.current = true
    invalidate()
    try {
      await apiClient.post('/api/auth/login', { email, password })
      resetCsrfToken()
      const current = await refresh()
      if (!current) throw new Error('Session could not be confirmed')
      channel.current?.postMessage('session-changed')
      return current
    } catch (error) { setUser(null); setStatus(error instanceof ApiError && error.status === 401 ? 'anonymous' : 'error'); throw error }
    finally { changingAccount.current = false }
  }
  const changePassword = async (currentPassword: string, newPassword: string, confirmation: string) => {
    await apiClient.post('/api/auth/change-password', { currentPassword, newPassword, confirmation })
    resetCsrfToken()
    return refresh()
  }
  useEffect(() => {
    void refresh()
    // Revalidate silently: re-check the cookie without dropping the rendered identity.
    const revalidate = () => { if (!changingAccount.current) void refresh({ background: true }) }
    const restored = (event: PageTransitionEvent) => { if (event.persisted) revalidate() }
    // A 401 from any API call (session expired mid-use): confirm it once against
    // /api/auth/session. If the cookie is really gone, refresh() flips status to
    // 'anonymous' and ProtectedRoute sends the user to login. Guarded so a burst of
    // simultaneous 401s and the confirming call's own 401 cannot loop.
    const onUnauthorized = () => {
      if (changingAccount.current || revalidating.current) return
      if (statusRef.current !== 'authenticated' && statusRef.current !== 'mustChangePassword') return
      revalidating.current = true
      void refresh({ background: true }).finally(() => { revalidating.current = false })
    }
    window.addEventListener('lumis:unauthorized', onUnauthorized)
    window.addEventListener('pageshow', restored)
    window.addEventListener('focus', revalidate)
    if (typeof BroadcastChannel !== 'undefined') {
      channel.current = new BroadcastChannel('lumis-session')
      channel.current.onmessage = revalidate
    }
    return () => {
      generation.current++
      window.removeEventListener('lumis:unauthorized', onUnauthorized)
      window.removeEventListener('pageshow', restored)
      window.removeEventListener('focus', revalidate)
      channel.current?.close(); channel.current = null
    }
  }, [refresh])
  return <SessionContext.Provider value={{ status, user, refresh, login, logout, changePassword }}>{children}</SessionContext.Provider>
}
export function useSession() {
  const value = useContext(SessionContext)
  if (!value) throw new Error('useSession must be used inside SessionProvider')
  return value
}
