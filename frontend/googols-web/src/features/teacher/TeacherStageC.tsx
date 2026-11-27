import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Icon } from '../../app/Icons'
import { api, errorMessage, fieldErrors } from '../../core/api/client'
import type { AssignmentKind, ChapterInsight, ExamAdmin, Heatmap, Slot, TeacherAssignment } from '../../core/api/types'
import { HeatmapTable } from '../../shared/HeatmapTable'
import { TimetableGrid } from '../../shared/TimetableGrid'

export function ClassHeatmap({ classroomId }: { classroomId: string }) {
  const hm = useQuery({ queryKey: ['teacher', 'heatmap', classroomId], queryFn: () => api.get<Heatmap>(`/teacher/classrooms/${classroomId}/heatmap`) })
  return (
    <section className="card" aria-label="Heat map">
      <div className="card-title"><h2>Heat map</h2></div>
      <p className="small muted">Mastery of each chapter from quizzes, tests and finished lessons. Red columns are the topics to reteach; red rows are students who need help.</p>
      {hm.isError && <div className="alert" role="alert">{errorMessage(hm.error)}</div>}
      {hm.data && <HeatmapTable data={hm.data} />}
    </section>
  )
}

const KIND_LABEL: Record<AssignmentKind, string> = { Lesson: 'Read a lesson', Quiz: 'Chapter quiz', Test: 'Timed test', Task: 'Other task' }

export function ClassHomework({ classroomId, chapters }: { classroomId: string; chapters: ChapterInsight[] }) {
  const qc = useQueryClient()
  const key = ['teacher', 'assignments', classroomId]
  const list = useQuery({ queryKey: key, queryFn: () => api.get<TeacherAssignment[]>(`/teacher/classrooms/${classroomId}/assignments`) })
  const [creating, setCreating] = useState(false)
  const [open, setOpen] = useState<string | null>(null)
  const remove = useMutation({ mutationFn: (id: string) => api.del(`/teacher/classrooms/${classroomId}/assignments/${id}`), onSuccess: () => qc.invalidateQueries({ queryKey: key }) })
  return (
    <section className="card stack" aria-label="Homework">
      <div className="card-title">
        <h2>Homework</h2>
        <button className="btn btn-primary btn-sm" onClick={() => setCreating(true)}><Icon name="plus" size={16} /> Set homework</button>
      </div>
      <p className="small muted">Lessons, quizzes and tests are ticked off automatically when students finish them.</p>
      {creating && <HomeworkForm classroomId={classroomId} chapters={chapters} onDone={() => { setCreating(false); void qc.invalidateQueries({ queryKey: key }) }} />}
      {list.isError && <div className="alert" role="alert">{errorMessage(list.error)}</div>}
      {list.data?.length === 0 && !creating && <p className="muted">No homework set yet.</p>}
      {list.data?.map(a => (
        <div key={a.id} className="nest">
          <div className="row-between">
            <div>
              <strong>{a.title}</strong> <span className="badge">{KIND_LABEL[a.kind]}{a.target ? `: ${a.target}` : ''}</span>
              <div className="small muted">{a.dueAtUtc ? `Due ${new Date(a.dueAtUtc).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' })}` : 'No due date'}</div>
            </div>
            <div className="row">
              <button className="btn btn-ghost btn-sm" aria-expanded={open === a.id} onClick={() => setOpen(o => (o === a.id ? null : a.id))}>{a.done}/{a.total} done</button>
              <button className="btn btn-ghost btn-sm" onClick={() => remove.mutate(a.id)}>Remove</button>
            </div>
          </div>
          {a.total > 0 && <div className="mastery-bar" style={{ marginTop: 8 }}><span style={{ width: `${(100 * a.done) / a.total}%` }} /></div>}
          {open === a.id && (
            <ul className="checks" style={{ marginTop: 'var(--space-3)' }}>
              {a.students.map(s => (
                <li key={s.studentId} className={s.done ? 'ok' : 'bad'}>
                  <Icon name={s.done ? 'check' : 'x'} size={14} /> {s.name}{s.doneAtUtc ? ` · ${new Date(s.doneAtUtc).toLocaleDateString()}` : ''}{s.text ? ` · “${s.text}”` : ''}
                </li>
              ))}
            </ul>
          )}
        </div>
      ))}
    </section>
  )
}

