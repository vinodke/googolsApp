import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent, type ReactNode } from 'react'
import { api, errorMessage } from '../../core/api/client'
import type { AdminBoard, AdminClass, AdminCurriculum, AdminSubject, PubStatus } from '../../core/api/types'
import './admin.css'

const TREE_KEY = ['admin', 'curriculum-tree']

/**
 * Catalog manager: Board → Academic year → Class → Subject → Chapter.
 * Everything starts as Draft. Students see an academic year only when it is Published,
 * and a chapter only when that chapter is Published too.
 */
export function AdminCurriculumPage() {
  const tree = useQuery({ queryKey: TREE_KEY, queryFn: () => api.get<AdminBoard[]>('/curriculum/admin/tree') })
  const [boardId, setBoardId] = useState<string | null>(null)
  const board = tree.data?.find(b => b.id === boardId) ?? tree.data?.[0]

  return (
    <div className="admin">
      <header className="admin-head">
        <h1>Curriculum</h1>
        <p className="muted">Add the syllabus level by level, then publish. Students only see published academic years and chapters.</p>
      </header>

      {tree.isLoading && <div className="skeleton" style={{ height: 200 }} />}
      {tree.isError && <div className="alert" role="alert">{errorMessage(tree.error)}</div>}

      {tree.data && (
        <div className="admin-grid">
          <aside className="card admin-boards">
            <h2>Boards</h2>
            {tree.data.length === 0 && <p className="muted">No boards yet. Add the first one below.</p>}
            <ul>
              {tree.data.map(b => (
                <li key={b.id}>
                  <button className={`board-btn${board?.id === b.id ? ' active' : ''}`} onClick={() => setBoardId(b.id)}>
                    <strong>{b.name}</strong>
                    <span className="muted">{b.code}{b.isEntranceExam ? ' · entrance' : ''}</span>
                  </button>
                </li>
              ))}
            </ul>
            <AddBoardForm onCreated={id => setBoardId(id)} />
          </aside>

          <section className="admin-main">
            {board ? <BoardPanel board={board} /> : <div className="card empty">Add a board to start, for example "Kerala State (SCERT)" with code KL-SCERT.</div>}
          </section>
        </div>
      )}
    </div>
  )
}

function BoardPanel({ board }: { board: AdminBoard }) {
  return (
    <div className="stack">
      <div className="card">
        <h2>{board.name}</h2>
        <p className="muted">{board.code} · {board.countryCode}</p>
        <AddCurriculumForm boardId={board.id} />
      </div>
      {board.curricula.length === 0 && <div className="card empty">No academic years yet for this board.</div>}
      {board.curricula.map(c => <CurriculumPanel key={c.id} curriculum={c} />)}
    </div>
  )
}

function CurriculumPanel({ curriculum }: { curriculum: AdminCurriculum }) {
  const published = curriculum.status === 'Published'
  return (
    <div className="card">
      <div className="row-between">
        <div>
          <h3>{curriculum.name} · {curriculum.academicYear} <span className="muted">v{curriculum.version}</span></h3>
          <StatusBadge status={curriculum.status} />
        </div>
        <StatusToggle path={`/curriculum/admin/curricula/${curriculum.id}/status`} published={published}
          disabledReason={!published && curriculum.classes.length === 0 ? 'Add a class first' : undefined} />
      </div>
      <div className="stack" style={{ marginTop: 'var(--space-4)' }}>
        {curriculum.classes.map(k => <ClassPanel key={k.id} cls={k} />)}
        <AddClassForm curriculumId={curriculum.id} existing={curriculum.classes.map(k => k.gradeNumber)} />
      </div>
    </div>
  )
}

function ClassPanel({ cls }: { cls: AdminClass }) {
  return (
    <details className="nest" open>
      <summary><strong>{cls.gradeName}</strong> <span className="muted">· {cls.subjects.length} subject{cls.subjects.length === 1 ? '' : 's'}</span></summary>
      <div className="stack nest-body">
        {cls.subjects.map(s => <SubjectPanel key={s.id} subject={s} />)}
        <AddSubjectForm classId={cls.id} nextSequence={cls.subjects.length + 1} />
      </div>
    </details>
  )
}

function SubjectPanel({ subject }: { subject: AdminSubject }) {
  return (
    <details className="nest" open>
      <summary>
        <strong>{subject.name}</strong> <span className="muted">· {subject.code} · {subject.mediumLanguage === 'ml' ? 'Malayalam' : 'English'} medium</span>
      </summary>
      <ol className="chapters">
        {subject.chapters.map(ch => (
          <li key={ch.id} className="row-between">
            <span><span className="muted">{ch.sequence}.</span> {ch.title} <StatusBadge status={ch.status} /></span>
            <StatusToggle path={`/curriculum/admin/chapters/${ch.id}/status`} published={ch.status === 'Published'} small />
          </li>
        ))}
      </ol>
      <AddChapterForm subjectId={subject.id} nextSequence={subject.chapters.length + 1} />
    </details>
  )
}

function StatusBadge({ status }: { status: PubStatus }) {
  return <span className={`badge status-${status.toLowerCase()}`}>{status}</span>
}

