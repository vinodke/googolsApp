import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, Navigate, Outlet, useNavigate } from 'react-router'
import { AppShell, PageLoading, type NavItem } from '../../app/AppShell'
import { Icon, ProgressRing } from '../../app/Icons'
import { xpForLevel } from '../../app/theme'
import { api, errorMessage, fieldErrors } from '../../core/api/client'
import type { Child } from '../../core/api/types'
import { CONSENT_VERSION, isAdmin, useAccount, useChildren, useStudentMe } from '../../core/queries'
import { Empty } from '../student/DashboardPage'
import './parent.css'

export function ParentLayout() {
  const account = useAccount()
  const me = useStudentMe()
  if (account.isLoading) return <PageLoading />
  const links: NavItem[] = [
    { to: '/parent', label: 'My children', icon: 'home', end: true },
    { to: '/parent/link', label: 'Link a child', icon: 'plus' },
    ...(me.data ? [{ to: '/', label: 'Student view', icon: 'book', end: true } as NavItem] : []),
    ...(isAdmin(account.data) ? [{ to: '/admin', label: 'Admin', icon: 'layers' } as NavItem] : []),
  ]
  return (
    <AppShell subtitle="Parent" sectionLabel="Family" links={links} signOutLabel="Sign out">
      <Outlet />
    </AppShell>
  )
}

export function ParentHomePage() {
  const children = useChildren()
  if (children.isLoading) return <div className="skeleton" style={{ height: 200 }} />
  if (children.isError) return <div className="alert" role="alert">{errorMessage(children.error)}</div>
  if (children.data?.length === 0) return <Navigate to="/parent/link" replace />
  return (
    <div className="stack-lg">
      <header className="page-head">
        <div>
          <h1>My children</h1>
          <p className="muted">See progress, and give or withdraw consent for learning and AI features.</p>
        </div>
        <Link className="btn btn-ghost" to="/parent/link"><Icon name="plus" size={18} /> Link another child</Link>
      </header>
      {children.data?.map(c => <ChildCard key={c.studentId} child={c} />)}
    </div>
  )
}

function ChildCard({ child }: { child: Child }) {
  const qc = useQueryClient()
  const [showNotice, setShowNotice] = useState(false)
  const refresh = () => qc.invalidateQueries({ queryKey: ['parent', 'children'] })
  const grant = useMutation({
    mutationFn: () => api.post(`/parents/children/${child.studentId}/consent`, { accept: true, consentVersion: CONSENT_VERSION }),
    onSuccess: () => { setShowNotice(false); void refresh() },
  })
  const withdraw = useMutation({ mutationFn: () => api.del(`/parents/children/${child.studentId}/consent`), onSuccess: refresh })
  const unlink = useMutation({ mutationFn: () => api.del(`/parents/children/${child.studentId}`), onSuccess: refresh })

  const start = xpForLevel(child.level)
  const next = xpForLevel(child.level + 1)
  const progress = (child.totalXp - start) / (next - start)

  return (
    <section className="card">
      <div className="row-between" style={{ marginBottom: 'var(--space-4)' }}>
        <div className="row">
          <span className="avatar" style={{ width: 52, height: 52, fontSize: '1.2rem' }} aria-hidden>{child.displayName.charAt(0).toUpperCase()}</span>
          <div>
            <h2 style={{ margin: 0 }}>{child.displayName}</h2>
            <span className="muted small">{[child.boardCode, child.className, child.academicYear].filter(Boolean).join(' · ') || 'Setup not finished'}</span>
          </div>
        </div>
        {child.consentGranted
          ? <span className="badge badge-success"><Icon name="check" size={14} /> Consent given</span>
          : child.consentRequired ? <span className="badge badge-warning">Consent needed</span> : <span className="badge">Adult account</span>}
      </div>

      <div className="child-grid">
        <div className="grid-stats">
          <Mini icon="trophy" tint="tint-indigo" value={`Level ${child.level}`} label={`${child.totalXp} XP`} />
          <Mini icon="flame" tint="tint-rose" value={`${child.streakDays} days`} label="Learning streak" />
          <Mini icon="sun" tint="tint-amber" value={child.lastActiveDate ? new Date(child.lastActiveDate).toLocaleDateString() : '—'} label="Last active" />
        </div>
        <div style={{ display: 'grid', placeItems: 'center' }}>
          <ProgressRing value={progress} size={110} stroke={10}><strong style={{ fontSize: 'var(--text-xl)' }}>{Math.round(progress * 100)}%</strong><span className="muted small">to level {child.level + 1}</span></ProgressRing>
        </div>
      </div>

      <h3 style={{ marginTop: 'var(--space-5)' }}>Recent rewards</h3>
      {child.recentXp.length === 0 ? <p className="muted small">No rewards yet. They appear when lessons and quizzes are completed.</p> : (
        <ul className="list">
          {child.recentXp.map((r, i) => (
            <li key={i} className="list-item"><span className="grow">{r.reason.replace(/-/g, ' ')}</span><span className="muted small">{new Date(r.createdAtUtc).toLocaleDateString()}</span><strong>+{r.points} XP</strong></li>
          ))}
        </ul>
      )}

      <div className="consent-box">
        {child.consentGranted ? (
          <div className="row-between">
            <span className="small">You gave consent on {child.consentGrantedAtUtc ? new Date(child.consentGrantedAtUtc).toLocaleDateString() : ''} (notice {child.consentVersion}).</span>
            <button className="btn btn-ghost btn-sm" onClick={() => withdraw.mutate()} disabled={withdraw.isPending}>Withdraw consent</button>
          </div>
        ) : showNotice ? (
          <ConsentNotice name={child.displayName} pending={grant.isPending} onAccept={() => grant.mutate()} onCancel={() => setShowNotice(false)} />
        ) : (
          <div className="row-between">
            <span className="small">{child.consentRequired ? `${child.displayName} can't use the AI teacher until you give consent.` : 'Consent is optional for adult accounts.'}</span>
            <button className="btn btn-primary btn-sm" onClick={() => setShowNotice(true)}>Review and give consent</button>
          </div>
        )}
        {(grant.isError || withdraw.isError) && <div className="alert" role="alert">{errorMessage(grant.error ?? withdraw.error)}</div>}
      </div>

      <div style={{ marginTop: 'var(--space-3)', textAlign: 'right' }}>
        <Link className="btn btn-ghost btn-sm" to={`/parent/plans?studentId=${child.studentId}`}><Icon name="star" size={16} /> Plans</Link>{' '}
        <button className="btn btn-ghost btn-sm" onClick={() => unlink.mutate()} disabled={unlink.isPending}>Unlink this child</button>
      </div>
    </section>
  )
}

