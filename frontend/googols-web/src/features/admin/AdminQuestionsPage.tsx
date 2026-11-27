import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { Icon } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { AdminQuestion } from '../../core/api/types'
import './admin.css'

const DIFFICULTY = ['', 'Easy', 'Medium', 'Hard']

/** Question bank for one chapter. AI drafts arrive as Draft and must be checked before publishing. */
export function AdminQuestionsPage() {
  const { chapterId = '' } = useParams()
  const qc = useQueryClient()
  const key = ['admin', 'questions', chapterId]
  const questions = useQuery({ queryKey: key, queryFn: () => api.get<AdminQuestion[]>(`/curriculum/admin/chapters/${chapterId}/questions`) })
  const [editing, setEditing] = useState<AdminQuestion | 'new' | null>(null)
  const refresh = () => qc.invalidateQueries({ queryKey: key })
  const generate = useMutation({
    mutationFn: () => api.post<{ created: number }>(`/curriculum/admin/chapters/${chapterId}/questions/generate`, { count: 10 }),
    onSuccess: refresh,
  })
  const published = questions.data?.filter(q => q.status === 'Published').length ?? 0

  return (
    <div>
      <header className="page-head">
        <div>
          <div className="crumbs"><Link to="/admin">Curriculum</Link><Icon name="chevron" size={14} /><Link to={`/admin/chapters/${chapterId}/lessons`}>Lessons</Link><Icon name="chevron" size={14} /><span>Questions</span></div>
          <h1>Question bank</h1>
          <p className="muted">{published} published · students get up to 10 random published questions per quiz.</p>
        </div>
        <div className="row">
          <button className="btn btn-ghost" onClick={() => generate.mutate()} disabled={generate.isPending}><Icon name="sparkles" size={18} /> {generate.isPending ? 'Writing drafts…' : 'Draft 10 with AI'}</button>
          <button className="btn btn-primary" onClick={() => setEditing('new')}><Icon name="plus" size={18} /> New question</button>
        </div>
      </header>
      {generate.data && <div className="alert alert-info" style={{ marginBottom: 'var(--space-4)' }}>{generate.data.created} draft questions added. Check each one, fix anything wrong, then publish.</div>}
      {generate.isError && <div className="alert" role="alert" style={{ marginBottom: 'var(--space-4)' }}>{errorMessage(generate.error)}</div>}

      {editing && <QuestionForm key={editing === 'new' ? 'new' : editing.id} chapterId={chapterId} question={editing === 'new' ? null : editing} onDone={() => { setEditing(null); void refresh() }} />}

      {questions.data?.length === 0 && !editing && <div className="card empty">No questions yet. Write one, or let the AI draft some from this chapter's lessons.</div>}
      <ol className="list" style={{ gap: 'var(--space-3)' }}>
        {questions.data?.map((q, i) => <QuestionRow key={q.id} n={i + 1} q={q} onEdit={() => setEditing(q)} onChanged={refresh} />)}
      </ol>
    </div>
  )
}

function QuestionRow({ n, q, onEdit, onChanged }: { n: number; q: AdminQuestion; onEdit: () => void; onChanged: () => void }) {
  const published = q.status === 'Published'
  const status = useMutation({ mutationFn: () => api.put(`/curriculum/admin/questions/${q.id}/status`, { status: published ? 'Draft' : 'Published' }), onSuccess: onChanged })
  const archive = useMutation({ mutationFn: () => api.del(`/curriculum/admin/questions/${q.id}`), onSuccess: onChanged })
  return (
    <li className="card">
      <div className="row-between">
        <strong>{n}. {q.prompt}</strong>
        <div className="row">
          {q.source === 'AiDraft' && <span className="badge"><Icon name="sparkles" size={12} /> AI draft</span>}
          <span className="badge">{DIFFICULTY[q.difficulty]}</span>
          <span className={`badge status-${q.status.toLowerCase()}`}>{q.status}</span>
        </div>
      </div>
      <ol className="q-options">
        {q.options.map((o, i) => <li key={i} className={i === q.correctIndex ? 'right' : ''}>{i === q.correctIndex && <Icon name="check" size={14} />} {o}</li>)}
      </ol>
      {q.explanation && <p className="small muted">{q.explanation}</p>}
      <div className="row">
        <button className="btn btn-ghost btn-sm" onClick={onEdit}>Edit</button>
        <button className={`btn btn-sm ${published ? 'btn-ghost' : 'btn-primary'}`} onClick={() => status.mutate()} disabled={status.isPending}>{published ? 'Unpublish' : 'Publish'}</button>
        <button className="btn btn-ghost btn-sm" onClick={() => archive.mutate()} disabled={archive.isPending}>Archive</button>
      </div>
    </li>
  )
}

function QuestionForm({ chapterId, question, onDone }: { chapterId: string; question: AdminQuestion | null; onDone: () => void }) {
  const [prompt, setPrompt] = useState(question?.prompt ?? '')
  const [options, setOptions] = useState<string[]>(question?.options ?? ['', '', '', ''])
  const [correct, setCorrect] = useState(question?.correctIndex ?? 0)
  const [explanation, setExplanation] = useState(question?.explanation ?? '')
  const [difficulty, setDifficulty] = useState(question?.difficulty ?? 2)
  const save = useMutation({
    mutationFn: () => {
      const filled = options.map(o => o.trim())
      const body = { prompt, options: filled.filter(Boolean), correctIndex: filled.slice(0, correct).filter(Boolean).length, explanation, difficulty, language: null }
      return question ? api.put(`/curriculum/admin/questions/${question.id}`, body) : api.post(`/curriculum/admin/chapters/${chapterId}/questions`, body)
    },
    onSuccess: onDone,
  })
  function submit(e: FormEvent) {
    e.preventDefault()
    save.mutate()
  }
  return (
    <form className="card stack" onSubmit={submit} style={{ marginBottom: 'var(--space-5)' }}>
      <div className="row-between"><h2 style={{ margin: 0 }}>{question ? 'Edit question' : 'New question'}</h2><button type="button" className="btn btn-ghost btn-sm" onClick={onDone}>Close</button></div>
      <textarea className="input" rows={2} aria-label="Question" placeholder="Question" value={prompt} onChange={e => setPrompt(e.target.value)} />
      <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0 }}>
        <legend className="small" style={{ fontWeight: 650 }}>Options (choose the correct one)</legend>
        {options.map((o, i) => (
          <div key={i} className="row" style={{ flexWrap: 'nowrap' }}>
            <input type="radio" name="correct" aria-label={`Option ${i + 1} is correct`} checked={correct === i} onChange={() => setCorrect(i)} />
            <input className="input" style={{ flex: 1 }} aria-label={`Option ${i + 1}`} placeholder={`Option ${String.fromCharCode(65 + i)}`} value={o}
              onChange={e => setOptions(os => os.map((x, j) => (j === i ? e.target.value : x)))} />
          </div>
        ))}
        {options.length < 6 && <button type="button" className="btn btn-ghost btn-sm" style={{ justifySelf: 'start' }} onClick={() => setOptions(os => [...os, ''])}>+ Add option</button>}
      </fieldset>
      <div className="inline-form" style={{ marginTop: 0 }}>
        <input className="input" aria-label="Explanation" placeholder="Explanation shown after the quiz (optional)" value={explanation} onChange={e => setExplanation(e.target.value)} />
        <select className="input narrow" aria-label="Difficulty" value={difficulty} onChange={e => setDifficulty(Number(e.target.value))}>
          <option value={1}>Easy</option><option value={2}>Medium</option><option value={3}>Hard</option>
        </select>
      </div>
      {save.isError && <div className="alert" role="alert">{errorMessage(save.error)}</div>}
      <div><button className="btn btn-primary" disabled={save.isPending || !prompt.trim() || !options[correct]?.trim()}>{save.isPending ? 'Saving…' : 'Save question'}</button></div>
    </form>
  )
}
