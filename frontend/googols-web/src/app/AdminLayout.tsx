import { Navigate, Outlet } from 'react-router'
import { isAdmin, useAccount, useStudentMe } from '../core/queries'
import { AppShell, PageLoading, type NavItem } from './AppShell'

/** Shell for content managers and admins. Non-admins are sent back to the student area. */
export function AdminLayout() {
  const account = useAccount()
  const me = useStudentMe()

  if (account.isLoading) return <PageLoading />
  if (!isAdmin(account.data)) return <Navigate to="/" replace />

  const links: NavItem[] = [
    { to: '/admin', label: 'Curriculum', icon: 'layers', end: true },
    { to: '/teacher', label: 'Teacher view', icon: 'grad' },
    ...(me.data ? [{ to: '/', label: 'Student view', icon: 'home', end: true } as NavItem] : []),
  ]
  const stats = account.data?.roles.map(r => <span key={r} className="badge">{r}</span>)

  return (
    <AppShell subtitle="Admin console" sectionLabel="Manage" links={links} stats={stats} signOutLabel="Sign out">
      <Outlet />
    </AppShell>
  )
}
