import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState, type FormEvent } from 'react'
import { Icon, type IconName } from '../../app/Icons'
import { api, errorMessage, fieldErrors } from '../../core/api/client'
import type { AdminBoard, AiUsage, EvalCase, EvalRun, EvalRunSummary, Lang, TeacherMode } from '../../core/api/types'
import { MiniChart } from './AdminPlatformPages'
import './admin.css'

type Tab = 'usage' | 'feedback' | 'evals'

const FEATURE_NAMES: Record<string, string> = {
  teacher: 'AI teacher chat',
  'teacher-photo': 'Photo questions',
  'question-draft': 'Question drafting',
  marking: 'Written-answer marking',
  eval: 'Test runs (evals)',
  speech: 'Read-aloud voice',
}

/** Admin: what the AI costs, what students think of its answers, and the golden-set tests. */
export function AdminAiPage() {
  const [tab, setTab] = useState<Tab>('usage')
  const [days, setDays] = useState(14)
  const usage = useQuery({ queryKey: ['admin', 'ai-usage', days], queryFn: () => api.get<AiUsage>(`/admin/ai/usage?days=${days}`) })
  return (
    <div className="stack-lg">
      <header className="page-head">
        <div><h1>AI quality</h1><p className="muted">Usage and cost, student ratings, and test questions that check the AI teacher's answers.</p></div>
        <div role="tablist" aria-label="AI quality sections" className="segmented">
          {(['usage', 'feedback', 'evals'] as const).map(k => (
            <button key={k} role="tab" type="button" aria-selected={tab === k} aria-pressed={tab === k} onClick={() => setTab(k)}>
              {k === 'usage' ? 'Usage & cost' : k === 'feedback' ? 'Student ratings' : 'Test questions'}
            </button>
          ))}
        </div>
      </header>
      {tab !== 'evals' && (
        <label className="row small muted" style={{ gap: 8 }}>Period
          <select className="input" style={{ width: 'auto' }} value={days} onChange={e => setDays(Number(e.target.value))}>
            <option value={7}>Last 7 days</option><option value={14}>Last 14 days</option><option value={30}>Last 30 days</option>
          </select>
        </label>
      )}
      {tab !== 'evals' && usage.isError && <div className="alert" role="alert">{errorMessage(usage.error)}</div>}
      {tab === 'usage' && usage.data && <UsagePanel u={usage.data} />}
      {tab === 'feedback' && usage.data && <FeedbackPanel u={usage.data} />}
      {tab === 'evals' && <EvalsPanel />}
    </div>
  )
}

const inr = (v: number) => `₹${v.toLocaleString('en-IN', { maximumFractionDigits: 2 })}`

function UsagePanel({ u }: { u: AiUsage }) {
  const rated = u.helpful + u.notHelpful
  const tiles: { label: string; value: string; icon: IconName; tint: string }[] = [
    { label: 'AI calls', value: u.totals.calls.toLocaleString('en-IN'), icon: 'sparkles', tint: 'tint-indigo' },
    { label: 'Tokens (in / out)', value: `${u.totals.inputTokens.toLocaleString('en-IN')} / ${u.totals.outputTokens.toLocaleString('en-IN')}`, icon: 'layers', tint: 'tint-sky' },
    { label: 'Estimated cost', value: `${inr(u.totals.costInr)} ($${u.totals.costUsd.toFixed(2)})`, icon: 'chart', tint: 'tint-amber' },
    { label: 'Rated helpful', value: rated ? `${Math.round((100 * u.helpful) / rated)}% of ${rated}` : 'No ratings yet', icon: 'thumbUp', tint: 'tint-lime' },
  ]
  return (
    <>
      <section className="grid-stats">
        {tiles.map(t => (
          <div key={t.label} className="card stat">
            <span className={`icon-tile lg ${t.tint}`}><Icon name={t.icon} /></span>
            <div><div className="stat-value">{t.value}</div><div className="stat-label">{t.label}</div></div>
          </div>
        ))}
      </section>
      <div className="grid">
        <MiniChart title="AI calls per day" points={u.daily.map(d => ({ day: d.day, value: d.calls }))} />
        <MiniChart title="Cost per day" points={u.daily.map(d => ({ day: d.day, value: d.costUsd * u.usdToInr }))} format={inr} />
      </div>
      <section className="card">
        <div className="card-title"><h2>By feature</h2></div>
        {u.byFeature.length === 0 ? <p className="muted">No AI calls in this period.</p> : (
          <table className="table">
            <thead><tr><th>Feature</th><th>Calls</th><th>Tokens</th><th>Cost</th></tr></thead>
            <tbody>
              {u.byFeature.map(f => (
                <tr key={f.feature}><td>{FEATURE_NAMES[f.feature] ?? f.feature}</td><td>{f.calls}</td><td>{f.tokens.toLocaleString('en-IN')}</td><td>{inr(f.costUsd * u.usdToInr)}</td></tr>
              ))}
            </tbody>
          </table>
        )}
        <p className="small muted">Costs use list prices set in app settings (AiPricing__*). Your Azure bill is the final word.</p>
      </section>
    </>
  )
}

