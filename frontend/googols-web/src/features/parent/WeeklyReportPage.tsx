import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { Icon, type IconName } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { WeeklyReport } from '../../core/api/types'
import '../tests/tests.css'

const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
const nice = (s: string) => new Date(`${s}T00:00:00`).toLocaleDateString([], { day: 'numeric', month: 'short' })

function delta(now: number, before: number) {
  if (now === before) return 'same as last week'
  return now > before ? `up from ${before} last week` : `down from ${before} last week`
}

/** A parent's weekly summary for one child. Shows activity and results only, never chat content. */
export function WeeklyReportPage() {
  const { studentId } = useParams()
  const qc = useQueryClient()
  const [offset, setOffset] = useState(0) // 0 = this week, 1 = last week, …
  const day = new Date()
  day.setDate(day.getDate() - offset * 7)
  const key = ['parent', 'report', studentId, offset]
  const report = useQuery({ queryKey: key, queryFn: () => api.get<WeeklyReport>(`/parents/children/${studentId}/weekly-report?week=${iso(day)}`) })
  const email = useMutation({
    mutationFn: (enabled: boolean) => api.put(`/parents/children/${studentId}/report-email`, { enabled }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['parent', 'report', studentId] }),
  })
  if (report.isLoading) return <div className="skeleton" style={{ height: 320 }} />
  if (report.isError || !report.data) return <div className="alert" role="alert">{errorMessage(report.error)}</div>
  const r = report.data
  const tiles: { icon: IconName; tint: string; value: string; label: string }[] = [
    { icon: 'book', tint: 'tint-teal', value: `${r.lessonsCompleted}`, label: `Lessons finished · ${delta(r.lessonsCompleted, r.lessonsLastWeek)}` },
    { icon: 'bolt', tint: 'tint-amber', value: `${r.quizzes + r.tests}`, label: `Quizzes and tests${r.averageScore !== null ? ` · average ${r.averageScore}%` : ''}` },
    { icon: 'calendar', tint: 'tint-indigo', value: `${r.activeDays}/7`, label: `Days learning · streak ${r.streakDays}` },
    { icon: 'star', tint: 'tint-lime', value: `${r.xpGained}`, label: `XP earned · ${delta(r.xpGained, r.xpLastWeek)}` },
    { icon: 'sparkles', tint: 'tint-sky', value: `${r.aiQuestions}`, label: 'Questions asked to the AI teacher' },
    ...(r.homeworkSet > 0 ? [{ icon: 'pen' as IconName, tint: 'tint-rose', value: `${r.homeworkDone}/${r.homeworkSet}`, label: `Homework done${r.homeworkOverdue ? ` · ${r.homeworkOverdue} overdue` : ''}` }] : []),
  ]
  return (
    <div className="stack-lg">
      <header className="page-head">
        <div>
          <div className="crumbs"><Link to="/parent">My children</Link><Icon name="chevron" size={14} /><span>Weekly report</span></div>
          <h1>{r.name}'s week</h1>
          <p className="muted">{nice(r.weekStart)} – {nice(r.weekEnd)}{offset === 0 ? ' (so far)' : ''}</p>
        </div>
        <div className="row">
          <button className="btn btn-ghost btn-sm" onClick={() => setOffset(o => o + 1)}><Icon name="arrowLeft" size={14} /> Earlier</button>
          <button className="btn btn-ghost btn-sm" onClick={() => setOffset(o => Math.max(0, o - 1))} disabled={offset === 0}>Later <Icon name="chevron" size={14} /></button>
        </div>
      </header>

      <section className="grid-stats">
        {tiles.map(t => (
          <div key={t.label} className="card stat">
            <span className={`icon-tile lg ${t.tint}`}><Icon name={t.icon} /></span>
            <div><div className="stat-value">{t.value}</div><div className="stat-label">{t.label}</div></div>
          </div>
        ))}
      </section>

      {r.lessonsCompleted + r.quizzes + r.tests === 0 && (
        <div className="alert alert-info">No learning was recorded this week. A short lesson each day helps a lot. You could sit together for 10 minutes and open the next lesson.</div>
      )}

      <div className="grid-2-even">
        <section className="card">
          <div className="card-title"><h2>Chapters practised</h2></div>
          {r.practised.length === 0 ? <p className="muted">No quizzes or tests this week.</p> : r.practised.map(c => (
            <div key={c.title} className="mission"><span className="grow"><strong>{c.title}</strong><span className="small muted" style={{ display: 'block' }}>{c.subject}</span></span><span className="badge">{c.mastery}%</span></div>
          ))}
          {r.lessons.length > 0 && <><h3 style={{ marginTop: 'var(--space-4)' }}>Lessons finished</h3><ul className="small">{r.lessons.map(l => <li key={l}>{l}</li>)}</ul></>}
        </section>
        <section className="card">
          <div className="card-title"><h2>Needs practice</h2></div>
          {r.weakAreas.length === 0 ? <p className="muted">No weak chapters right now.</p> : r.weakAreas.map(c => (
            <div key={c.title} className="mission"><span className="icon-tile tint-rose"><Icon name="target" /></span><span className="grow"><strong>{c.title}</strong><span className="small muted" style={{ display: 'block' }}>{c.subject}</span></span><span className="badge badge-warning">{c.mastery}%</span></div>
          ))}
        </section>
      </div>

      <section className="card row-between">
        <div>
          <strong>Weekly email</strong>
          <p className="small muted" style={{ margin: 0 }}>
            {r.emailConfigured ? 'Every Monday morning we email you last week\'s report.' : 'Weekly emails start once email sending is set up on Googols. You can still see the report here any time.'}
          </p>
        </div>
        <label className="check" style={{ flexBasis: 'auto' }}>
          <input type="checkbox" checked={r.emailEnabled} disabled={email.isPending} onChange={e => email.mutate(e.target.checked)} /> Send me the weekly email
        </label>
      </section>
    </div>
  )
}
