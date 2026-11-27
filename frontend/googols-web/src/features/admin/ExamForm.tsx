import { useState, type FormEvent } from 'react'
import { Icon } from '../../app/Icons'
import { errorMessage, fieldErrors } from '../../core/api/client'

export interface ExamChapterOption { id: string; title: string; subject: string }
export interface ExamFormValues {
  title: string; chapterIds: string[]; mcqCount: number; writtenCount: number; durationMinutes: number; opensAtUtc: string | null; closesAtUtc: string | null
}

const toUtc = (local: string) => (local ? new Date(local).toISOString() : null)

/** Timed test settings, shared by the admin mock-exam screen and the teacher's class tests. */
export function ExamForm({ chapters, submitLabel, pending, error, onSubmit, onCancel }: {
  chapters: ExamChapterOption[]
  submitLabel: string
  pending: boolean
  error: unknown
  onSubmit: (v: ExamFormValues) => void
  onCancel: () => void
}) {
  const [title, setTitle] = useState('')
  const [chosen, setChosen] = useState<string[]>([])
  const [mcq, setMcq] = useState(20)
  const [written, setWritten] = useState(0)
  const [minutes, setMinutes] = useState(30)
  const [opens, setOpens] = useState('')
  const [closes, setCloses] = useState('')
  const errors = fieldErrors(error)
  function submit(e: FormEvent) {
    e.preventDefault()
    onSubmit({ title, chapterIds: chosen, mcqCount: mcq, writtenCount: written, durationMinutes: minutes, opensAtUtc: toUtc(opens), closesAtUtc: toUtc(closes) })
  }
  return (
    <form className="nest stack" onSubmit={submit} aria-label="Test settings">
      <label className="field">Title
        <input className="input" maxLength={200} value={title} onChange={e => setTitle(e.target.value)} placeholder="e.g. Term 1 Biology mock exam" required />
        {errors.title && <span className="field-error">{errors.title}</span>}
      </label>
      <fieldset className="field">
        <legend>Chapters</legend>
        <div className="chips">
          {chapters.map(c => (
            <label key={c.id} className={`chip${chosen.includes(c.id) ? ' on' : ''}`}>
              <input type="checkbox" checked={chosen.includes(c.id)} onChange={e => setChosen(l => (e.target.checked ? [...l, c.id] : l.filter(x => x !== c.id)))} />
              {c.subject} · {c.title}
            </label>
          ))}
          {chapters.length === 0 && <span className="muted small">No published chapters for this class yet.</span>}
        </div>
        {errors.chapterIds && <span className="field-error">{errors.chapterIds}</span>}
      </fieldset>
      <div className="inline-form" style={{ marginTop: 0 }}>
        <label className="row small">Multiple choice <input className="input narrow" type="number" min={0} max={50} value={mcq} onChange={e => setMcq(Number(e.target.value))} /></label>
        <label className="row small">Written <input className="input narrow" type="number" min={0} max={5} value={written} onChange={e => setWritten(Number(e.target.value))} /></label>
        <label className="row small">Minutes <input className="input narrow" type="number" min={5} max={180} value={minutes} onChange={e => setMinutes(Number(e.target.value))} /></label>
      </div>
      {(errors.mcqCount || errors.durationMinutes) && <span className="field-error">{errors.mcqCount ?? errors.durationMinutes}</span>}
      <div className="inline-form" style={{ marginTop: 0 }}>
        <label className="row small">Opens (optional) <input className="input" type="datetime-local" value={opens} onChange={e => setOpens(e.target.value)} /></label>
        <label className="row small">Closes (optional) <input className="input" type="datetime-local" value={closes} onChange={e => setCloses(e.target.value)} /></label>
      </div>
      {errors.closesAtUtc && <span className="field-error">{errors.closesAtUtc}</span>}
      {!!error && !Object.keys(errors).length && <div className="alert" role="alert">{errorMessage(error)}</div>}
      <div className="row">
        <button className="btn btn-primary" disabled={pending || !title.trim() || chosen.length === 0}><Icon name="clock" size={16} /> {submitLabel}</button>
        <button type="button" className="btn btn-ghost" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}
