import { NavLink, Navigate, Outlet } from 'react-router'
import { useAuth } from '../core/auth/auth'
import { isAdmin, useAccount } from '../core/queries'
import './layout.css'

/** Shell for content managers and admins. Non-admins are sent back to the student area. */
export function AdminLayout() {
  const { user, logout } = useAuth()
  const account = useAccount()

  if (account.isLoading) return <div className="page-loading" aria-busy="true"><div className="skeleton" style={{ width: 240, height: 24 }} /></div>
  if (!isAdmin(account.data)) return <Navigate to="/" replace />

  return (
    <div className="shell">
      <aside className="sidebar" aria-label="Admin navigation">
        <div className="brand">Googols Admin</div>
        <nav>
          <NavLink to="/admin" end className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}>
            <span aria-hidden>📚</span> Curriculum
          </NavLink>
        </nav>
      </aside>
      <div className="main">
        <header className="topbar">
          <div className="topbar-stats">
            {account.data?.roles.map(r => <span key={r} className="badge">{r}</span>)}
          </div>
          <div className="topbar-user">
            <span className="muted">{user?.email}</span>
            <button className="btn btn-ghost" onClick={() => void logout()}>Sign out</button>
          </div>
        </header>
        <main className="content"><Outlet /></main>
      </div>
    </div>
  )
}
