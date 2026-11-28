import { useEffect, useState, type ReactNode } from 'react'
import { Link, NavLink, matchPath, useLocation, useNavigate } from 'react-router'
import { useAuth } from '../core/auth/auth'
import { Icon, type IconName } from './Icons'
import { useTheme } from './theme'
import './layout.css'

export interface NavItem { to: string; label: string; icon: IconName; end?: boolean }

/** How many destinations fit in the phone tab bar before the rest move into "More". */
const MAX_TABS = 5

const isActive = (l: NavItem, path: string) => !!matchPath({ path: l.to, end: l.end ?? false }, path)

/** Titles for detail screens that are not tabs themselves. */
const DETAIL_TITLES: [RegExp, string][] = [
  [/^\/lessons\//, 'Lesson'], [/^\/chapters\/[^/]+\/quiz/, 'Quiz'], [/^\/chapters\//, 'Chapter'], [/^\/subjects\/./, 'Subject'],
  [/^\/tests\/attempts/, 'Test'], [/^\/plans/, 'Plans'], [/^\/progress\/./, 'Progress'],
]

/**
 * Shared shell for every signed-in area. Desktop: floating sidebar + glass top bar.
 * Phone: app-style header with the page title, a bottom tab bar and a "More" sheet for the rest.
 */
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
  const { pathname } = useLocation()
  const navigate = useNavigate()
  // The sheet remembers the page it was opened on, so navigating anywhere closes it.
  const [sheetPath, setSheetPath] = useState<string | null>(null)
  const sheetOpen = sheetPath === pathname
  const setSheetOpen = (open: boolean) => setSheetPath(open ? pathname : null)
  const email = user?.email ?? ''
  const initial = email.charAt(0).toUpperCase() || '?'

  const tabs = links.length <= MAX_TABS ? links : links.slice(0, MAX_TABS - 1)
  const more = links.length <= MAX_TABS ? [] : links.slice(MAX_TABS - 1)
  // Most specific match wins, so "/admin/users" is titled "Users" rather than "Dashboard".
  const current = links.filter(l => isActive(l, pathname)).sort((a, b) => b.to.length - a.to.length)[0]
  const moreActive = more.some(l => l === current)
  // A screen that is not itself a destination (lesson, chapter, test...) gets a back button, like a native app.
  const isTopLevel = links.some(l => !!matchPath({ path: l.to, end: true }, pathname))
  const detailTitle = DETAIL_TITLES.find(([re]) => re.test(pathname))?.[1]
  const title = isTopLevel ? current?.label : detailTitle ?? current?.label
  const goBack = () => { if (window.history.length > 1) navigate(-1); else navigate(links[0]?.to ?? '/') }

  useEffect(() => {
    if (!sheetOpen) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setSheetPath(null) }
    document.addEventListener('keydown', onKey)
    document.body.classList.add('no-scroll')
    return () => { document.removeEventListener('keydown', onKey); document.body.classList.remove('no-scroll') }
  }, [sheetOpen])

  const themeLabel = theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'

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
          {isTopLevel
            ? <Link to={links[0]?.to ?? '/'} className="topbar-brand" aria-label="Googols home"><span className="brand-mark" aria-hidden>G</span></Link>
            : <button type="button" className="topbar-brand back-btn" onClick={goBack} aria-label="Back"><Icon name="chevron" size={22} /></button>}
          <div className="topbar-heading">
            <strong>{title ?? 'Googols'}</strong>
            <small>{subtitle}</small>
          </div>
          <div className="topbar-stats">{stats}</div>
          <div className="topbar-right">
            <button className="icon-btn hide-phone" onClick={toggle} aria-label={themeLabel}>
              <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={18} />
            </button>
            <button type="button" className="user-chip" onClick={() => setSheetOpen(true)} aria-label="Account and menu">
              <span className="avatar" aria-hidden>{initial}</span>
              <span className="who">{email}<small>{subtitle}</small></span>
            </button>
            <button className="icon-btn hide-phone" onClick={() => void logout()} aria-label={signOutLabel} title={signOutLabel}>
              <Icon name="logout" size={18} />
            </button>
          </div>
        </header>
        <main className="content fade-in">{children}</main>
      </div>

      <nav className="tabbar" aria-label="Tabs">
        {tabs.map(l => (
          <NavLink key={l.to} to={l.to} end={l.end} className={({ isActive }) => `tab${isActive ? ' active' : ''}`}>
            <span className="tab-icon"><Icon name={l.icon} size={22} /></span>
            <span className="tab-label">{l.label}</span>
          </NavLink>
        ))}
        {more.length > 0 && (
          <button type="button" className={`tab${moreActive || sheetOpen ? ' active' : ''}`} onClick={() => setSheetOpen(true)}
            aria-haspopup="dialog" aria-expanded={sheetOpen} aria-label="More">
            <span className="tab-icon"><Icon name="grid" size={22} /></span>
            <span className="tab-label">{moreActive ? current?.label : 'More'}</span>
          </button>
        )}
      </nav>

      {sheetOpen && (
        <div className="sheet-layer">
          <button type="button" className="sheet-backdrop" aria-label="Close menu" onClick={() => setSheetOpen(false)} />
          <div className="sheet" role="dialog" aria-modal="true" aria-label="Menu">
            <div className="sheet-handle" aria-hidden />
            <div className="sheet-account">
              <span className="avatar avatar-lg" aria-hidden>{initial}</span>
              <div className="sheet-who"><strong>{email || 'Signed in'}</strong><small>{subtitle}</small></div>
              <button type="button" className="icon-btn" onClick={() => setSheetOpen(false)} aria-label="Close menu"><Icon name="x" size={18} /></button>
            </div>
            {stats && <div className="sheet-stats">{stats}</div>}
            {more.length > 0 && (
              <nav className="sheet-grid" aria-label={sectionLabel}>
                {more.map(l => (
                  <NavLink key={l.to} to={l.to} end={l.end} className={({ isActive }) => `sheet-tile${isActive ? ' active' : ''}`}>
                    <span className="sheet-tile-icon"><Icon name={l.icon} size={22} /></span>
                    <span>{l.label}</span>
                  </NavLink>
                ))}
              </nav>
            )}
            <div className="sheet-list">
              <button type="button" className="sheet-row" onClick={toggle}>
                <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={20} />
                <span>{theme === 'dark' ? 'Light mode' : 'Dark mode'}</span>
                <span className={`toggle-pill${theme === 'dark' ? ' on' : ''}`} aria-hidden />
              </button>
              <button type="button" className="sheet-row danger" onClick={() => void logout()}>
                <Icon name="logout" size={20} /><span>{signOutLabel}</span>
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export function PageLoading() {
  return <div className="page-loading" aria-busy="true"><div className="skeleton" style={{ width: 240, height: 24 }} /></div>
}
