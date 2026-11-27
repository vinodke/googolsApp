import { useEffect, useState, type ReactNode } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router'
import { Icon } from '../../app/Icons'
import { useTheme } from '../../app/theme'
import { useAuth } from '../../core/auth/auth'
import './site.css'

const MENU = [
  { to: '/home', label: 'Home', end: true },
  { to: '/features', label: 'Features' },
  { to: '/how-it-works', label: 'How it works' },
  { to: '/about', label: 'About us' },
  { to: '/contact', label: 'Contact' },
]

/** Public website shell: sticky glass menu (collapses to a drawer on phones) and footer. */
export function SiteLayout({ children }: { children?: ReactNode }) {
  const { user } = useAuth()
  const { theme, toggle } = useTheme()
  const [open, setOpen] = useState(false)
  const { pathname } = useLocation()
  useEffect(() => { setOpen(false); window.scrollTo?.(0, 0) }, [pathname])

  return (
    <div className="site">
      <header className="site-nav">
        <div className="site-nav-inner">
          <Link to="/" className="brand" aria-label="Googols home">
            <span className="brand-mark" aria-hidden>G</span><span className="brand-name">Googols</span>
          </Link>
          <nav className={`site-menu${open ? ' open' : ''}`} aria-label="Website">
            {MENU.map(m => (
              <NavLink key={m.to} to={m.to} end={m.end} className={({ isActive }) => `site-link${isActive ? ' active' : ''}`}>{m.label}</NavLink>
            ))}
            <div className="site-menu-actions">
              {user ? (
                <Link to="/" className="btn btn-primary btn-sm">Open my dashboard</Link>
              ) : (
                <>
                  <Link to="/login" className="btn btn-ghost btn-sm">Log in</Link>
                  <Link to="/register" className="btn btn-primary btn-sm">Sign up free</Link>
                </>
              )}
            </div>
          </nav>
          <div className="row" style={{ gap: 'var(--space-2)' }}>
            <button className="icon-btn" onClick={toggle} aria-label={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}>
              <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={18} />
            </button>
            <button className="icon-btn site-burger" aria-label="Menu" aria-expanded={open} onClick={() => setOpen(o => !o)}>
              <span aria-hidden>{open ? '✕' : '☰'}</span>
            </button>
          </div>
        </div>
      </header>

      <main className="site-main">{children ?? <Outlet />}</main>

      <footer className="site-footer">
        <div className="site-footer-inner">
          <div>
            <Link to="/" className="brand"><span className="brand-mark" aria-hidden>G</span><span className="brand-name">Googols</span></Link>
            <p className="muted small" style={{ maxWidth: 320, marginTop: 'var(--space-3)' }}>
              Learn Smarter. Grow Further. Achieve Anything. An AI learning companion for school students in Kerala, in Malayalam and English.
            </p>
          </div>
          <div>
            <strong>Explore</strong>
            <ul>{MENU.slice(1).map(m => <li key={m.to}><Link to={m.to}>{m.label}</Link></li>)}</ul>
          </div>
          <div>
            <strong>Get started</strong>
            <ul>
              <li><Link to="/register">Sign up free</Link></li>
              <li><Link to="/login">Log in</Link></li>
              <li><Link to="/parent/link">For parents</Link></li>
            </ul>
          </div>
          <div>
            <strong>Legal</strong>
            <ul>
              <li><Link to="/privacy">Privacy policy</Link></li>
              <li><Link to="/contact">Contact us</Link></li>
            </ul>
          </div>
        </div>
        <div className="site-footer-bottom muted small">© {new Date().getFullYear()} Googols · googols.in</div>
      </footer>
    </div>
  )
}
