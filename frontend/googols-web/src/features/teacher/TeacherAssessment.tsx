import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Icon } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { ChapterInsight, ExamAdmin, MarkingItem } from '../../core/api/types'
import { ExamForm, type ExamFormValues } from '../admin/ExamForm'
import '../tests/tests.css'

/** Timed tests the teacher sets for one classroom, with submission counts and averages. */
export function ClassTests({ classroomId, chapters }: { classroomId: string; chapters: ChapterInsight[] }) {
  const qc = useQueryClient()
  const key = ['teacher', 'exams', classroomId]
  const exams = useQuery({ queryKey: key, queryFn: () => api.get<ExamAdmin[]>(`/teacher/classrooms/${classroomId}/exams`) })
  const [creating, setCreating] = useState(false)
  const create = useMutation({
    mutationFn: (v: ExamFormValues) => api.post(`/teacher/classrooms/${classroomId}/exams`, { ...v, classId: '00000000-0000-0000-0000-000000000000', subjectId: null }),
    onSuccess: () => { setCreating(false); void qc.invalidateQueries({ queryKey: key }) },
  })
  const remove = useMutation({ mutationFn: (id: string) => api.del(`/teacher/exams/${id}`), onSuccess: () => qc.invalidateQueries({ queryKey: key }) })
  return (
    <section className="card stack" aria-label="Class tests">
      <div className="card-title">
        <h2>Class tests</h2>
        <button className="btn btn-primary btn-sm" onClick={() => setCreating(true)}><Icon name="plus" size={16} /> New timed test</button>
      </div>
      <p className="small muted">The test appears for every student in this classroom straight away. Set a closing time for homework tests.</p>
      {creating && (
        <ExamForm chapters={chapters.map(c => ({ id: c.chapterId, title: c.title, subject: c.subject }))} submitLabel="Publish test"
          pending={create.isPending} error={create.error} onSubmit={v => create.mutate(v)} onCancel={() => setCreating(false)} />
      )}
      {exams.isError && <div className="alert" role="alert">{errorMessage(exams.error)}</div>}
      {exams.data?.length === 0 && !creating && <p className="muted">No tests yet.</p>}
      {exams.data?.map(e => (
        <div key={e.id} className="list-item row-between">
          <div>
            <strong>{e.title}</strong>
            <div className="small muted">{e.mcqCount} multiple choice · {e.writtenCount} written · {e.durationMinutes} min{e.closesAtUtc ? ` · closes ${new Date(e.closesAtUtc).toLocaleString()}` : ''}</div>
          </div>
          <div className="row">
            <span className="badge">{e.submitted} submitted{e.average !== null ? ` · avg ${e.average}%` : ''}</span>
            <button className="btn btn-ghost btn-sm" onClick={() => remove.mutate(e.id)}>Remove</button>
          </div>
        </div>
      ))}
    </section>
  )
}

/** Written answers from this classroom: confirm the AI's provisional mark or change it. */
export function Marking({ classroomId }: { classroomId: string }) {
  const [all, setAll] = useState(false)
  const key = ['teacher', 'marking', classroomId, all]
  const items = useQuery({ queryKey: key, queryFn: () => api.get<MarkingItem[]>(`/teacher/classrooms/${classroomId}/marking${all ? '?all=true' : ''}`) })
  return (
    <section className="stack" aria-label="Marking">
      <div className="row-between">
        <p className="muted" style={{ margin: 0 }}>The AI marks written answers against the marking points. Its marks are provisional until you confirm them.</p>
        <label className="check" style={{ flexBasis: 'auto' }}><input type="checkbox" checked={all} onChange={e => setAll(e.target.checked)} /> Show confirmed too</label>
      </div>
      {items.isError && <div className="alert" role="alert">{errorMessage(items.error)}</div>}
      {items.data?.length === 0 && <div className="card empty">Nothing to mark. 🎉</div>}
      {items.data?.map(i => <MarkingCard key={i.answerId} item={i} classroomId={classroomId} />)}
    </section>
  )
}

function MarkingCard({ item, classroomId }: { item: MarkingItem; classroomId: string }) {
  const qc = useQueryClient()
  const [score, setScore] = useState(String(item.teacherScore ?? item.aiScore ?? ''))
  const [comment, setComment] = useState(item.teacherComment ?? '')
  const save = useMutation({
    mutationFn: () => api.put(`/teacher/written-answers/${item.answerId}`, { score: Number(score), comment: comment || null }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['teacher', 'marking', classroomId] }),
  })
  return (
    <article className="card stack">
      <div className="row-between">
        <div><strong>{item.student}</strong> <span className="small muted">· {item.examTitle} · {new Date(item.submittedAtUtc).toLocaleString()}</span></div>
        <span className={`badge ${item.status === 'Confirmed' ? 'badge-success' : item.status === 'NotMarked' ? 'badge-warning' : ''}`}>
          {item.status === 'Confirmed' ? 'Confirmed' : item.status === 'NotMarked' ? 'Needs marking' : `AI: ${item.aiScore}/${item.maxMarks}`}
        </span>
      </div>
      <strong>{item.question}</strong>
      <p className="pre-wrap answer-box">{item.answer || <em className="muted">No answer written.</em>}</p>
      {item.aiCriteria.length > 0 && (
        <ul className="checks">
          {item.aiCriteria.map(c => <li key={c.criterion} className={c.awarded >= c.max ? 'ok' : c.awarded > 0 ? '' : 'bad'}><strong>{c.awarded}/{c.max}</strong> {c.criterion}{c.comment ? ` · ${c.comment}` : ''}</li>)}
        </ul>
      )}
      {item.modelAnswer && <details><summary className="small">Model answer</summary><p className="pre-wrap small">{item.modelAnswer}</p></details>}
      <form className="inline-form" style={{ marginTop: 0 }} onSubmit={e => { e.preventDefault(); save.mutate() }}>
        <label className="row small">Mark
          <input className="input narrow" type="number" min={0} max={item.maxMarks} step={0.5} value={score} onChange={e => setScore(e.target.value)} aria-label={`Mark for ${item.student}`} required />
          / {item.maxMarks}
        </label>
        <input className="input" placeholder="Comment for the student (optional)" maxLength={1000} value={comment} onChange={e => setComment(e.target.value)} />
        <button className="btn btn-primary btn-sm" disabled={save.isPending || score === ''}>{item.status === 'Confirmed' ? 'Update' : 'Confirm mark'}</button>
        {save.isSuccess && <span className="small text-success">Saved</span>}
        {save.isError && <span className="field-error">{errorMessage(save.error)}</span>}
      </form>
    </article>
  )
}
