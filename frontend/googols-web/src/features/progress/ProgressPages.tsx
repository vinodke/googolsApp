import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Icon } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { MasteryOverview, Missions, PlanDay } from '../../core/api/types'
import { useBookmarks } from '../../core/bookmarks'
import { Empty } from '../student/DashboardPage'
import '../tests/tests.css'

const dayName = (iso: string, i: number) => (i === 0 ? 'Today' : i === 1 ? 'Tomorrow' : new Date(`${iso}T00:00:00`).toLocaleDateString([], { weekday: 'long' }))

function barClass(v: number | null) {
  if (v === null) return 'mastery-bar'
  return `mastery-bar${v < 40 ? ' weak' : v < 70 ? ' mid' : ''}`
}

/** Daily missions and weekly challenges; claim buttons pay XP once the server has verified the activity. */
export function MissionsCard({ compact = false }: { compact?: boolean }) {
  const qc = useQueryClient()
  const m = useQuery({ queryKey: ['learning', 'missions'], queryFn: () => api.get<Missions>('/learning/missions') })
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: ['learning', 'missions'] })
    void qc.invalidateQueries({ queryKey: ['student', 'me'] })
    void qc.invalidateQueries({ queryKey: ['xp'] })
  }
  const claim = useMutation({ mutationFn: () => api.post<{ xpAwarded: number }>('/learning/missions/claim'), onSuccess: refresh })
  const claimChallenge = useMutation({ mutationFn: (code: string) => api.post<{ xpAwarded: number }>(`/learning/challenges/${code}/claim`), onSuccess: refresh })
  if (!m.data) return m.isError ? <div className="alert" role="alert">{errorMessage(m.error)}</div> : <div className="skeleton" style={{ height: 160 }} />
  const d = m.data
  return (
    <section className="card" aria-label="Missions and challenges">
      <div className="card-title"><h2>Today's missions</h2><span className="badge badge-warning"><Icon name="star" size={14} /> +{d.bonusXp} XP for all three</span></div>
      {d.missions.map(x => (
        <div key={x.code} className={`mission${x.done ? ' done' : ''}`}>
          <span className="tick" aria-hidden>{x.done && <Icon name="check" size={16} />}</span>
          <span className="grow">{x.title}</span>
          <span className="small muted">{x.progress}/{x.target}</span>
        </div>
      ))}
      {d.claimed
        ? <p className="small muted"><Icon name="check" size={14} /> Bonus collected. New missions tomorrow.</p>
        : <button className="btn btn-primary btn-sm" disabled={!d.allDone || claim.isPending} onClick={() => claim.mutate()}>{d.allDone ? `Collect +${d.bonusXp} XP` : 'Finish all three to collect'}</button>}
      {!compact && (
        <>
          <div className="card-title" style={{ marginTop: 'var(--space-5)' }}><h2>This week's challenges</h2><span className="badge">+{d.challengeXp} XP each</span></div>
          {d.challenges.map(c => (
            <div key={c.code} className={`mission${c.done ? ' done' : ''}`}>
              <span className="tick" aria-hidden>{c.done && <Icon name="check" size={16} />}</span>
              <span className="grow"><strong>{c.title}</strong><span className="small muted" style={{ display: 'block' }}>{c.description}</span>
                <span className="mastery-bar" style={{ display: 'block', marginTop: 4 }}><span style={{ width: `${(100 * c.progress) / c.target}%` }} /></span></span>
              {c.claimed
                ? <span className="badge badge-success">Collected</span>
                : <button className="btn btn-ghost btn-sm" disabled={!c.done || claimChallenge.isPending} onClick={() => claimChallenge.mutate(c.code)}>{c.done ? 'Collect' : `${c.progress}/${c.target}`}</button>}
            </div>
          ))}
        </>
      )}
      {(claim.isError || claimChallenge.isError) && <div className="alert" role="alert">{errorMessage(claim.error ?? claimChallenge.error)}</div>}
    </section>
  )
}

export function StudyPlanCard({ days = 7 }: { days?: number }) {
  const plan = useQuery({ queryKey: ['learning', 'study-plan'], queryFn: () => api.get<PlanDay[]>('/learning/study-plan') })
  if (!plan.data) return plan.isError ? <div className="alert" role="alert">{errorMessage(plan.error)}</div> : <div className="skeleton" style={{ height: 160 }} />
  const shown = plan.data.slice(0, days)
  const empty = shown.every(d => d.tasks.length === 0)
  return (
    <section className="card" aria-label="Study plan">
      <div className="card-title"><h2>{days === 1 ? "Today's plan" : 'My study plan'}</h2>{days === 1 && <Link to="/progress" className="small">Whole week</Link>}</div>
      {empty ? <p className="muted">You're all caught up. Take a timed test or a quiz to keep your skills sharp; new tasks appear here as lessons are added.</p> : (
        <div className="plan">
          {shown.map((d, i) => (
            <div key={d.date} className={`plan-day${i === 0 ? ' today' : ''}`}>
              <strong>{dayName(d.date, i)}</strong>
              {d.tasks.length === 0 && <span className="small muted">Free day: revise anything you like.</span>}
              {d.tasks.map(t => (
                <Link key={t.title} className="plan-task"
                  to={t.kind === 'lesson' && t.lessonId ? `/lessons/${t.lessonId}` : t.kind === 'quiz' ? `/chapters/${t.chapterId}/quiz` : `/ai-teacher?chapter=${t.chapterId}`}>
                  <span><Icon name={t.kind === 'lesson' ? 'book' : t.kind === 'quiz' ? 'bolt' : 'sparkles'} size={14} /> <strong>{t.title}</strong></span>
                  <span className="small muted">{t.subject} · {t.reason}</span>
                </Link>
              ))}
            </div>
          ))}
        </div>
      )}
    </section>
  )
}