function FeedbackPanel({ u }: { u: AiUsage }) {
  return (
    <section className="card">
      <div className="card-title"><h2>Answers marked "not helpful"</h2><span className="badge">{u.helpful} 👍 · {u.notHelpful} 👎</span></div>
      {u.recentNotHelpful.length === 0 && <p className="muted">No answers were marked not helpful in this period.</p>}
      <div className="stack">
        {u.recentNotHelpful.map(f => (
          <article key={f.messageId} className="nest">
            <div className="row-between small muted"><span>{new Date(f.at).toLocaleString()}</span><span className="badge">{f.language === 'ml' ? 'Malayalam' : 'English'}</span></div>
            <p style={{ margin: 'var(--space-2) 0' }}><strong>Question:</strong> {f.question || '—'}</p>
            <details><summary className="small">Answer given</summary><p className="pre-wrap small">{f.answer}</p></details>
            {f.comment && <p className="small" style={{ marginBottom: 0 }}><strong>Student's comment:</strong> {f.comment}</p>}
          </article>
        ))}
      </div>
    </section>
  )
}

interface SubjectOption { id: string; label: string; chapters: { id: string; title: string }[] }

function EvalsPanel() {
  const qc = useQueryClient()
  const cases = useQuery({ queryKey: ['admin', 'evals', 'cases'], queryFn: () => api.get<EvalCase[]>('/admin/ai/evals/cases') })
  const runs = useQuery({ queryKey: ['admin', 'evals', 'runs'], queryFn: () => api.get<EvalRunSummary[]>('/admin/ai/evals/runs') })
  const tree = useQuery({ queryKey: ['admin', 'tree'], queryFn: () => api.get<AdminBoard[]>('/curriculum/admin/tree') })
  const [editing, setEditing] = useState<EvalCase | 'new' | null>(null)
  const [openRun, setOpenRun] = useState<EvalRun | null>(null)
  const subjects = useMemo<SubjectOption[]>(() => (tree.data ?? []).flatMap(b => b.curricula.flatMap(c => c.classes.flatMap(k => k.subjects.map(s => ({
    id: s.id, label: `${b.code} · ${c.academicYear} · ${k.gradeName} · ${s.name}`, chapters: s.chapters.map(ch => ({ id: ch.id, title: ch.title })),
  }))))), [tree.data])

  const run = useMutation({
    mutationFn: () => api.post<EvalRun>('/admin/ai/evals/run'),
    onSuccess: r => { setOpenRun(r); void qc.invalidateQueries({ queryKey: ['admin', 'evals', 'runs'] }); void qc.invalidateQueries({ queryKey: ['admin', 'ai-usage'] }) },
  })
  const remove = useMutation({ mutationFn: (id: string) => api.del(`/admin/ai/evals/cases/${id}`), onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'evals', 'cases'] }) })
  const openOld = useMutation({ mutationFn: (id: string) => api.get<EvalRun>(`/admin/ai/evals/runs/${id}`), onSuccess: setOpenRun })

  return (
    <div className="stack-lg">
      <section className="card">
        <div className="card-title">
          <h2>Test questions</h2>
          <div className="row">
            <button className="btn btn-ghost btn-sm" onClick={() => setEditing('new')}><Icon name="plus" size={16} /> Add question</button>
            <button className="btn btn-primary btn-sm" disabled={run.isPending || !cases.data?.length} onClick={() => run.mutate()}>
              <Icon name="bolt" size={16} /> {run.isPending ? 'Running… (up to a minute)' : 'Run all tests'}
            </button>
          </div>
        </div>
        <p className="small muted">Each test asks the AI teacher a question the way a student would, then checks the answer's language, the key points it must cover, words it must not use, and whether it used the published lessons. Run it after changing lessons, prompts or the AI model.</p>
        {run.isError && <div className="alert" role="alert">{errorMessage(run.error)}</div>}
        {editing && <EvalCaseForm initial={editing === 'new' ? null : editing} subjects={subjects} onDone={() => { setEditing(null); void qc.invalidateQueries({ queryKey: ['admin', 'evals', 'cases'] }) }} />}
        {cases.data?.length === 0 && !editing && <p className="muted">No test questions yet. Add a few for each published chapter, in Malayalam and English.</p>}
        <ul className="chapters">
          {cases.data?.map(c => (
            <li key={c.id} className="row-between">
              <div style={{ minWidth: 0 }}>
                <strong>{c.question}</strong>
                <div className="small muted">{c.subjectName}{c.chapterTitle ? ` · ${c.chapterTitle}` : ''} · {c.language === 'ml' ? 'Malayalam' : 'English'}{c.expectGrounded ? ' · must use lessons' : ''}</div>
              </div>
              <div className="row">
                <button className="icon-action" onClick={() => setEditing(c)}>Edit</button>
                <button className="icon-action" onClick={() => remove.mutate(c.id)}>Remove</button>
              </div>
            </li>
          ))}
        </ul>
      </section>

      {openRun && <RunResults run={openRun} onClose={() => setOpenRun(null)} />}

      <section className="card">
        <div className="card-title"><h2>Past runs</h2></div>
        {runs.data?.length === 0 && <p className="muted">No runs yet.</p>}
        <table className="table">
          <tbody>
            {runs.data?.map(r => (
              <tr key={r.id}>
                <td>{new Date(r.createdAtUtc).toLocaleString()}</td>
                <td><span className={`badge ${r.passed === r.total ? 'badge-success' : 'badge-warning'}`}>{r.passed}/{r.total} passed</span></td>
                <td className="small muted">{r.promptVersion} · {r.model ?? '—'}</td>
                <td><button className="icon-action" onClick={() => openOld.mutate(r.id)}>View</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  )
}

function RunResults({ run, onClose }: { run: EvalRun; onClose: () => void }) {
  return (
    <section className="card" aria-label="Test results">
      <div className="card-title">
        <h2>Results: {run.run.passed}/{run.run.total} passed · average score {Math.round(run.run.averageScore * 100)}%</h2>
        <button className="icon-btn" aria-label="Close results" onClick={onClose}><Icon name="x" size={16} /></button>
      </div>
      <div className="stack">
        {run.results.map(r => (
          <details key={r.caseId} className="nest" open={!r.passed}>
            <summary><span className={`badge ${r.passed ? 'badge-success' : 'badge-warning'}`}>{r.passed ? 'Pass' : 'Fail'}</span> {r.question}</summary>
            <ul className="checks">
              {r.checks.map(c => <li key={c.name} className={c.passed ? 'ok' : 'bad'}><Icon name={c.passed ? 'check' : 'x'} size={14} /> {c.name}{c.detail ? `: ${c.detail}` : ''}</li>)}
            </ul>
            {r.answer && <p className="pre-wrap small">{r.answer}</p>}
          </details>
        ))}
      </div>
    </section>
  )
}

const MODES: { value: TeacherMode; label: string }[] = [
  { value: 'PersonalTeacher', label: 'Personal teacher' }, { value: 'DoubtSolver', label: 'Doubt solver' },
  { value: 'ExamCoach', label: 'Exam coach' }, { value: 'RevisionAssistant', label: 'Revision' }, { value: 'PracticeTeacher', label: 'Practice' },
]

function EvalCaseForm({ initial, subjects, onDone }: { initial: EvalCase | null; subjects: SubjectOption[]; onDone: () => void }) {
  const [subjectId, setSubjectId] = useState(initial?.subjectId ?? '')
  const [chapterId, setChapterId] = useState(initial?.chapterId ?? '')
  const [question, setQuestion] = useState(initial?.question ?? '')
  const [language, setLanguage] = useState<Lang>(initial?.language ?? 'ml')
  const [mode, setMode] = useState<TeacherMode>(initial?.mode ?? 'PersonalTeacher')
  const [expected, setExpected] = useState(initial?.expectedKeywords ?? '')
  const [forbidden, setForbidden] = useState(initial?.forbiddenPhrases ?? '')
  const [grounded, setGrounded] = useState(initial?.expectGrounded ?? true)
  const save = useMutation({
    mutationFn: () => {
      const body = { subjectId, chapterId: chapterId || null, question, language, mode, expectedKeywords: expected, forbiddenPhrases: forbidden, expectGrounded: grounded }
      return initial ? api.put(`/admin/ai/evals/cases/${initial.id}`, body) : api.post('/admin/ai/evals/cases', body)
    },
    onSuccess: onDone,
  })
  const errors = fieldErrors(save.error)
  const chapters = subjects.find(s => s.id === subjectId)?.chapters ?? []
  function submit(e: FormEvent) { e.preventDefault(); save.mutate() }
  return (
    <form className="nest stack" onSubmit={submit} aria-label="Test question">
      <div className="grid-2-even">
        <label className="field">Subject
          <select className="input" value={subjectId} onChange={e => { setSubjectId(e.target.value); setChapterId('') }} required>
            <option value="">Choose…</option>
            {subjects.map(s => <option key={s.id} value={s.id}>{s.label}</option>)}
          </select>
          {errors.subjectId && <span className="field-error">{errors.subjectId}</span>}
        </label>
        <label className="field">Chapter (optional)
          <select className="input" value={chapterId} onChange={e => setChapterId(e.target.value)}>
            <option value="">Any chapter</option>
            {chapters.map(c => <option key={c.id} value={c.id}>{c.title}</option>)}
          </select>
        </label>
      </div>
      <label className="field">Question, written as a student would ask it
        <textarea className="input" rows={2} maxLength={1000} value={question} onChange={e => setQuestion(e.target.value)} required />
        {errors.question && <span className="field-error">{errors.question}</span>}
      </label>
      <div className="grid-2-even">
        <label className="field">Answer language
          <select className="input" value={language} onChange={e => setLanguage(e.target.value as Lang)}><option value="ml">Malayalam</option><option value="en">English</option></select>
        </label>
        <label className="field">Mode
          <select className="input" value={mode} onChange={e => setMode(e.target.value as TeacherMode)}>{MODES.map(m => <option key={m.value} value={m.value}>{m.label}</option>)}</select>
        </label>
      </div>
      <label className="field">Key points the answer must include (one per line; use | for either, e.g. chlorophyll|ഹരിതകം)
        <textarea className="input" rows={3} value={expected} onChange={e => setExpected(e.target.value)} />
      </label>
      <label className="field">Words or claims it must not include (one per line)
        <textarea className="input" rows={2} value={forbidden} onChange={e => setForbidden(e.target.value)} />
      </label>
      <label className="check"><input type="checkbox" checked={grounded} onChange={e => setGrounded(e.target.checked)} /> The answer should come from the published lessons</label>
      {save.isError && !Object.keys(errors).length && <div className="alert" role="alert">{errorMessage(save.error)}</div>}
      <div className="row"><button className="btn btn-primary" disabled={save.isPending}>Save</button><button type="button" className="btn btn-ghost" onClick={onDone}>Cancel</button></div>
    </form>
  )
}
