import type { ReactNode } from 'react'
import { Link, NavLink } from 'react-router'
import { useAuth } from '../core/auth/auth'
import { Icon, type IconName } from './Icons'
import { useTheme } from './theme'
import './layout.css'

export interface NavItem { to: string; label: string; icon: IconName; end?: boolean }

/** Shared shell for the student and admin areas: floating sidebar, glass top bar, theme toggle. */
export function AppShell({ subtitle, sectionLabel, links, stats, signOutLabel, footer, children }: {
  subtitle: string
  sectionLabel: string
  links: NavItem[]
  stats?: ReactNode
  signOutLabel: string
  footer?: ReactNode
  children: ReactNode
}) {
  const { user, logout } = useAuth()
  const { theme, toggle } = useTheme()
  const email = user?.email ?? ''

  return (
    <div className="shell">
      <aside className="sidebar" aria-label="Main navigation">
        <div className="sidebar-inner">
          <Link to="/" className="brand">
            <span className="brand-mark" aria-hidden>G</span>
            <span><span className="brand-name">Googols</span><span className="brand-sub">{subtitle}</span></span>
          </Link>
          <div>
            <div className="nav-section">{sectionLabel}</div>
            <nav>
              {links.map(l => (
                <NavLink key={l.to} to={l.to} end={l.end} className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}>
                  <Icon name={l.icon} /> <span>{l.label}</span>
                </NavLink>
              ))}
            </nav>
          </div>
          {footer && <div className="sidebar-foot">{footer}</div>}
        </div>
      </aside>
      <div className="main">
        <header className="topbar">
          <div className="topbar-stats">{stats}</div>
          <div className="topbar-right">
            <button className="icon-btn" onClick={toggle} aria-label={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}>
              <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={18} />
            </button>
            <div className="user-chip">
              <span className="avatar" aria-hidden>{email.charAt(0).toUpperCase() || '?'}</span>
              <span className="who">{email}<small>{subtitle}</small></span>
            </div>
            <button className="icon-btn" onClick={() => void logout()} aria-label={signOutLabel} title={signOutLabel}>
              <Icon name="logout" size={18} />
            </button>
          </div>
        </header>
        <main className="content fade-in">{children}</main>
      </div>
    </div>
  )
}

export function PageLoading() {
  return <div className="page-loading" aria-busy="true"><div className="skeleton" style={{ width: 240, height: 24 }} /></div>
}
