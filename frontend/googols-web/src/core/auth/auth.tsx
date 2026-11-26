import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from '../api/client'

// ASP.NET Core Identity endpoints mapped at /api/v1/auth (MapIdentityApi).
interface Me { email: string; isEmailConfirmed: boolean }

interface AuthState {
  user: Me | null
  loading: boolean
  login: (email: string, password: string) => Promise<void>
  register: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<Me | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    try {
      setUser(await api.get<Me>('/auth/manage/info'))
    } catch {
      setUser(null)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh])

  const value = useMemo<AuthState>(() => ({
    user,
    loading,
    login: async (email, password) => {
      await api.post('/auth/login?useCookies=true', { email, password })
      await refresh()
    },
    register: async (email, password) => {
      await api.post('/auth/register', { email, password })
    },
    logout: async () => {
      await api.post('/auth/logout').catch(() => undefined) // server clears the HttpOnly cookie
      setUser(null)
    },
  }), [user, loading, refresh])

  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}