function Mini({ icon, tint, value, label }: { icon: 'trophy' | 'flame' | 'sun'; tint: string; value: string; label: string }) {
  return (
    <div className="stat list-item">
      <span className={`icon-tile ${tint}`}><Icon name={icon} /></span>
      <div><div style={{ fontWeight: 800 }}>{value}</div><div className="stat-label">{label}</div></div>
    </div>
  )
}

/** Plain-language consent notice (version must match the server). */
function ConsentNotice({ name, pending, onAccept, onCancel }: { name: string; pending: boolean; onAccept: () => void; onCancel: () => void }) {
  const [read, setRead] = useState(false)
  return (
    <div className="notice" role="region" aria-label="Consent notice">
      <h3>Consent notice · version {CONSENT_VERSION}</h3>
      <p className="small">By giving consent you allow <strong>{name}</strong> to use Googols. Here is what that means:</p>
      <ul className="small">
        <li><strong>What we keep:</strong> name, date of birth, syllabus and class, lessons and quizzes done, XP, and questions asked to the AI teacher.</li>
        <li><strong>Why:</strong> only to teach, track progress and show it to you. No advertising, and we never sell data.</li>
        <li><strong>AI teacher:</strong> questions are answered by an AI model running in Microsoft Azure. Phone numbers and email addresses are blocked from being sent.</li>
        <li><strong>Your rights:</strong> you can withdraw consent at any time here, which turns the AI teacher off for {name}, and you can ask us to delete the account.</li>
      </ul>
      <label className="check" style={{ margin: 'var(--space-3) 0' }}>
        <input type="checkbox" checked={read} onChange={e => setRead(e.target.checked)} /> I am {name}'s parent or guardian, and I have read and agree to this notice.
      </label>
      <div className="row">
        <button className="btn btn-primary btn-sm" disabled={!read || pending} onClick={onAccept}>{pending ? 'Saving…' : 'Give consent'}</button>
        <button className="btn btn-ghost btn-sm" onClick={onCancel}>Cancel</button>
      </div>
    </div>
  )
}

export function LinkChildPage() {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [code, setCode] = useState('')
  const [relationship, setRelationship] = useState('mother')
  const link = useMutation({
    mutationFn: () => api.post<{ studentId: string; displayName: string }>('/parents/link', { code, relationship }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['account'] })
      await qc.invalidateQueries({ queryKey: ['parent', 'children'] })
      navigate('/parent')
    },
  })
  const errs = fieldErrors(link.error)
  function submit(e: FormEvent) {
    e.preventDefault()
    link.mutate()
  }
  return (
    <div className="stack-lg" style={{ maxWidth: 640 }}>
      <header className="page-head">
        <div>
          <h1>Link your child</h1>
          <p className="muted">Ask your child to open <strong>Family</strong> in their Googols account and tap <strong>Create code</strong>. Then type that code here.</p>
        </div>
      </header>
      <form className="card" onSubmit={submit}>
        {link.isError && !Object.keys(errs).length && <div className="alert" role="alert">{errorMessage(link.error)}</div>}
        <div className="field">
          <label htmlFor="code">Child's code</label>
          <input id="code" className="input code-input" placeholder="ABCD-EFGH" autoComplete="off" maxLength={12} value={code} onChange={e => setCode(e.target.value.toUpperCase())} />
          {errs.code && <span className="field-error">{errs.code}</span>}
        </div>
        <div className="field">
          <label htmlFor="rel">You are their</label>
          <select id="rel" className="input" value={relationship} onChange={e => setRelationship(e.target.value)}>
            <option value="mother">Mother</option>
            <option value="father">Father</option>
            <option value="guardian">Guardian</option>
            <option value="other">Other</option>
          </select>
        </div>
        <button className="btn btn-primary" disabled={link.isPending || code.replace(/[^A-Za-z0-9]/g, '').length !== 8}>
          {link.isPending ? 'Linking…' : 'Link child'}
        </button>
      </form>
      <div className="card"><Empty icon="sparkles" text="After linking, you can see progress and give consent for the AI teacher." /></div>
    </div>
  )
}
