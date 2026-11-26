import { useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate } from 'react-router'
import { errorMessage } from '../../core/api/client'
import { useAuth } from '../../core/auth/auth'
import { strings } from '../../core/i18n/strings'
import '../../app/layout.css'

const t = strings.en

function AuthForm({ mode }: { mode: 'login' | 'register' }) {
  const { user, login, register } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

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
    <div className="auth-page">
      <form className="card auth-card" onSubmit={submit} noValidate>
        <h1>{mode === 'login' ? t.signIn : t.createAccount}</h1>
        <p className="muted">{t.appTagline}</p>
        {error && <div className="alert" role="alert">{error}</div>}
        <div className="field">
          <label htmlFor="email">{t.email}</label>
          <input id="email" className="input" type="email" autoComplete="email" required value={email} onChange={e => setEmail(e.target.value)} />
        </div>
        <div className="field">
          <label htmlFor="password">{t.password}</label>
          <input id="password" className="input" type="password" minLength={8} required
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'} value={password} onChange={e => setPassword(e.target.value)} />
          {mode === 'register' && <span className="muted" style={{ fontSize: 'var(--text-sm)' }}>At least 8 characters, with a capital letter, a number and a symbol.</span>}
        </div>
        <button className="btn btn-primary" style={{ width: '100%' }} disabled={busy || !email || !password}>
          {busy ? '…' : mode === 'login' ? t.signIn : t.createAccount}
        </button>
        <p className="muted" style={{ textAlign: 'center' }}>
          {mode === 'login' ? <Link to="/register">{t.createAccount}</Link> : <Link to="/login">{t.signIn}</Link>}
        </p>
      </form>
    </div>
  )
}

export const LoginPage = () => <AuthForm mode="login" />
export const RegisterPage = () => <AuthForm mode="register" />
