import { Link, Navigate, Outlet } from 'react-router'
import { strings } from '../core/i18n/strings'
import { isAdmin, isParent, isTeacher, useAccount, useStudentMe } from '../core/queries'
import { useLearningHub } from '../core/realtime/useLearningHub'
import { AppShell, PageLoading, type NavItem } from './AppShell'
import { Icon } from './Icons'

export function StudentLayout() {
  const me = useStudentMe()
  const account = useAccount()
  const admin = isAdmin(account.data)
  useLearningHub(!!me.data)

  if (me.isLoading || account.isLoading) return <PageLoading />
  // Accounts without a student profile: admins manage the catalog, parents see their children, others finish setup.
  if (me.data === null) return <Navigate to={admin ? '/admin' : isTeacher(account.data) ? '/teacher' : isParent(account.data) ? '/parent' : '/onboarding'} replace />

  const t = strings[me.data?.preferredLanguage ?? 'en']
  const links: NavItem[] = [
    { to: '/', label: t.navHome, icon: 'home', end: true },
    { to: '/subjects', label: t.navSubjects, icon: 'book' },
    { to: '/ai-teacher', label: t.navAiTeacher, icon: 'sparkles' },
    { to: '/tests', label: t.navTests, icon: 'clipboard' },
    { to: '/progress', label: t.navProgress, icon: 'chart' },
    { to: '/saved', label: t.navSaved, icon: 'bookmark' },
    { to: '/achievements', label: t.navRewards, icon: 'trophy' },
    { to: '/family', label: t.navFamily, icon: 'shield' },
    ...(isParent(account.data) ? [{ to: '/parent', label: 'Parent', icon: 'home' } as NavItem] : []),
    ...(admin ? [{ to: '/admin', label: 'Admin', icon: 'layers' } as NavItem] : []),
  ]

  const stats = me.data && (
    <>
      <span className="badge"><Icon name="trophy" size={14} /> {t.level} {me.data.level}</span>
      <span className="badge badge-warning"><Icon name="star" size={14} /> {me.data.totalXp} {t.xp}</span>
      {me.data.streakDays > 0 && <span className="badge badge-success"><Icon name="flame" size={14} /> {t.streak(me.data.streakDays)}</span>}
    </>
  )

  const footer = (
    <div className="promo">
      <strong>{t.navAiTeacher}</strong>
      <p>{t.askTeacher}</p>
      <Link to="/ai-teacher" className="btn btn-light btn-sm"><Icon name="sparkles" size={16} /> {t.navAiTeacher}</Link>
    </div>
  )

  return (
    <AppShell subtitle={me.data?.enrollment ? `${me.data.enrollment.className} · ${me.data.enrollment.boardCode}` : 'Student'}
      sectionLabel="Menu" links={links} stats={stats} signOutLabel={t.signOut} footer={footer}>
      {me.isError ? <div className="alert" role="alert">Could not load your profile. Please refresh.</div> : <Outlet />}
    </AppShell>
  )
}
