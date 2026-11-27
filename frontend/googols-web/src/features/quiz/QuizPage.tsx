import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { Icon, ProgressRing } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { QuizResult, QuizStart } from '../../core/api/types'
import './quiz.css'

/** Chapter quiz: one question at a time, then a full review with explanations. */
export function QuizPage() {
  const { chapterId } = useParams()
  const qc = useQueryClient()
  const [answers, setAnswers] = useState<(number | null)[]>([])
  const [index, setIndex] = useState(0)
  const start = useMutation({
    mutationFn: () => api.post<QuizStart>(`/learning/chapters/${chapterId}/quiz`),
    onSuccess: q => { setAnswers(q.questions.map(() => null)); setIndex(0) },
  })
  const submit = useMutation({
    mutationFn: () => api.post<QuizResult>(`/learning/quiz-attempts/${start.data!.attemptId}/submit`, { answers }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['learning'] })
      void qc.invalidateQueries({ queryKey: ['student', 'me'] })
    },
  })

  if (submit.data) return <Results result={submit.data} chapterId={chapterId!} onRetry={() => { submit.reset(); start.mutate() }} />

  const quiz = start.data
  if (!quiz) {
    return (
      <div className="card quiz-intro">
        <span className="icon-tile lg tint-amber" style={{ margin: '0 auto' }}><Icon name="bolt" /></span>
        <h1>Chapter quiz</h1>
        <p className="muted">Up to 10 questions. Take your time; you can go back before you submit. Score 90% or more for bonus XP.</p>
        {start.isError && <div className="alert" role="alert">{errorMessage(start.error)}</div>}
        <div className="row" style={{ justifyContent: 'center' }}>
          <Link className="btn btn-ghost" to={`/chapters/${chapterId}`}>Back to chapter</Link>
          <button className="btn btn-primary" onClick={() => start.mutate()} disabled={start.isPending}>{start.isPending ? 'Loading…' : 'Start quiz'}</button>
        </div>
      </div>
    )
  }

  const q = quiz.questions[index]
  const answered = answers.filter(a => a !== null).length
  const last = index === quiz.questions.length - 1
  return (
    <div className="quiz">
      <div className="row-between" style={{ marginBottom: 'var(--space-3)' }}>
        <span className="muted small">{quiz.chapterTitle}</span>
        <span className="badge">Question {index + 1} of {quiz.questions.length}</span>
      </div>
      <div className="bar" style={{ marginBottom: 'var(--space-4)' }}><span style={{ width: `${(answered / quiz.questions.length) * 100}%` }} /></div>
      <section className="card">
        <h2 className="quiz-prompt">{q.prompt}</h2>
        <div className="options" role="radiogroup" aria-label={q.prompt}>
          {q.options.map((o, i) => (
            <button key={i} type="button" role="radio" aria-checked={answers[index] === i}
              className={`option${answers[index] === i ? ' chosen' : ''}`}
              onClick={() => setAnswers(a => a.map((v, j) => (j === index ? i : v)))}>
              <span className="option-letter">{String.fromCharCode(65 + i)}</span><span>{o}</span>
            </button>
          ))}
        </div>
      </section>
      {submit.isError && <div className="alert" role="alert" style={{ marginTop: 'var(--space-3)' }}>{errorMessage(submit.error)}</div>}
      <div className="row-between" style={{ marginTop: 'var(--space-4)' }}>
        <button className="btn btn-ghost" onClick={() => setIndex(i => i - 1)} disabled={index === 0}><Icon name="arrowLeft" size={16} /> Back</button>
        {last
          ? <button className="btn btn-primary" onClick={() => submit.mutate()} disabled={submit.isPending}>{submit.isPending ? 'Checking…' : `Submit (${answered}/${quiz.questions.length} answered)`}</button>
          : <button className="btn btn-primary" onClick={() => setIndex(i => i + 1)}>Next <Icon name="chevron" size={16} /></button>}
      </div>
    </div>
  )
}

function Results({ result, chapterId, onRetry }: { result: QuizResult; chapterId: string; onRetry: () => void }) {
  return (
    <div className="stack-lg quiz">
      <section className="card" style={{ textAlign: 'center' }}>
        <div style={{ display: 'grid', placeItems: 'center' }}>
          <ProgressRing value={result.percent / 100}><strong>{Math.round(result.percent)}%</strong><span className="muted small">{result.score} / {result.total}</span></ProgressRing>
        </div>
        <h1 style={{ marginTop: 'var(--space-3)' }}>{result.percent >= 90 ? 'Brilliant!' : result.percent >= 60 ? 'Well done!' : 'Good try. Keep going!'}</h1>
        <div className="row" style={{ justifyContent: 'center' }}>
          {result.xpAwarded > 0 && <span className="badge badge-warning"><Icon name="star" size={14} /> +{result.xpAwarded} XP</span>}
          {result.streakDays > 0 && <span className="badge badge-success"><Icon name="flame" size={14} /> {result.streakDays}-day streak</span>}
          {result.newBadges?.length ? <Link to="/achievements" className="badge"><Icon name="trophy" size={14} /> New badge!</Link> : null}
        </div>
        <div className="row" style={{ justifyContent: 'center', marginTop: 'var(--space-4)' }}>
          <Link className="btn btn-ghost" to={`/chapters/${chapterId}`}>Back to chapter</Link>
          <button className="btn btn-primary" onClick={onRetry}>Try another quiz</button>
        </div>
      </section>
      <h2>Review</h2>
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
