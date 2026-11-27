import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { ApiError, api } from '../api/client'

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
  const queryClient = useQueryClient()

  const refresh = useCallback(async () => {
    // Only a 401 means "signed out". A busy server (429/5xx) or a dropped connection is retried,
    // so a hiccup doesn't throw the user back to the login page.
    for (let attempt = 0; ; attempt++) {
      try {
        setUser(await api.get<Me>('/auth/manage/info'))
        break
      } catch (e) {
        const signedOut = e instanceof ApiError && (e.status === 401 || e.status === 403 || e.status === 404)
        if (signedOut || attempt >= 3) {
          setUser(null)
          break
        }
        await new Promise(r => setTimeout(r, 1500 * (attempt + 1)))
      }
    }
    setLoading(false)
  }, [])

  useEffect(() => { void refresh() }, [refresh])

  const value = useMemo<AuthState>(() => ({
    user,
    loading,
    login: async (email, password) => {
      await api.post('/auth/login?useCookies=true', { email, password })
      queryClient.clear() // never show a previous user's cached profile or roles
      await refresh()
    },
    register: async (email, password) => {
      await api.post('/auth/register', { email, password })
    },
    logout: async () => {
      await api.post('/auth/logout').catch(() => undefined) // server clears the HttpOnly cookie
      queryClient.clear()
      setUser(null)
    },
  }), [user, loading, refresh, queryClient])

  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}
