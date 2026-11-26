import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router'
import { api, errorMessage, fieldErrors } from '../../core/api/client'
import type { AcademicClass, Board, CurriculumSummary, Lang } from '../../core/api/types'
import { useStudentMe } from '../../core/queries'
import '../../app/layout.css'

/** Board -> curriculum (academic year) -> class -> language. All options come from the published catalog. */
export function OnboardingPage() {
  const me = useStudentMe()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [displayName, setDisplayName] = useState('')
  const [dob, setDob] = useState('')
  const [boardId, setBoardId] = useState('')
  const [curriculumId, setCurriculumId] = useState('')
  const [classId, setClassId] = useState('')
  const [language, setLanguage] = useState<Lang>('ml')

  const boards = useQuery({ queryKey: ['boards'], queryFn: () => api.get<Board[]>('/curriculum/boards') })
  const curricula = useQuery({
    queryKey: ['curricula', boardId],
    queryFn: () => api.get<CurriculumSummary[]>(`/curriculum/boards/${boardId}/curricula`),
    enabled: !!boardId,
  })
  const classes = useQuery({
    queryKey: ['classes', curriculumId],
    queryFn: () => api.get<AcademicClass[]>(`/curriculum/curricula/${curriculumId}/classes`),
    enabled: !!curriculumId,
  })

  const save = useMutation({
    mutationFn: () => api.post('/students/me/onboarding', {
      displayName, dateOfBirth: dob || null, preferredLanguage: language, curriculumId, classId,
    }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ['student', 'me'] })
      navigate('/', { replace: true })
    },
  })

  if (me.data) return <Navigate to="/" replace />
  const errs = fieldErrors(save.error)

  function submit(e: FormEvent) {
    e.preventDefault()
    save.mutate()
  }

  return (
    <div className="auth-page">
      <form className="card auth-card" onSubmit={submit}>
        <h1>Let's set up your learning</h1>
        <p className="muted">Pick your syllabus and class. You can change language any time.</p>
        {save.isError && !Object.keys(errs).length && <div className="alert" role="alert">{errorMessage(save.error)}</div>}

        <div className="field">
          <label htmlFor="name">Your name</label>
          <input id="name" className="input" required maxLength={100} value={displayName} onChange={e => setDisplayName(e.target.value)} />
          {errs.displayName && <span className="field-error">{errs.displayName}</span>}
        </div>
        <div className="field">
          <label htmlFor="dob">Date of birth</label>
          <input id="dob" className="input" type="date" value={dob} onChange={e => setDob(e.target.value)} />
          <span className="muted" style={{ fontSize: 'var(--text-sm)' }}>Under 18? A parent will be asked to give permission for AI features.</span>
        </div>
        <div className="field">
          <label htmlFor="board">Syllabus</label>
          <select id="board" className="input" required value={boardId}
            onChange={e => { setBoardId(e.target.value); setCurriculumId(''); setClassId('') }} disabled={boards.isLoading}>
            <option value="">{boards.isLoading ? 'Loading…' : 'Choose a syllabus'}</option>
            {boards.data?.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
          {boards.data?.length === 0 && <span className="muted">No syllabus has been published yet. Please check back soon.</span>}
        </div>
        {boardId && (
          <div className="field">
            <label htmlFor="curriculum">Academic year</label>
            <select id="curriculum" className="input" required value={curriculumId}
              onChange={e => { setCurriculumId(e.target.value); setClassId('') }}>
              <option value="">{curricula.isLoading ? 'Loading…' : 'Choose a year'}</option>
              {curricula.data?.map(c => <option key={c.id} value={c.id}>{c.name} ({c.academicYear})</option>)}
            </select>
          </div>
        )}
        {curriculumId && (
          <div className="field">
            <label htmlFor="class">Class</label>
            <select id="class" className="input" required value={classId} onChange={e => setClassId(e.target.value)}>
              <option value="">{classes.isLoading ? 'Loading…' : 'Choose your class'}</option>
              {classes.data?.map(c => <option key={c.id} value={c.id}>{c.gradeName}</option>)}
            </select>
            {errs.classId && <span className="field-error">{errs.classId}</span>}
          </div>
        )}
        <fieldset className="field" style={{ border: 0, padding: 0 }}>
          <legend style={{ fontWeight: 600, fontSize: 'var(--text-sm)' }}>Teaching language</legend>
          <div style={{ display: 'flex', gap: 'var(--space-2)' }}>
            {(['ml', 'en'] as const).map(l => (
              <button type="button" key={l} className={`btn ${language === l ? 'btn-primary' : 'btn-ghost'}`} aria-pressed={language === l} onClick={() => setLanguage(l)}>
                {l === 'ml' ? 'മലയാളം' : 'English'}
              </button>
            ))}
          </div>
        </fieldset>
        <button className="btn btn-primary" style={{ width: '100%' }} disabled={save.isPending || !displayName || !classId}>
          {save.isPending ? 'Saving…' : 'Start learning'}
        </button>
      </form>
    </div>
  )
}