function HomeworkForm({ classroomId, chapters, onDone }: { classroomId: string; chapters: ChapterInsight[]; onDone: () => void }) {
  const [kind, setKind] = useState<AssignmentKind>('Lesson')
  const [title, setTitle] = useState('')
  const [instructions, setInstructions] = useState('')
  const [lessonId, setLessonId] = useState('')
  const [chapterId, setChapterId] = useState('')
  const [examId, setExamId] = useState('')
  const [due, setDue] = useState('')
  const lessons = useQuery({
    queryKey: ['teacher', 'lessons', classroomId],
    queryFn: () => api.get<{ id: string; title: string; chapter: string; subject: string }[]>(`/teacher/classrooms/${classroomId}/lessons`),
    enabled: kind === 'Lesson',
  })
  const exams = useQuery({ queryKey: ['teacher', 'exams', classroomId], queryFn: () => api.get<ExamAdmin[]>(`/teacher/classrooms/${classroomId}/exams`), enabled: kind === 'Test' })
  const save = useMutation({
    mutationFn: () => api.post(`/teacher/classrooms/${classroomId}/assignments`, {
      title, instructions: instructions || null, kind, lessonId: lessonId || null, chapterId: chapterId || null, examId: examId || null,
      dueAtUtc: due ? new Date(due).toISOString() : null,
    }),
    onSuccess: onDone,
  })
  const errors = fieldErrors(save.error)
  function submit(e: FormEvent) { e.preventDefault(); save.mutate() }
  return (
    <form className="nest stack" onSubmit={submit} aria-label="New homework">
      <div className="inline-form" style={{ marginTop: 0 }}>
        <select className="input narrow" aria-label="Type" value={kind} onChange={e => setKind(e.target.value as AssignmentKind)}>
          {(Object.keys(KIND_LABEL) as AssignmentKind[]).map(k => <option key={k} value={k}>{KIND_LABEL[k]}</option>)}
        </select>
        <input className="input" aria-label="Title" placeholder="Title, e.g. Read Photosynthesis before Monday" maxLength={200} value={title} onChange={e => setTitle(e.target.value)} />
      </div>
      {kind === 'Lesson' && (
        <select className="input" aria-label="Lesson" value={lessonId} onChange={e => setLessonId(e.target.value)}>
          <option value="">Choose a lesson…</option>
          {lessons.data?.map(l => <option key={l.id} value={l.id}>{l.subject} · {l.chapter} · {l.title}</option>)}
        </select>
      )}
      {kind === 'Quiz' && (
        <select className="input" aria-label="Chapter" value={chapterId} onChange={e => setChapterId(e.target.value)}>
          <option value="">Choose a chapter…</option>
          {chapters.map(c => <option key={c.chapterId} value={c.chapterId}>{c.subject} · {c.title}</option>)}
        </select>
      )}
      {kind === 'Test' && (
        <select className="input" aria-label="Test" value={examId} onChange={e => setExamId(e.target.value)}>
          <option value="">Choose one of this class's tests…</option>
          {exams.data?.map(x => <option key={x.id} value={x.id}>{x.title}</option>)}
        </select>
      )}
      <textarea className="input" rows={2} maxLength={2000} placeholder="Instructions (optional)" aria-label="Instructions" value={instructions} onChange={e => setInstructions(e.target.value)} />
      <label className="row small">Due <input className="input" type="datetime-local" value={due} onChange={e => setDue(e.target.value)} /></label>
      {Object.values(errors)[0] && <span className="field-error">{Object.values(errors)[0]}</span>}
      {save.isError && !Object.keys(errors).length && <div className="alert" role="alert">{errorMessage(save.error)}</div>}
      <div className="row"><button className="btn btn-primary" disabled={save.isPending || !title.trim()}>Set homework</button><button type="button" className="btn btn-ghost" onClick={onDone}>Cancel</button></div>
    </form>
  )
}

export function TeacherTimetable() {
  const tt = useQuery({ queryKey: ['teacher', 'timetable'], queryFn: () => api.get<Slot[]>('/teacher/timetable') })
  if (!tt.data?.length) return null
  return (
    <section className="card">
      <div className="card-title"><h2>My timetable</h2></div>
      <TimetableGrid slots={tt.data} show="division" />
    </section>
  )
}
