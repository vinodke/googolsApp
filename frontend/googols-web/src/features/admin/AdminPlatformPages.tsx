import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Icon, type IconName } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { DayPoint, Metrics, UsersPage } from '../../core/api/types'
import './admin.css'

const ROLES = ['Student', 'Parent', 'Teacher', 'ContentManager', 'Admin', 'SuperAdmin']

export function AdminDashboardPage() {
  const m = useQuery({ queryKey: ['admin', 'metrics'], queryFn: () => api.get<Metrics>('/admin/metrics') })
  if (m.isLoading) return <div className="skeleton" style={{ height: 300 }} />
  if (m.isError || !m.data) return <div className="alert" role="alert">{errorMessage(m.error)}</div>
  const d = m.data
  const tiles: { label: string; value: string; icon: IconName; tint: string }[] = [
    { label: 'Students', value: `${d.students}`, icon: 'grad', tint: 'tint-indigo' },
    { label: 'Active this week', value: `${d.activeStudents7d}`, icon: 'flame', tint: 'tint-rose' },
    { label: 'Parents · Teachers', value: `${d.parents} · ${d.teachers}`, icon: 'shield', tint: 'tint-teal' },
    { label: 'Published lessons', value: `${d.publishedLessons}`, icon: 'book', tint: 'tint-sky' },
    { label: 'Published questions', value: `${d.publishedQuestions}`, icon: 'bolt', tint: 'tint-amber' },
    { label: 'Quizzes this week', value: `${d.quizAttempts7d}`, icon: 'check', tint: 'tint-lime' },
    { label: 'AI questions this week', value: `${d.aiMessages7d}`, icon: 'sparkles', tint: 'tint-indigo' },
    { label: 'AI tokens this week', value: d.aiTokens7d.toLocaleString('en-IN'), icon: 'layers', tint: 'tint-sky' },
    { label: 'Active subscriptions', value: `${d.activeSubscriptions}`, icon: 'star', tint: 'tint-amber' },
    { label: 'Revenue, 30 days', value: `₹${(d.revenuePaise30d / 100).toLocaleString('en-IN')}`, icon: 'trophy', tint: 'tint-lime' },
  ]
  return (
    <div className="stack-lg">
      <header className="page-head"><div><h1>Dashboard</h1><p className="muted">{d.users} accounts in total. Figures update live.</p></div></header>
      <section className="grid-stats">
        {tiles.map(t => (
          <div key={t.label} className="card stat">
            <span className={`icon-tile lg ${t.tint}`}><Icon name={t.icon} /></span>
            <div><div className="stat-value">{t.value}</div><div className="stat-label">{t.label}</div></div>
          </div>
        ))}
      </section>
      <div className="grid">
        <MiniChart title="AI questions per day" points={d.aiMessagesDaily} />
        <MiniChart title="Lessons completed per day" points={d.lessonsCompletedDaily} />
        <MiniChart title="New students per day" points={d.newStudentsDaily} />
      </div>
    </div>
  )
}

/** 14-day bar chart drawn in SVG; each bar has a native tooltip. */
function MiniChart({ title, points }: { title: string; points: DayPoint[] }) {
  const max = Math.max(1, ...points.map(p => p.value))
  const total = points.reduce((s, p) => s + p.value, 0)
  return (
    <section className="card">
      <div className="card-title"><h2>{title}</h2><span className="badge">{total} in 14 days</span></div>
      <svg viewBox="0 0 280 110" width="100%" role="img" aria-label={`${title}: ${total} in the last 14 days`}>
        {points.map((p, i) => {
          const h = (p.value / max) * 90
          return (
            <g key={p.day}>
              <rect x={i * 20 + 3} y={100 - h} width={14} height={Math.max(h, 1)} rx={4} className="chart-bar"><title>{new Date(p.day).toLocaleDateString()}: {p.value}</title></rect>
            </g>
          )
        })}
        <line x1="0" y1="100.5" x2="280" y2="100.5" className="chart-axis" />
      </svg>
      <div className="row-between small muted"><span>{points[0] && new Date(points[0].day).toLocaleDateString()}</span><span>today</span></div>
    </section>
  )
}