/** My progress: mastery by chapter, weak areas, study plan, missions and challenges. */
export function ProgressPage() {
  const mastery = useQuery({ queryKey: ['learning', 'mastery'], queryFn: () => api.get<MasteryOverview>('/learning/mastery') })
  return (
    <div className="stack-lg">
      <header className="page-head">
        <div><h1>My progress</h1><p className="muted">Mastery comes from your quiz and test results (recent ones count more) and the lessons you've finished.</p></div>
        <Link to="/tests" className="btn btn-primary"><Icon name="clock" size={18} /> Take a timed test</Link>
      </header>

      <div className="grid-2-even">
        <MissionsCard />
        <section className="card" aria-label="Weak areas">
          <div className="card-title"><h2>Needs work</h2></div>
          {mastery.data?.weakAreas.length === 0 && <p className="muted">No weak chapters right now. Take quizzes to keep it that way.</p>}
          {mastery.data?.weakAreas.map(w => (
            <div key={w.chapterId} className="mission">
              <span className="icon-tile tint-rose"><Icon name="target" /></span>
              <span className="grow"><strong>{w.title}</strong><span className="small muted" style={{ display: 'block' }}>{w.subject} · mastery {w.mastery}%</span></span>
              <Link className="btn btn-ghost btn-sm" to={`/chapters/${w.chapterId}/quiz`}>Practise</Link>
            </div>
          ))}
        </section>
      </div>

      <StudyPlanCard />

      {mastery.isError && <div className="alert" role="alert">{errorMessage(mastery.error)}</div>}
      {mastery.data?.subjects.map(s => (
        <section key={s.subjectId} className="card" style={{ overflowX: 'auto' }}>
          <div className="card-title"><h2>{s.name}</h2><span className="badge">{s.average === null ? 'Not tested yet' : `Average ${s.average}%`}</span></div>
          <table className="table">
            <thead><tr><th>Chapter</th><th>Lessons</th><th style={{ width: '35%' }}>Mastery</th><th /></tr></thead>
            <tbody>
              {s.chapters.map(c => (
                <tr key={c.chapterId}>
                  <td><Link to={`/chapters/${c.chapterId}`}>{c.title}</Link></td>
                  <td>{c.lessonsDone}/{c.lessonsTotal}</td>
                  <td>
                    <div className={barClass(c.mastery)}><span style={{ width: `${c.mastery ?? 0}%` }} /></div>
                    <span className="small muted">{c.mastery === null ? c.band : `${c.mastery}% · ${c.band}`}{c.attempts ? ` · ${c.attempts} attempt${c.attempts === 1 ? '' : 's'}` : ''}</span>
                  </td>
                  <td>{c.hasQuiz && <Link className="btn btn-ghost btn-sm" to={`/chapters/${c.chapterId}/quiz`}>Quiz</Link>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ))}
    </div>
  )
}

/** Saved lessons and AI answers. */
export function BookmarksPage() {
  const qc = useQueryClient()
  const list = useBookmarks()
  const remove = useMutation({ mutationFn: (id: string) => api.del(`/learning/bookmarks/${id}`), onSuccess: () => qc.invalidateQueries({ queryKey: ['learning', 'bookmarks'] }) })
  return (
    <div className="stack-lg">
      <header className="page-head"><div><h1>Saved</h1><p className="muted">Lessons and AI teacher answers you saved to come back to.</p></div></header>
      {list.isError && <div className="alert" role="alert">{errorMessage(list.error)}</div>}
      {list.data?.length === 0 && <div className="card"><Empty icon="bookmark" text="Nothing saved yet. Tap Save on a lesson or on an AI teacher answer." /></div>}
      <div className="stack">
        {list.data?.map(b => (
          <article key={b.id} className="card row-between">
            <div style={{ minWidth: 0, flex: 1 }}>
              <span className="badge">{b.kind === 'lesson' ? 'Lesson' : 'AI answer'}</span>
              <h3 style={{ margin: 'var(--space-2) 0 4px' }}>
                {b.kind === 'lesson' ? <Link to={`/lessons/${b.refId}`}>{b.title}</Link> : b.title}
              </h3>
              {b.snippet && <p className="small muted pre-wrap" style={{ margin: 0 }}>{b.snippet}</p>}
            </div>
            <div className="row">
              {b.kind === 'answer' && b.subjectId && <Link className="btn btn-ghost btn-sm" to={`/ai-teacher?subject=${b.subjectId}`}>AI teacher</Link>}
              <button className="btn btn-ghost btn-sm" onClick={() => remove.mutate(b.id)} aria-label={`Remove ${b.title}`}>Remove</button>
            </div>
          </article>
        ))}
      </div>
    </div>
  )
}
