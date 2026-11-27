import { useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router'
import { api, errorMessage } from '../../core/api/client'
import { useAuth } from '../../core/auth/auth'
import { strings } from '../../core/i18n/strings'
import { AuthShell } from '../../app/AuthShell'

const t = strings.en

function AuthForm({ mode }: { mode: 'login' | 'register' }) {
  const { user, login, register } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [params] = useSearchParams()
  const providers = useQuery({ queryKey: ['auth', 'providers'], queryFn: () => api.get<string[]>('/auth/providers'), staleTime: Infinity })
  const googleError = GOOGLE_ERRORS[params.get('error') ?? '']

  if (user) return <Navigate to="/" replace />

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      if (mode === 'register') await register(email, password)
      await login(email, password)
      navigate('/', { replace: true })
    } catch (err) {
      setError(mode === 'login' ? 'Email or password is incorrect, or the account is locked for 15 minutes.' : errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthShell>
      <form className="card auth-card" onSubmit={submit} noValidate>
        <h1>{mode === 'login' ? t.signIn : t.createAccount}</h1>
        <p className="muted">{t.appTagline}</p>
        {(error ?? googleError) && <div className="alert" role="alert">{error ?? googleError}</div>}
        {providers.data?.includes('Google') && (
          <>
            <a className="btn btn-ghost google-btn" href="/api/v1/auth/external/google?returnUrl=%2F">
              <GoogleMark /> Continue with Google
            </a>
            <div className="or"><span>or use email</span></div>
          </>
        )}
        <div className="field">
          <label htmlFor="email">{t.email}</label>
          <input id="email" className="input" type="email" autoComplete="email" required value={email} onChange={e => setEmail(e.target.value)} />
        </div>
        <div className="field">
          <label htmlFor="password">{t.password}</label>
          <input id="password" className="input" type="password" minLength={8} required
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'} value={password} onChange={e => setPassword(e.target.value)} />
          {mode === 'register' && <span className="hint">At least 8 characters, with a capital letter, a number and a symbol.</span>}
        </div>
        <button className="btn btn-primary" style={{ width: '100%' }} disabled={busy || !email || !password}>
          {busy ? '…' : mode === 'login' ? t.signIn : t.createAccount}
        </button>
        <p className="muted small" style={{ textAlign: 'center', marginTop: 'var(--space-4)' }}>
          {mode === 'login' ? <Link to="/register">{t.createAccount}</Link> : <Link to="/login">{t.signIn}</Link>}
          {' · '}<Link to="/privacy">Privacy</Link>
        </p>
      </form>
    </AuthShell>
  )
}

const GOOGLE_ERRORS: Record<string, string> = {
  google: 'Google sign-in didn\'t finish. Please try again.',
  'google-email': 'Your Google account didn\'t share an email address.',
  'google-unverified': 'That Google email isn\'t verified, so it can\'t be linked to an existing account. Sign in with your password instead.',
  locked: 'This account is locked. Try again later or contact support.',
}

/** Google "G" logo (official colours). */
function GoogleMark() {
  return (
    <svg width="18" height="18" viewBox="0 0 48 48" aria-hidden="true">
      <path fill="#FFC107" d="M43.6 20.5H42V20H24v8h11.3C33.7 32.7 29.2 36 24 36c-6.6 0-12-5.4-12-12s5.4-12 12-12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 12.9 4 4 12.9 4 24s8.9 20 20 20 20-8.9 20-20c0-1.3-.1-2.4-.4-3.5z" />
      <path fill="#FF3D00" d="M6.3 14.7l6.6 4.8C14.7 15.1 19 12 24 12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 16.3 4 9.7 8.3 6.3 14.7z" />
      <path fill="#4CAF50" d="M24 44c5.2 0 9.9-2 13.4-5.2l-6.2-5.2C29.2 35.1 26.7 36 24 36c-5.2 0-9.6-3.3-11.3-7.9l-6.5 5C9.5 39.6 16.2 44 24 44z" />
      <path fill="#1976D2" d="M43.6 20.5H42V20H24v8h11.3c-.8 2.2-2.2 4.1-4.1 5.6l6.2 5.2C37 39.2 44 34 44 24c0-1.3-.1-2.4-.4-3.5z" />
    </svg>
  )
}

export const LoginPage = () => <AuthForm mode="login" />
export const RegisterPage = () => <AuthForm mode="register" />
