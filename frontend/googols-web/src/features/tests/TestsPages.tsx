import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Icon, ProgressRing } from '../../app/Icons'
import { api, errorMessage, fieldErrors } from '../../core/api/client'
import type { ChapterProgress, ExamAttempt, ExamHistory, ExamResult, ExamSummary } from '../../core/api/types'
import { useStudentMe, useSubjects } from '../../core/queries'
import { Empty } from '../student/DashboardPage'
import '../quiz/quiz.css'
import './tests.css'

const KIND_LABEL = { Practice: 'Practice test', Mock: 'Mock exam', ClassTest: 'Class test' } as const

const when = (iso: string | null) => (iso ? new Date(iso).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : null)

/** Tests & exams: mock exams for the class, tests from teachers, self-made timed practice tests and past results. */
export function TestsPage() {
  const navigate = useNavigate()
  const exams = useQuery({ queryKey: ['learning', 'exams'], queryFn: () => api.get<ExamSummary[]>('/learning/exams') })
  const history = useQuery({ queryKey: ['learning', 'exam-history'], queryFn: () => api.get<ExamHistory[]>('/learning/exam-attempts') })
  const start = useMutation({
    mutationFn: (examId: string) => api.post<ExamAttempt>(`/learning/exams/${examId}/start`),
    onSuccess: a => navigate(`/tests/attempts/${a.attemptId}`),
  })

  return (
    <div className="stack-lg">
      <header className="page-head">
        <div><h1>Tests & exams</h1><p className="muted">Timed tests like the real exam. Multiple-choice answers are marked straight away; written answers get a provisional mark from the AI that your teacher can confirm.</p></div>
      </header>

      <section className="card">
        <div className="card-title"><h2>Set for you</h2></div>
        {exams.isError && <div className="alert" role="alert">{errorMessage(exams.error)}</div>}
        {exams.data?.length === 0 && <Empty icon="clipboard" text="No mock exams or class tests right now. Build a practice test below." />}
        {start.isError && <div className="alert" role="alert">{errorMessage(start.error)}</div>}
        <div className="grid">
          {exams.data?.map(e => (
            <article key={e.id} className="list-item exam-card">
              <div className="row-between">
                <span className={`badge ${e.kind === 'Mock' ? '' : 'badge-success'}`}>{e.classroom ?? KIND_LABEL[e.kind]}</span>
                <span className="small muted"><Icon name="clock" size={14} /> {e.durationMinutes} min</span>
              </div>
              <strong>{e.title}</strong>
              <span className="small muted">{e.subject ? `${e.subject} · ` : ''}{e.mcqCount} multiple choice{e.writtenCount ? ` · ${e.writtenCount} written` : ''}</span>
              {e.closesAtUtc && <span className="small muted">Closes {when(e.closesAtUtc)}</span>}
              {e.submitted
                ? <Link className="btn btn-ghost btn-sm" to={`/tests/attempts/${e.myAttemptId}`}>See result{e.percent !== null ? ` · ${Math.round(e.percent)}%` : ''}</Link>
                : <button className="btn btn-primary btn-sm" disabled={!e.canStart || start.isPending} onClick={() => start.mutate(e.id)}>
                    {e.myAttemptId ? 'Continue' : e.canStart ? 'Start' : e.opensAtUtc ? `Opens ${when(e.opensAtUtc)}` : 'Closed'}
                  </button>}
            </article>
          ))}
        </div>
      </section>

      <PracticeBuilder onStarted={id => navigate(`/tests/attempts/${id}`)} />

      <section className="card">
        <div className="card-title"><h2>My results</h2></div>
        {history.data?.length === 0 && <p className="muted">No tests taken yet.</p>}
        <table className="table">
          <tbody>
            {history.data?.map(h => (
              <tr key={h.attemptId}>
                <td><Link to={`/tests/attempts/${h.attemptId}`}>{h.title}</Link><div className="small muted">{KIND_LABEL[h.kind]} · {when(h.startedAtUtc)}</div></td>
                <td>{h.submittedAtUtc ? (h.percent === null ? '—' : `${Math.round(h.percent)}%`) : <span className="badge badge-warning">Not submitted</span>}</td>
                <td>{h.provisional && h.submittedAtUtc && <span className="badge">Marks provisional</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  )
}

function PracticeBuilder({ onStarted }: { onStarted: (attemptId: string) => void }) {
  const me = useStudentMe()
  const subjects = useSubjects(me.data?.enrollment?.classId)
  const [subjectId, setSubjectId] = useState('')
  const [chosen, setChosen] = useState<string[]>([])
  const [count, setCount] = useState(10)
  const [minutes, setMinutes] = useState(15)
  const [written, setWritten] = useState(false)
  const chapters = useQuery({
    queryKey: ['learning', 'chapters', subjectId],
    queryFn: () => api.get<ChapterProgress[]>(`/learning/subjects/${subjectId}/chapters`),
    enabled: !!subjectId,
  })
  const create = useMutation({
    mutationFn: () => api.post<ExamAttempt>('/learning/practice-tests', { subjectId, chapterIds: chosen, questionCount: count, minutes, includeWritten: written }),
    onSuccess: a => onStarted(a.attemptId),
  })
  const errors = fieldErrors(create.error)
  function submit(e: FormEvent) { e.preventDefault(); create.mutate() }
  return (
    <form className="card stack" onSubmit={submit} aria-label="Build a practice test">
      <div className="card-title"><h2>Build a practice test</h2></div>
      <div className="inline-form" style={{ marginTop: 0 }}>
        <select className="input" aria-label="Subject" value={subjectId} onChange={e => { setSubjectId(e.target.value); setChosen([]) }}>
          <option value="">Choose a subject…</option>
          {subjects.data?.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
        </select>
        <label className="row small">Questions
          <select className="input narrow" value={count} onChange={e => setCount(Number(e.target.value))}>{[5, 10, 15, 20, 30].map(n => <option key={n}>{n}</option>)}</select>
        </label>
        <label className="row small">Minutes
          <select className="input narrow" value={minutes} onChange={e => setMinutes(Number(e.target.value))}>{[5, 10, 15, 20, 30, 45, 60].map(n => <option key={n}>{n}</option>)}</select>
        </label>
      </div>
      {subjectId && (
        <fieldset className="chips" aria-label="Chapters">
          {chapters.data?.map(c => (
            <label key={c.id} className={`chip${chosen.includes(c.id) ? ' on' : ''}`}>
              <input type="checkbox" checked={chosen.includes(c.id)} onChange={e => setChosen(list => (e.target.checked ? [...list, c.id] : list.filter(x => x !== c.id)))} />
              {c.sequence}. {c.title}
            </label>
          ))}
          {chapters.data?.length === 0 && <span className="muted small">No chapters published yet.</span>}
        </fieldset>
      )}
      {errors.chapterIds && <span className="field-error">{errors.chapterIds}</span>}
      <label className="check"><input type="checkbox" checked={written} onChange={e => setWritten(e.target.checked)} /> Add 2 written questions (marked by the AI, provisional)</label>
      {create.isError && !Object.keys(errors).length && <div className="alert" role="alert">{errorMessage(create.error)}</div>}
      <div><button className="btn btn-primary" disabled={!subjectId || chosen.length === 0 || create.isPending}><Icon name="clock" size={18} /> {create.isPending ? 'Preparing…' : `Start ${minutes}-minute test`}</button></div>
    </form>
  )
}

const fmt = (ms: number) => {
  const s = Math.max(0, Math.ceil(ms / 1000))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const sec = s % 60
  return `${h ? `${h}:` : ''}${String(m).padStart(h ? 2 : 1, '0')}:${String(sec).padStart(2, '0')}`
}

/** Runs one timed attempt: countdown, autosave, auto-submit at zero, then the result. */
export function ExamAttemptPage() {
  const { attemptId } = useParams()
  const qc = useQueryClient()
  const attempt = useQuery({ queryKey: ['learning', 'exam-attempt', attemptId], queryFn: () => api.get<ExamAttempt>(`/learning/exam-attempts/${attemptId}`), enabled: !!attemptId })
  if (attempt.isLoading) return <div className="skeleton" style={{ height: 300 }} />
  if (attempt.isError || !attempt.data) return <div className="alert" role="alert">{errorMessage(attempt.error)}</div>
  const a = attempt.data
  if (a.result) return <ExamResultView title={a.title} result={a.result} />
  return <Runner key={a.attemptId} attempt={a} onDone={() => void qc.invalidateQueries({ queryKey: ['learning'] })} />
}

function Runner({ attempt, onDone }: { attempt: ExamAttempt; onDone: () => void }) {
  const [answers, setAnswers] = useState<(number | null)[]>(attempt.answers)
  const [written, setWritten] = useState<Record<string, string>>(() => Object.fromEntries(attempt.writtenAnswers.map(w => [w.questionId, w.text])))
  const offset = useRef(new Date(attempt.serverNowUtc).getTime() - Date.now())
  const deadline = new Date(attempt.deadlineUtc).getTime()
  const [left, setLeft] = useState(() => deadline - (Date.now() + offset.current))
  const [dirty, setDirty] = useState(false)
  const [saved, setSaved] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [result, setResult] = useState<ExamResult | null>(null)
  const submittedRef = useRef(false)

  const payload = useCallback(() => ({ answers, written: Object.entries(written).map(([questionId, text]) => ({ questionId, text })) }), [answers, written])

  const save = useMutation({
    mutationFn: () => api.put(`/learning/exam-attempts/${attempt.attemptId}/answers`, payload()),
    onSuccess: () => { setDirty(false); setSaved(new Date().toLocaleTimeString([], { timeStyle: 'short' })) },
  })
  const submit = useMutation({
    mutationFn: () => api.post<ExamResult>(`/learning/exam-attempts/${attempt.attemptId}/submit`, payload()),
    onSuccess: r => { setResult(r); onDone() },
    onError: () => { submittedRef.current = false },
  })
  const doSubmit = useCallback(() => {
    if (submittedRef.current) return
    submittedRef.current = true
    submit.mutate()
  }, [submit])

  useEffect(() => {
    const t = setInterval(() => setLeft(deadline - (Date.now() + offset.current)), 1000)
    return () => clearInterval(t)
  }, [deadline])
  useEffect(() => { if (left <= 0) doSubmit() }, [left, doSubmit])
  useEffect(() => {
    if (!dirty || submit.isPending || submittedRef.current) return
    const t = setTimeout(() => save.mutate(), 2500)
    return () => clearTimeout(t)
  }, [dirty, answers, written, save, submit.isPending])

  if (result) return <ExamResultView title={attempt.title} result={result} />

  const answered = answers.filter(x => x !== null).length + attempt.written.filter(w => (written[w.id] ?? '').trim()).length
  const total = attempt.questions.length + attempt.written.length
  const low = left < 60_000

  return (
    <div className="stack-lg quiz exam">
      <div className="exam-bar card" role="timer" aria-live="off">
        <div><strong>{attempt.title}</strong><div className="small muted">{answered}/{total} answered{saved ? ` · saved ${saved}` : ''}{save.isError ? ' · not saved, retrying' : ''}</div></div>
        <span className={`timer${low ? ' low' : ''}`} aria-label={`Time left ${fmt(left)}`}><Icon name="clock" size={18} /> {fmt(left)}</span>
      </div>

      {attempt.questions.map((q, i) => (
        <section key={q.id} className="card" aria-label={`Question ${i + 1}`}>
          <p className="quiz-prompt"><span className="muted">{i + 1}.</span> {q.prompt}</p>
          <div className="options" role="radiogroup" aria-label={`Question ${i + 1} options`}>
            {q.options.map((o, j) => (
              <button key={j} type="button" role="radio" aria-checked={answers[i] === j} className={`option${answers[i] === j ? ' chosen' : ''}`}
                onClick={() => { setAnswers(list => list.map((v, k) => (k === i ? j : v))); setDirty(true) }}>
                <span className="option-letter">{String.fromCharCode(65 + j)}</span><span>{o}</span>
              </button>
            ))}
          </div>
        </section>
      ))}

      {attempt.written.map((w, i) => (
        <section key={w.id} className="card">
          <label className="field" style={{ marginBottom: 0 }}>
            <span className="quiz-prompt"><span className="muted">{attempt.questions.length + i + 1}.</span> {w.prompt} <span className="badge">{w.maxMarks} marks</span></span>
            <textarea className="input" rows={6} maxLength={3000} value={written[w.id] ?? ''} aria-label={`Answer to: ${w.prompt}`}
              onChange={e => { const v = e.target.value; setWritten(m => ({ ...m, [w.id]: v })); setDirty(true) }} />
          </label>
        </section>
      ))}

      {submit.isError && <div className="alert" role="alert">{errorMessage(submit.error)}</div>}
      <div className="card row-between">
        {confirming
          ? <><span>Submit now? {total - answered > 0 ? `${total - answered} question(s) are not answered.` : 'All questions answered.'}</span>
              <div className="row"><button className="btn btn-ghost" onClick={() => setConfirming(false)}>Keep working</button>
                <button className="btn btn-primary" onClick={doSubmit} disabled={submit.isPending}>{submit.isPending ? 'Marking…' : 'Submit test'}</button></div></>
          : <><span className="small muted">Your answers save automatically. The test submits itself when the time runs out.</span>
              <button className="btn btn-primary" onClick={() => setConfirming(true)} disabled={submit.isPending}>{submit.isPending ? 'Marking…' : `Submit (${answered}/${total})`}</button></>}
      </div>
    </div>
  )
}

export function ExamResultView({ title, result }: { title: string; result: ExamResult }) {
  const percent = result.percent ?? 0
  return (
    <div className="stack-lg quiz">
      <section className="card" style={{ textAlign: 'center' }}>
        <div style={{ display: 'grid', placeItems: 'center' }}>
          <ProgressRing value={percent / 100}><strong>{Math.round(percent)}%</strong><span className="muted small">{title}</span></ProgressRing>
        </div>
        <h1 style={{ marginTop: 'var(--space-3)' }}>{percent >= 90 ? 'Brilliant!' : percent >= 60 ? 'Well done!' : 'Good try. Keep going!'}</h1>
        <p className="muted">
          Multiple choice {result.mcqScore}/{result.mcqTotal}
          {result.writtenMax > 0 && <> · Written {result.writtenScore ?? 0}/{result.writtenMax}</>}
        </p>
        {result.late && <p className="small text-warning">Time ran out before you submitted, so your last saved answers were marked.</p>}
        {result.provisional && <p className="small"><span className="badge">Provisional</span> Written answers were marked by the AI. Your teacher may change the marks.</p>}
        <div className="row" style={{ justifyContent: 'center' }}>
          {result.xpAwarded > 0 && <span className="badge badge-warning"><Icon name="star" size={14} /> +{result.xpAwarded} XP</span>}
          {result.newBadges?.length ? <Link to="/achievements" className="badge"><Icon name="trophy" size={14} /> New badge!</Link> : null}
        </div>
        <div className="row" style={{ justifyContent: 'center', marginTop: 'var(--space-4)' }}>
          <Link className="btn btn-ghost" to="/tests">Back to tests</Link>
          <Link className="btn btn-primary" to="/progress">See my progress</Link>
        </div>
      </section>

      {result.written.map((w, i) => (
        <section key={w.questionId} className="card">
          <div className="row-between">
            <strong>Written {i + 1}. {w.prompt}</strong>
            <span className={`badge ${w.status === 'Confirmed' ? 'badge-success' : ''}`}>
              {w.score === null ? 'Waiting for teacher' : `${w.score}/${w.max}`}{w.status === 'Provisional' ? ' · provisional' : w.status === 'Confirmed' ? ' · confirmed' : ''}
            </span>
          </div>
          <p className="pre-wrap answer-box">{w.answer || <em className="muted">No answer written.</em>}</p>
          {w.criteria.length > 0 && (
            <ul className="checks">
              {w.criteria.map(c => (
                <li key={c.criterion} className={c.awarded >= c.max ? 'ok' : c.awarded > 0 ? '' : 'bad'}>
                  <strong>{c.awarded}/{c.max}</strong> {c.criterion}{c.comment ? ` · ${c.comment}` : ''}
                </li>
              ))}
            </ul>
          )}
          {w.feedback && <p className="small"><strong>How to improve:</strong> {w.feedback}</p>}
          {w.teacherComment && <p className="small"><strong>Teacher:</strong> {w.teacherComment}</p>}
          {w.modelAnswer && <details><summary className="small">Model answer</summary><p className="pre-wrap small">{w.modelAnswer}</p></details>}
        </section>
      ))}

      {result.review.length > 0 && <h2>Multiple-choice review</h2>}
      {result.review.map((r, i) => (
        <section key={r.questionId} className="card">
          <div className="row-between"><strong>{i + 1}. {r.prompt}</strong>{r.isCorrect ? <span className="badge badge-success">Correct</span> : <span className="badge badge-warning">Not quite</span>}</div>
          <ul className="review-options">
            {r.options.map((o, j) => (
              <li key={j} className={j === r.correct ? 'right' : j === r.chosen ? 'wrong' : ''}>
                {j === r.correct ? <Icon name="check" size={16} /> : <span style={{ width: 16 }} />} {o}{j === r.chosen ? ' (your answer)' : ''}
              </li>
            ))}
          </ul>
          {r.explanation && <p className="small muted" style={{ margin: 0 }}>{r.explanation}</p>}
        </section>
      ))}
    </div>
  )
}
