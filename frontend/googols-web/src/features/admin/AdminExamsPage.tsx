import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Icon } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { AdminBoard, ExamAdmin } from '../../core/api/types'
import { ExamForm, type ExamFormValues } from './ExamForm'
import '../tests/tests.css'
import './admin.css'

/** Mock exams: timed tests published to every student of a class. */
export function AdminExamsPage() {
  const qc = useQueryClient()
  const tree = useQuery({ queryKey: ['admin', 'tree'], queryFn: () => api.get<AdminBoard[]>('/curriculum/admin/tree') })
  const classes = useMemo(() => (tree.data ?? []).flatMap(b => b.curricula.flatMap(c => c.classes.map(k => ({
    id: k.id, label: `${b.code} · ${c.academicYear} · ${k.gradeName}`,
    chapters: k.subjects.flatMap(s => s.chapters.filter(ch => ch.status === 'Published').map(ch => ({ id: ch.id, title: ch.title, subject: s.name }))),
  })))), [tree.data])
  const [classId, setClassId] = useState('')
  const current = classes.find(c => c.id === classId) ?? classes[0]
  const key = ['admin', 'exams', current?.id]
  const exams = useQuery({ queryKey: key, queryFn: () => api.get<ExamAdmin[]>(`/curriculum/admin/exams?classId=${current!.id}`), enabled: !!current })
  const [creating, setCreating] = useState(false)
  const refresh = () => qc.invalidateQueries({ queryKey: ['admin', 'exams'] })
  const create = useMutation({
    mutationFn: (v: ExamFormValues) => api.post('/curriculum/admin/exams', { ...v, classId: current!.id, subjectId: null }),
    onSuccess: () => { setCreating(false); void refresh() },
  })
  const status = useMutation({ mutationFn: (v: { id: string; status: string }) => api.put(`/curriculum/admin/exams/${v.id}/status`, { status: v.status }), onSuccess: refresh })
  const remove = useMutation({ mutationFn: (id: string) => api.del(`/curriculum/admin/exams/${id}`), onSuccess: refresh })

  return (
    <div className="stack-lg">
      <header className="page-head">
        <div><h1>Mock exams</h1><p className="muted">Timed exams for a whole class. Each student gets their own random set of questions from the chosen chapters and one attempt.</p></div>
        <button className="btn btn-primary" onClick={() => setCreating(true)} disabled={!current}><Icon name="plus" size={18} /> New mock exam</button>
      </header>
      <label className="row small">Class
        <select className="input" style={{ width: 'auto' }} value={current?.id ?? ''} onChange={e => setClassId(e.target.value)}>
          {classes.map(c => <option key={c.id} value={c.id}>{c.label}</option>)}
        </select>
      </label>
      {creating && current && (
        <ExamForm chapters={current.chapters} submitLabel="Save as draft" pending={create.isPending} error={create.error}
          onSubmit={v => create.mutate(v)} onCancel={() => setCreating(false)} />
      )}
      {exams.isError && <div className="alert" role="alert">{errorMessage(exams.error)}</div>}
      {exams.data?.length === 0 && !creating && <div className="card empty">No mock exams for this class yet.</div>}
      <div className="stack">
        {exams.data?.map(e => (
          <article key={e.id} className="card row-between">
            <div>
              <strong>{e.title}</strong> <span className={`badge status-${e.status.toLowerCase()}`}>{e.status}</span>
              <div className="small muted">{e.mcqCount} multiple choice · {e.writtenCount} written · {e.durationMinutes} min · {e.chapterIds.length} chapters
                {e.closesAtUtc ? ` · closes ${new Date(e.closesAtUtc).toLocaleString()}` : ''}</div>
              <div className="small muted">{e.submitted} submitted{e.average !== null ? ` · average ${e.average}%` : ''}</div>
            </div>
            <div className="row">
              <button className="btn btn-ghost btn-sm" onClick={() => status.mutate({ id: e.id, status: e.status === 'Published' ? 'Draft' : 'Published' })}>{e.status === 'Published' ? 'Unpublish' : 'Publish'}</button>
              <button className="btn btn-ghost btn-sm" onClick={() => remove.mutate(e.id)}>Archive</button>
            </div>
          </article>
        ))}
      </div>
    </div>
  )
}