export function AdminUsersPage() {
  const qc = useQueryClient()
  const [search, setSearch] = useState('')
  const [role, setRole] = useState('')
  const [page, setPage] = useState(1)
  const key = ['admin', 'users', search, role, page]
  const users = useQuery({ queryKey: key, queryFn: () => api.get<UsersPage>(`/admin/users?search=${encodeURIComponent(search)}&role=${role}&page=${page}`) })
  const refresh = () => qc.invalidateQueries({ queryKey: ['admin', 'users'] })
  const setRoleM = useMutation({ mutationFn: (v: { id: string; role: string; grant: boolean }) => api.put(`/admin/users/${v.id}/roles`, { role: v.role, grant: v.grant }), onSuccess: refresh })
  const lock = useMutation({ mutationFn: (v: { id: string; locked: boolean }) => api.put(`/admin/users/${v.id}/lock`, { locked: v.locked }), onSuccess: refresh })
  const pages = Math.max(1, Math.ceil((users.data?.total ?? 0) / 25))
  return (
    <div className="stack-lg">
      <header className="page-head"><div><h1>Users</h1><p className="muted">Give teacher or content-manager access, and lock accounts. Role changes apply immediately.</p></div></header>
      <div className="inline-form" style={{ marginTop: 0 }}>
        <input className="input" placeholder="Search by email" aria-label="Search by email" value={search} onChange={e => { setSearch(e.target.value); setPage(1) }} />
        <select className="input narrow" aria-label="Role filter" value={role} onChange={e => { setRole(e.target.value); setPage(1) }}>
          <option value="">All roles</option>{ROLES.map(r => <option key={r}>{r}</option>)}
        </select>
      </div>
      {(setRoleM.isError || lock.isError) && <div className="alert" role="alert">{errorMessage(setRoleM.error ?? lock.error)}</div>}
      <section className="card" style={{ overflowX: 'auto' }}>
        <table className="table">
          <thead><tr><th>Email</th><th>Joined</th><th>Roles</th><th>Give access</th><th /></tr></thead>
          <tbody>
            {users.data?.users.map(u => (
              <tr key={u.id}>
                <td><strong>{u.email}</strong>{u.locked && <span className="badge badge-warning" style={{ marginLeft: 8 }}>Locked</span>}</td>
                <td>{new Date(u.createdAtUtc).toLocaleDateString()}</td>
                <td><div className="row" style={{ gap: 4 }}>{u.roles.map(r => (
                  <button key={r} className="badge" title={`Remove ${r}`} onClick={() => setRoleM.mutate({ id: u.id, role: r, grant: false })}>{r} ✕</button>
                ))}</div></td>
                <td>
                  <select className="input narrow" aria-label={`Add role to ${u.email}`} value="" onChange={e => e.target.value && setRoleM.mutate({ id: u.id, role: e.target.value, grant: true })}>
                    <option value="">Add role…</option>{ROLES.filter(r => !u.roles.includes(r)).map(r => <option key={r}>{r}</option>)}
                  </select>
                </td>
                <td><button className="btn btn-ghost btn-sm" onClick={() => lock.mutate({ id: u.id, locked: !u.locked })}>{u.locked ? 'Unlock' : 'Lock'}</button></td>
              </tr>
            ))}
          </tbody>
        </table>
        {users.data?.users.length === 0 && <p className="muted small">No users match.</p>}
      </section>
      <div className="row-between">
        <button className="btn btn-ghost btn-sm" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Previous</button>
        <span className="small muted">Page {page} of {pages} · {users.data?.total ?? 0} users</span>
        <button className="btn btn-ghost btn-sm" disabled={page >= pages} onClick={() => setPage(p => p + 1)}>Next</button>
      </div>
    </div>
  )
}
