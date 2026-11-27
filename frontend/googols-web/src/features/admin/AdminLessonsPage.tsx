import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { Icon } from '../../app/Icons'
import { api, errorMessage, fieldErrors } from '../../core/api/client'
import type { AdminBoard, AdminLesson, AdminLessonDetail } from '../../core/api/types'
import { LessonBody } from '../lessons/LessonBody'
import './admin.css'

const TREE_KEY = ['admin', 'curriculum-tree']

/** Lessons of one chapter: write or paste text, attach a PDF, publish. Published text feeds the AI teacher. */
export function AdminLessonsPage() {
  const { chapterId = '' } = useParams()
  const qc = useQueryClient()
  const tree = useQuery({ queryKey: TREE_KEY, queryFn: () => api.get<AdminBoard[]>('/curriculum/admin/tree') })
  const lessons = useQuery({ queryKey: ['admin', 'lessons', chapterId], queryFn: () => api.get<AdminLesson[]>(`/curriculum/admin/chapters/${chapterId}/lessons`) })
  const [editing, setEditing] = useState<string | 'new' | null>(null)

  const place = findChapter(tree.data, chapterId)
  const refresh = () => qc.invalidateQueries({ queryKey: ['admin', 'lessons', chapterId] })

  return (
    <div>
      <header className="page-head">
        <div>
          <div className="crumbs"><Link to="/admin">Curriculum</Link><Icon name="chevron" size={14} /><span>{place ? `${place.subject} · ${place.className}` : '…'}</span></div>
          <h1>{place ? `${place.sequence}. ${place.title}` : 'Lessons'}</h1>
          <p className="muted">Published lessons appear for students and the AI teacher answers from their text. Drafts stay hidden.</p>
        </div>
        <button className="btn btn-primary" onClick={() => setEditing('new')}><Icon name="plus" size={18} /> New lesson</button>
      </header>

      {place && place.status !== 'Published' && <div className="alert alert-info" style={{ marginBottom: 'var(--space-4)' }}>This chapter is still a draft. Students see its lessons only after you publish the chapter too.</div>}

      {editing && <LessonEditor key={editing} chapterId={chapterId} lessonId={editing === 'new' ? null : editing} onClose={() => { setEditing(null); void refresh() }} />}

      {lessons.isLoading && <div className="skeleton" style={{ height: 120 }} />}
      {lessons.isError && <div className="alert" role="alert">{errorMessage(lessons.error)}</div>}
      {lessons.data?.length === 0 && !editing && <div className="card empty">No lessons yet. Add the first one, or paste the textbook text for this chapter.</div>}
      <ol className="list" style={{ gap: 'var(--space-3)' }}>
        {lessons.data?.map(l => <LessonRow key={l.id} lesson={l} onEdit={() => setEditing(l.id)} onChanged={refresh} />)}
      </ol>
    </div>
  )
}

function LessonRow({ lesson, onEdit, onChanged }: { lesson: AdminLesson; onEdit: () => void; onChanged: () => void }) {
  const published = lesson.status === 'Published'
  const status = useMutation({ mutationFn: () => api.put(`/curriculum/admin/lessons/${lesson.id}/status`, { status: published ? 'Draft' : 'Published' }), onSuccess: onChanged })
  const archive = useMutation({ mutationFn: () => api.del(`/curriculum/admin/lessons/${lesson.id}`), onSuccess: onChanged })
  return (
    <li className="card row-between">
      <div className="row">
        <span className="icon-tile tint-sky" style={{ fontWeight: 800 }}>{lesson.sequence}</span>
        <div>
          <strong>{lesson.title}</strong>
          <div className="small muted">
            {lesson.estimatedMinutes} min · {lesson.bodyLength.toLocaleString()} characters · {lesson.kind === 'OfficialSyllabus' ? 'Textbook' : 'Teacher notes'} · {lesson.language === 'ml' ? 'Malayalam' : 'English'}
            {lesson.attachmentFileName ? ` · 📎 ${lesson.attachmentFileName}` : ''}
          </div>
        </div>
      </div>
      <div className="row">
        <span className={`badge status-${lesson.status.toLowerCase()}`}>{lesson.status}</span>
        <button className="btn btn-ghost btn-sm" onClick={onEdit}>Edit</button>
        <button className={`btn btn-sm ${published ? 'btn-ghost' : 'btn-primary'}`} onClick={() => status.mutate()} disabled={status.isPending}>{published ? 'Unpublish' : 'Publish'}</button>
        <button className="btn btn-ghost btn-sm" onClick={() => archive.mutate()} disabled={archive.isPending} aria-label={`Archive ${lesson.title}`}>Archive</button>
      </div>
      {(status.isError || archive.isError) && <span className="field-error" style={{ flexBasis: '100%' }}>{errorMessage(status.error ?? archive.error)}</span>}
    </li>
  )
}

