import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { Icon } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { Homework, MyTimetable } from '../../core/api/types'
import { TimetableGrid } from '../../shared/TimetableGrid'
import { Empty } from '../student/DashboardPage'
import '../tests/tests.css'
import '../../shared/heatmap.css'

const due = (iso: string | null) => (iso ? new Date(iso).toLocaleString([], { weekday: 'short', day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' }) : null)

export function useHomework() {
  return useQuery({ queryKey: ['learning', 'homework'], queryFn: () => api.get<Homework[]>('/learning/homework') })
}

/** Homework from the student's teachers: open items first, each with a link to do it. */
export function HomeworkPage() {
  const list = useHomework()
  const open = list.data?.filter(h => !h.done) ?? []
  const done = list.data?.filter(h => h.done) ?? []
  return (
    <div className="stack-lg">
      <header className="page-head"><div><h1>Homework</h1><p className="muted">Set by your teachers. Lessons, quizzes and tests tick themselves off when you finish them.</p></div></header>
      {list.isError && <div className="alert" role="alert">{errorMessage(list.error)}</div>}
      {list.data?.length === 0 && <div className="card"><Empty icon="clipboard" text="No homework. When your teacher sets some, it appears here." /></div>}
      {open.length > 0 && <section className="stack"><h2>To do</h2>{open.map(h => <HomeworkCard key={h.id} h={h} />)}</section>}
      {done.length > 0 && <section className="stack"><h2>Done</h2>{done.map(h => <HomeworkCard key={h.id} h={h} />)}</section>}
      <TimetableCard />
    </div>
  )
}

function HomeworkCard({ h }: { h: Homework }) {
  const qc = useQueryClient()
  const [note, setNote] = useState(h.myText ?? '')
  const mark = useMutation({
    mutationFn: () => api.post(`/learning/homework/${h.id}/done`, { text: note || null }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['learning', 'homework'] }),
  })
  const link = h.kind === 'Lesson' && h.lessonId ? `/lessons/${h.lessonId}` : h.kind === 'Quiz' && h.chapterId ? `/chapters/${h.chapterId}/quiz` : h.kind === 'Test' ? '/tests' : null
  return (
    <article className="card stack">
      <div className="row-between">
        <div>
          <strong>{h.title}</strong>
          <div className="small muted">{h.classroom}{h.target ? ` · ${h.target}` : ''}{h.dueAtUtc ? ` · due ${due(h.dueAtUtc)}` : ''}</div>
        </div>
        {h.done ? <span className="badge badge-success"><Icon name="check" size={14} /> Done</span>
          : h.overdue ? <span className="badge badge-warning">Overdue</span> : <span className="badge">To do</span>}
      </div>
      {h.instructions && <p className="pre-wrap small" style={{ margin: 0 }}>{h.instructions}</p>}
      {!h.done && link && <div><Link className="btn btn-primary btn-sm" to={link}>{h.kind === 'Lesson' ? 'Open lesson' : h.kind === 'Quiz' ? 'Take quiz' : 'Go to tests'} <Icon name="chevron" size={14} /></Link></div>}
      {h.kind === 'Task' && (
        <form className="inline-form" style={{ marginTop: 0 }} onSubmit={e => { e.preventDefault(); mark.mutate() }}>
          <input className="input" placeholder="Add a note for your teacher (optional)" maxLength={2000} value={note} onChange={e => setNote(e.target.value)} aria-label={`Note for ${h.title}`} />
          <button className="btn btn-ghost btn-sm" disabled={mark.isPending}>{h.done ? 'Update' : 'Mark as done'}</button>
          {mark.isError && <span className="field-error">{errorMessage(mark.error)}</span>}
        </form>
      )}
    </article>
  )
}

export function TimetableCard() {
  const tt = useQuery({ queryKey: ['learning', 'timetable'], queryFn: () => api.get<MyTimetable[]>('/learning/timetable') })
  if (!tt.data?.length) return null
  return (
    <>
      {tt.data.map(t => (
        <section key={t.divisionId} className="card">
          <div className="card-title"><h2>Timetable · {t.division}</h2><span className="badge">{t.school}</span></div>
          <TimetableGrid slots={t.slots} />
        </section>
      ))}
    </>
  )
}
