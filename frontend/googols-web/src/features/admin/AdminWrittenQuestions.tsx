import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Icon } from '../../app/Icons'
import { api, errorMessage, fieldErrors } from '../../core/api/client'
import type { AdminWrittenQuestion, RubricCriterion } from '../../core/api/types'

/** Written (long-answer) questions for a chapter, each with a marking scheme the AI marks against. */
export function WrittenQuestions({ chapterId }: { chapterId: string }) {
  const qc = useQueryClient()
  const key = ['admin', 'written', chapterId]
  const list = useQuery({ queryKey: key, queryFn: () => api.get<AdminWrittenQuestion[]>(`/curriculum/admin/chapters/${chapterId}/written-questions`) })
  const [editing, setEditing] = useState<AdminWrittenQuestion | 'new' | null>(null)
  const refresh = () => qc.invalidateQueries({ queryKey: key })
  const status = useMutation({ mutationFn: (v: { id: string; status: string }) => api.put(`/curriculum/admin/written-questions/${v.id}/status`, { status: v.status }), onSuccess: refresh })
  const remove = useMutation({ mutationFn: (id: string) => api.del(`/curriculum/admin/written-questions/${id}`), onSuccess: refresh })
  return (
    <section className="card" style={{ marginTop: 'var(--space-6)' }} aria-label="Written questions">
      <div className="card-title">
        <h2>Written questions</h2>
        <button className="btn btn-primary btn-sm" onClick={() => setEditing('new')}><Icon name="plus" size={16} /> New written question</button>
      </div>
      <p className="small muted">Used in timed tests. Students write their own answer; the AI marks it against your marking points (provisional), and teachers can confirm or change the mark.</p>
      {editing && <WrittenForm key={editing === 'new' ? 'new' : editing.id} chapterId={chapterId} q={editing === 'new' ? null : editing} onDone={() => { setEditing(null); void refresh() }} />}
      {list.data?.length === 0 && !editing && <p className="muted">No written questions yet.</p>}
      <ol className="list" style={{ gap: 'var(--space-3)' }}>
        {list.data?.map(q => (
          <li key={q.id} className="nest">
            <div className="row-between">
              <strong>{q.prompt}</strong>
              <span className={`badge status-${q.status.toLowerCase()}`}>{q.status}</span>
            </div>
            <ul className="small" style={{ margin: 'var(--space-2) 0' }}>
              {q.rubric.map((c, i) => <li key={i}>{c.criterion} ({c.marks})</li>)}
            </ul>
            <div className="row small">
              <span className="muted">{q.maxMarks} marks · {q.language === 'ml' ? 'Malayalam' : 'English'}</span>
              <button className="icon-action" onClick={() => setEditing(q)}>Edit</button>
              <button className="icon-action" onClick={() => status.mutate({ id: q.id, status: q.status === 'Published' ? 'Draft' : 'Published' })}>{q.status === 'Published' ? 'Unpublish' : 'Publish'}</button>
              <button className="icon-action" onClick={() => remove.mutate(q.id)}>Archive</button>
            </div>
          </li>
        ))}
      </ol>
    </section>
  )
}

function WrittenForm({ chapterId, q, onDone }: { chapterId: string; q: AdminWrittenQuestion | null; onDone: () => void }) {
  const [prompt, setPrompt] = useState(q?.prompt ?? '')
  const [model, setModel] = useState(q?.modelAnswer ?? '')
  const [rubric, setRubric] = useState<RubricCriterion[]>(q?.rubric ?? [{ criterion: '', marks: 1 }])
  const save = useMutation({
    mutationFn: () => {
      const body = { prompt, modelAnswer: model || null, rubric: rubric.filter(r => r.criterion.trim()), language: null }
      return q ? api.put(`/curriculum/admin/written-questions/${q.id}`, body) : api.post(`/curriculum/admin/chapters/${chapterId}/written-questions`, body)
    },
    onSuccess: onDone,
  })
  const errors = fieldErrors(save.error)
  const total = rubric.reduce((s, r) => s + (r.criterion.trim() ? r.marks : 0), 0)
  function submit(e: FormEvent) { e.preventDefault(); save.mutate() }
  return (
    <form className="nest stack" onSubmit={submit} aria-label="Written question">
      <label className="field">Question
        <textarea className="input" rows={2} maxLength={1000} value={prompt} onChange={e => setPrompt(e.target.value)} required />
        {errors.prompt && <span className="field-error">{errors.prompt}</span>}
      </label>
      <label className="field">Model answer (optional, helps the AI and is shown to students after the test)
        <textarea className="input" rows={3} maxLength={3000} value={model} onChange={e => setModel(e.target.value)} />
      </label>
      <fieldset className="field">
        <legend>Marking points · total {total} marks</legend>
        {rubric.map((r, i) => (
          <div key={i} className="inline-form" style={{ marginTop: 0 }}>
            <input className="input" aria-label={`Marking point ${i + 1}`} placeholder="e.g. Says chlorophyll absorbs light" value={r.criterion}
              onChange={e => setRubric(list => list.map((x, j) => (j === i ? { ...x, criterion: e.target.value } : x)))} />
            <select className="input narrow" aria-label={`Marks for point ${i + 1}`} value={r.marks}
              onChange={e => setRubric(list => list.map((x, j) => (j === i ? { ...x, marks: Number(e.target.value) } : x)))}>
              {[1, 2, 3, 4, 5].map(n => <option key={n} value={n}>{n} mark{n > 1 ? 's' : ''}</option>)}
            </select>
            <button type="button" className="icon-action" onClick={() => setRubric(list => list.filter((_, j) => j !== i))} disabled={rubric.length === 1}>Remove</button>
          </div>
        ))}
        <div><button type="button" className="btn btn-ghost btn-sm" onClick={() => setRubric(list => [...list, { criterion: '', marks: 1 }])} disabled={rubric.length >= 10}><Icon name="plus" size={14} /> Add marking point</button></div>
        {errors.rubric && <span className="field-error">{errors.rubric}</span>}
      </fieldset>
      {save.isError && !Object.keys(errors).length && <div className="alert" role="alert">{errorMessage(save.error)}</div>}
      <div className="row"><button className="btn btn-primary" disabled={save.isPending}>Save as draft</button><button type="button" className="btn btn-ghost" onClick={onDone}>Cancel</button></div>
    </form>
  )
}