function LessonEditor({ chapterId, lessonId, onClose }: { chapterId: string; lessonId: string | null; onClose: () => void }) {
  const existing = useQuery({
    queryKey: ['admin', 'lesson', lessonId],
    queryFn: () => api.get<AdminLessonDetail>(`/curriculum/admin/lessons/${lessonId}`),
    enabled: !!lessonId,
  })
  const [title, setTitle] = useState('')
  const [body, setBody] = useState('')
  const [minutes, setMinutes] = useState(10)
  const [kind, setKind] = useState('OfficialSyllabus')
  const [preview, setPreview] = useState(false)
  const [savedId, setSavedId] = useState<string | null>(lessonId)

  useEffect(() => {
    const d = existing.data
    if (!d) return
    // Copy the loaded lesson into the form once it arrives.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTitle(d.title); setBody(d.body); setMinutes(d.estimatedMinutes); setKind(d.kind)
  }, [existing.data])

  const save = useMutation({
    mutationFn: async () => {
      const payload = { title, body, estimatedMinutes: minutes, kind, language: null, sequence: null }
      if (savedId) {
        await api.put(`/curriculum/admin/lessons/${savedId}`, payload)
        return savedId
      }
      const r = await api.post<{ id: string }>(`/curriculum/admin/chapters/${chapterId}/lessons`, payload)
      return r.id
    },
    onSuccess: id => setSavedId(id),
  })
  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return api.upload<{ fileName: string; bytes: number }>(`/curriculum/admin/lessons/${savedId}/attachment`, form)
    },
  })
  const errs = fieldErrors(save.error)

  async function loadText(e: ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0]
    if (!f) return
    setBody(await f.text())
    if (!title) setTitle(f.name.replace(/\.[^.]+$/, ''))
  }
  function submit(e: FormEvent) {
    e.preventDefault()
    save.mutate()
  }

  return (
    <form className="card stack" onSubmit={submit} style={{ marginBottom: 'var(--space-5)' }}>
      <div className="row-between">
        <h2 style={{ margin: 0 }}>{lessonId ? 'Edit lesson' : 'New lesson'}</h2>
        <button type="button" className="btn btn-ghost btn-sm" onClick={onClose}>Close</button>
      </div>
      <div className="inline-form" style={{ marginTop: 0 }}>
        <input className="input" placeholder="Lesson title, e.g. What is photosynthesis?" aria-label="Lesson title" value={title} onChange={e => setTitle(e.target.value)} />
        <input className="input narrow" type="number" min={1} max={180} aria-label="Minutes" value={minutes} onChange={e => setMinutes(Number(e.target.value))} />
        <select className="input narrow" aria-label="Source" value={kind} onChange={e => setKind(e.target.value)}>
          <option value="OfficialSyllabus">Textbook text</option>
          <option value="ApprovedMaterial">Teacher notes</option>
        </select>
      </div>
      {errs.title && <span className="field-error">{errs.title}</span>}
      <div className="row-between">
        <div className="segmented" role="group" aria-label="View">
          <button type="button" aria-pressed={!preview} onClick={() => setPreview(false)}>Write</button>
          <button type="button" aria-pressed={preview} onClick={() => setPreview(true)}>Preview</button>
        </div>
        <label className="btn btn-ghost btn-sm">
          Load .txt / .md file<input type="file" accept=".txt,.md,text/plain,text/markdown" onChange={loadText} hidden />
        </label>
      </div>
      {preview
        ? <div className="nest" style={{ minHeight: 240 }}><LessonBody text={body || '_Nothing to preview yet._'} /></div>
        : <textarea className="input" rows={14} aria-label="Lesson text" value={body} onChange={e => setBody(e.target.value)}
            placeholder={'Paste or type the lesson.\n\n## Use headings like this\n\nLeave a blank line between paragraphs.\n\n- Bullet points\n- start with a dash\n\nUse **bold** for key words.'} />}
      <span className="hint">{body.length.toLocaleString()} characters. Blank lines separate paragraphs; the AI teacher searches this text once the lesson is published.</span>
      {errs.body && <span className="field-error">{errs.body}</span>}
      {save.isError && !Object.keys(errs).length && <div className="alert" role="alert">{errorMessage(save.error)}</div>}
      <div className="row">
        <button className="btn btn-primary" disabled={save.isPending || !title.trim() || !body.trim()}>{save.isPending ? 'Saving…' : savedId ? 'Save changes' : 'Create lesson'}</button>
        {save.isSuccess && <span className="badge badge-success"><Icon name="check" size={14} /> Saved</span>}
      </div>

      <div className="nest">
        <strong>Attachment (optional)</strong>
        <p className="hint" style={{ margin: '4px 0 8px' }}>A PDF of the textbook pages or a diagram image, up to 20 MB. Students can download it from the lesson.</p>
        {savedId ? (
          <div className="row">
            <input type="file" accept="application/pdf,image/png,image/jpeg" aria-label="Attachment file" onChange={e => { const f = e.target.files?.[0]; if (f) upload.mutate(f) }} />
            {upload.isPending && <span className="small muted">Uploading…</span>}
            {upload.data && <span className="badge badge-success">📎 {upload.data.fileName}</span>}
            {!upload.data && existing.data?.attachmentFileName && <span className="badge">📎 {existing.data.attachmentFileName}</span>}
          </div>
        ) : <span className="small muted">Save the lesson first, then add a file.</span>}
        {upload.isError && <div className="field-error" role="alert">{errorMessage(upload.error)}</div>}
      </div>
    </form>
  )
}

function findChapter(tree: AdminBoard[] | undefined, chapterId: string) {
  for (const b of tree ?? []) for (const c of b.curricula) for (const k of c.classes) for (const s of k.subjects) for (const ch of s.chapters) {
    if (ch.id === chapterId) return { title: ch.title, sequence: ch.sequence, status: ch.status, subject: s.name, className: k.gradeName }
  }
  return null
}