function StatusToggle({ path, published, small, disabledReason }: { path: string; published: boolean; small?: boolean; disabledReason?: string }) {
  const qc = useQueryClient()
  const m = useMutation({
    mutationFn: () => api.put(path, { status: published ? 'Draft' : 'Published' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: TREE_KEY }),
  })
  return (
    <span className="toggle-wrap">
      <button className={`btn ${published ? 'btn-ghost' : 'btn-primary'}${small ? ' btn-sm' : ''}`} disabled={m.isPending || !!disabledReason}
        title={disabledReason} onClick={() => m.mutate()}>
        {m.isPending ? '…' : published ? 'Unpublish' : 'Publish'}
      </button>
      {m.isError && <span className="field-error">{errorMessage(m.error)}</span>}
    </span>
  )
}

/** Small inline form: renders fields, posts, refreshes the tree, shows the server's validation message. */
function InlineForm({ label, onSubmit, children, canSubmit }: { label: string; onSubmit: () => Promise<unknown>; children: ReactNode; canSubmit: boolean }) {
  const qc = useQueryClient()
  const m = useMutation({ mutationFn: onSubmit, onSuccess: () => qc.invalidateQueries({ queryKey: TREE_KEY }) })
  function submit(e: FormEvent) {
    e.preventDefault()
    m.mutate()
  }
  return (
    <form className="inline-form" onSubmit={submit}>
      {children}
      <button className="btn btn-chip" disabled={!canSubmit || m.isPending}>{m.isPending ? 'Saving…' : `+ ${label}`}</button>
      {m.isError && <span className="field-error" role="alert">{errorMessage(m.error)}</span>}
    </form>
  )
}

function AddBoardForm({ onCreated }: { onCreated: (id: string) => void }) {
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [entrance, setEntrance] = useState(false)
  return (
    <InlineForm label="Add board" canSubmit={!!name && !!code}
      onSubmit={async () => {
        const b = await api.post<{ id: string }>('/curriculum/boards', { name, code: code.toUpperCase(), countryCode: 'IN', isEntranceExam: entrance })
        setName(''); setCode(''); setEntrance(false); onCreated(b.id)
      }}>
      <input className="input" placeholder="Board name, e.g. Kerala State (SCERT)" value={name} onChange={e => setName(e.target.value)} aria-label="Board name" />
      <input className="input" placeholder="Code, e.g. KL-SCERT" value={code} onChange={e => setCode(e.target.value)} aria-label="Board code" />
      <label className="check"><input type="checkbox" checked={entrance} onChange={e => setEntrance(e.target.checked)} /> Entrance exam (NEET, JEE, KEAM…)</label>
    </InlineForm>
  )
}

function AddCurriculumForm({ boardId }: { boardId: string }) {
  const [name, setName] = useState('')
  const [year, setYear] = useState('2026-27')
  return (
    <InlineForm label="Add academic year" canSubmit={!!name && !!year}
      onSubmit={async () => { await api.post('/curriculum/admin/curricula', { boardId, name, academicYear: year }); setName('') }}>
      <input className="input" placeholder="Name, e.g. Kerala State Syllabus" value={name} onChange={e => setName(e.target.value)} aria-label="Curriculum name" />
      <input className="input narrow" placeholder="2026-27" value={year} onChange={e => setYear(e.target.value)} aria-label="Academic year" />
    </InlineForm>
  )
}

function AddClassForm({ curriculumId, existing }: { curriculumId: string; existing: number[] }) {
  const free = Array.from({ length: 12 }, (_, i) => i + 1).filter(n => !existing.includes(n))
  const [grade, setGrade] = useState<number | ''>('')
  return (
    <InlineForm label="Add class" canSubmit={grade !== ''}
      onSubmit={async () => { await api.post('/curriculum/admin/classes', { curriculumId, gradeNumber: grade, gradeName: `Class ${grade}` }); setGrade('') }}>
      <select className="input narrow" value={grade} onChange={e => setGrade(e.target.value ? Number(e.target.value) : '')} aria-label="Class">
        <option value="">Class…</option>
        {free.map(n => <option key={n} value={n}>Class {n}</option>)}
      </select>
    </InlineForm>
  )
}

function AddSubjectForm({ classId, nextSequence }: { classId: string; nextSequence: number }) {
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [medium, setMedium] = useState<'ml' | 'en'>('ml')
  return (
    <InlineForm label="Add subject" canSubmit={!!name && !!code}
      onSubmit={async () => {
        await api.post('/curriculum/admin/subjects', { classId, name, code, mediumLanguage: medium, sequence: nextSequence })
        setName(''); setCode('')
      }}>
      <input className="input" placeholder="Subject, e.g. Biology" value={name} onChange={e => setName(e.target.value)} aria-label="Subject name" />
      <input className="input narrow" placeholder="Code, e.g. BIO-8" value={code} onChange={e => setCode(e.target.value)} aria-label="Subject code" />
      <select className="input narrow" value={medium} onChange={e => setMedium(e.target.value as 'ml' | 'en')} aria-label="Medium">
        <option value="ml">Malayalam medium</option>
        <option value="en">English medium</option>
      </select>
    </InlineForm>
  )
}

function AddChapterForm({ subjectId, nextSequence }: { subjectId: string; nextSequence: number }) {
  const [title, setTitle] = useState('')
  return (
    <InlineForm label="Add chapter" canSubmit={!!title}
      onSubmit={async () => { await api.post('/curriculum/chapters', { subjectId, title, sequence: nextSequence }); setTitle('') }}>
      <input className="input" placeholder={`Chapter ${nextSequence} title`} value={title} onChange={e => setTitle(e.target.value)} aria-label="Chapter title" />
    </InlineForm>
  )
}
