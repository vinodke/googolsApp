import { NavLink, Navigate, Outlet } from 'react-router'
import { useAuth } from '../core/auth/auth'
import { strings } from '../core/i18n/strings'
import { useStudentMe } from '../core/queries'
import { useLearningHub } from '../core/realtime/useLearningHub'
import './layout.css'

export function StudentLayout() {
  const { user, logout } = useAuth()
  const me = useStudentMe()
  useLearningHub(!!me.data)

  if (me.isLoading) return <div className="page-loading" aria-busy="true"><div className="skeleton" style={{ width: 240, height: 24 }} /></div>
  if (me.data === null) return <Navigate to="/onboarding" replace />

  const t = strings[me.data?.preferredLanguage ?? 'en']
  const links = [
    { to: '/', label: t.navHome, icon: '🏠', end: true },
    { to: '/subjects', label: t.navSubjects, icon: '📚' },
    { to: '/ai-teacher', label: t.navAiTeacher, icon: '✨' },
  ]

  return (
    <div className="shell">
      <aside className="sidebar" aria-label="Main navigation">
        <div className="brand">Googols</div>
        <nav>
          {links.map(l => (
            <NavLink key={l.to} to={l.to} end={l.end} className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}>
              <span aria-hidden>{l.icon}</span> {l.label}
            </NavLink>
          ))}
        </nav>
      </aside>
      <div className="main">
        <header className="topbar">
          {me.data && (
            <div className="topbar-stats">
              <span className="badge">{t.level} {me.data.level}</span>
              <span className="badge">⭐ {me.data.totalXp} {t.xp}</span>
              {me.data.streakDays > 0 && <span className="badge">🔥 {t.streak(me.data.streakDays)}</span>}
            </div>
          )}
          <div className="topbar-user">
            <span className="muted">{user?.email}</span>
            <button className="btn btn-ghost" onClick={() => void logout()}>{t.signOut}</button>
          </div>
        </header>
        <main className="content">
          {me.isError ? <div className="alert" role="alert">Could not load your profile. Please refresh.</div> : <Outlet />}
        </main>
      </div>
    </div>
  )
}
