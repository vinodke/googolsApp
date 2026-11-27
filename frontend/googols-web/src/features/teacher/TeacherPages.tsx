import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, Navigate, Outlet, useParams } from 'react-router'
import { AppShell, PageLoading, type NavItem } from '../../app/AppShell'
import { Icon } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { AcademicClass, Board, Classroom, ClassroomDetail, CurriculumSummary } from '../../core/api/types'
import { isAdmin, isSchoolAdmin, isTeacher, useAccount } from '../../core/queries'
import { Empty } from '../student/DashboardPage'
import { ClassTests, Marking } from './TeacherAssessment'
import { ClassHeatmap, ClassHomework, TeacherTimetable } from './TeacherStageC'
import '../tests/tests.css'

export function TeacherLayout() {
  const account = useAccount()
  if (account.isLoading) return <PageLoading />
  if (!isTeacher(account.data) && !isAdmin(account.data)) return <Navigate to="/" replace />
  const links: NavItem[] = [
    { to: '/teacher', label: 'My classrooms', icon: 'home', end: true },
    ...(isSchoolAdmin(account.data) ? [{ to: '/school', label: 'My school', icon: 'school' } as NavItem] : []),
    ...(isAdmin(account.data) ? [{ to: '/admin', label: 'Admin', icon: 'layers' } as NavItem] : []),
  ]
  return <AppShell subtitle="Teacher" sectionLabel="Teaching" links={links} signOutLabel="Sign out"><Outlet /></AppShell>
}

export function TeacherHomePage() {
  const qc = useQueryClient()
  const rooms = useQuery({ queryKey: ['teacher', 'classrooms'], queryFn: () => api.get<Classroom[]>('/teacher/classrooms') })
  const [creating, setCreating] = useState(false)
  return (
    <div className="stack-lg">
      <header className="page-head">
        <div><h1>My classrooms</h1><p className="muted">Share a classroom's code with your students. You'll see their lessons, quiz scores and the chapters they find hardest.</p></div>
        <button className="btn btn-primary" onClick={() => setCreating(true)}><Icon name="plus" size={18} /> New classroom</button>
      </header>
      {creating && <CreateClassroom onDone={() => { setCreating(false); void qc.invalidateQueries({ queryKey: ['teacher'] }) }} />}
      <TeacherTimetable />
      {rooms.isError && <div className="alert" role="alert">{errorMessage(rooms.error)}</div>}
      {rooms.data?.length === 0 && !creating && <div className="card"><Empty icon="layers" text="No classrooms yet. Create one for each class you teach." /></div>}
      <div className="grid">
        {rooms.data?.map(r => (
          <Link key={r.id} to={`/teacher/classrooms/${r.id}`} className="card card-link">
            <div className="row-between"><span className="icon-tile lg tint-teal"><Icon name="grad" /></span><span className="badge">{r.studentCount} students</span></div>
            <h3 style={{ margin: 'var(--space-3) 0 2px', fontSize: 'var(--text-lg)' }}>{r.name}</h3>
            <span className="small muted">{r.boardCode} · {r.className} · {r.academicYear}{r.schoolName ? ` · ${r.schoolName}` : ''}</span>
            {r.isSchoolClass ? <div className="code-pill">School class</div> : <div className="code-pill">Code <strong>{r.joinCode}</strong></div>}
          </Link>
        ))}
      </div>
    </div>
  )
}

function CreateClassroom({ onDone }: { onDone: () => void }) {
  const [name, setName] = useState('')
  const [school, setSchool] = useState('')
  const [boardId, setBoardId] = useState('')
  const [curriculumId, setCurriculumId] = useState('')
  const [classId, setClassId] = useState('')
  const boards = useQuery({ queryKey: ['boards'], queryFn: () => api.get<Board[]>('/curriculum/boards') })
  const curricula = useQuery({ queryKey: ['curricula', boardId], queryFn: () => api.get<CurriculumSummary[]>(`/curriculum/boards/${boardId}/curricula`), enabled: !!boardId })
  const classes = useQuery({ queryKey: ['classes', curriculumId], queryFn: () => api.get<AcademicClass[]>(`/curriculum/curricula/${curriculumId}/classes`), enabled: !!curriculumId })
  const create = useMutation({ mutationFn: () => api.post('/teacher/classrooms', { name, classId, schoolName: school || null }), onSuccess: onDone })
  function submit(e: FormEvent) { e.preventDefault(); create.mutate() }
  return (
    <form className="card stack" onSubmit={submit}>
      <div className="row-between"><h2 style={{ margin: 0 }}>New classroom</h2><button type="button" className="btn btn-ghost btn-sm" onClick={onDone}>Close</button></div>
      <div className="inline-form" style={{ marginTop: 0 }}>
        <input className="input" aria-label="Classroom name" placeholder="Name, e.g. 8B Biology" value={name} onChange={e => setName(e.target.value)} />
        <input className="input" aria-label="School" placeholder="School (optional)" value={school} onChange={e => setSchool(e.target.value)} />
      </div>
      <div className="inline-form" style={{ marginTop: 0 }}>
        <select className="input" aria-label="Board" value={boardId} onChange={e => { setBoardId(e.target.value); setCurriculumId(''); setClassId('') }}>
          <option value="">Board…</option>{boards.data?.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}
        </select>
        <select className="input" aria-label="Academic year" value={curriculumId} disabled={!boardId} onChange={e => { setCurriculumId(e.target.value); setClassId('') }}>
          <option value="">Academic year…</option>{curricula.data?.map(c => <option key={c.id} value={c.id}>{c.name} ({c.academicYear})</option>)}
        </select>
        <select className="input" aria-label="Class" value={classId} disabled={!curriculumId} onChange={e => setClassId(e.target.value)}>
          <option value="">Class…</option>{classes.data?.map(k => <option key={k.id} value={k.id}>{k.gradeName}</option>)}
        </select>
      </div>
      {create.isError && <div className="alert" role="alert">{errorMessage(create.error)}</div>}
      <div><button className="btn btn-primary" disabled={!name.trim() || !classId || create.isPending}>{create.isPending ? 'Creating…' : 'Create classroom'}</button></div>
    </form>
  )
}

export function ClassroomPage() {
  const { id } = useParams()
  const qc = useQueryClient()
  const key = ['teacher', 'classroom', id]
  const detail = useQuery({ queryKey: key, queryFn: () => api.get<ClassroomDetail>(`/teacher/classrooms/${id}`), enabled: !!id })
  const refresh = () => qc.invalidateQueries({ queryKey: key })
  const rotate = useMutation({ mutationFn: () => api.post(`/teacher/classrooms/${id}/rotate-code`), onSuccess: refresh })
  const joining = useMutation({ mutationFn: (open: boolean) => api.put(`/teacher/classrooms/${id}/joining`, { open }), onSuccess: refresh })
  const remove = useMutation({ mutationFn: (studentId: string) => api.del(`/teacher/classrooms/${id}/students/${studentId}`), onSuccess: refresh })
  const [tab, setTab] = useState<ClassroomTab>('overview')

  if (detail.isLoading) return <div className="skeleton" style={{ height: 240 }} />
  if (detail.isError || !detail.data) return <div className="alert" role="alert">{errorMessage(detail.error)}</div>
  const { classroom: r, students, chapters, totalLessons } = detail.data
  const weakest = chapters.filter(c => c.quizAverage !== null).sort((a, b) => (a.quizAverage ?? 0) - (b.quizAverage ?? 0)).slice(0, 3)

  return (
    <div className="stack-lg">
      <header className="page-head">
        <div>
          <div className="crumbs"><Link to="/teacher">My classrooms</Link><Icon name="chevron" size={14} /><span>{r.name}</span></div>
          <h1>{r.name}</h1>
          <p className="muted">{r.boardCode} · {r.className} · {r.academicYear} · {students.length} students</p>
        </div>
        {r.isSchoolClass ? (
          <div className="card" style={{ padding: 'var(--space-3) var(--space-4)', maxWidth: 320 }}>
            <span className="badge">School class</span>
            <p className="small muted" style={{ margin: '6px 0 0' }}>Students join through their division's code from the school admin.</p>
          </div>
        ) : (
        <div className="card row" style={{ padding: 'var(--space-3) var(--space-4)' }}>
          <span className="small muted">Join code</span><strong className="join-code">{r.joinCode}</strong>
          <button className="btn btn-ghost btn-sm" onClick={() => rotate.mutate()} disabled={rotate.isPending}>New code</button>
          <label className="check" style={{ alignItems: 'center' }}><input type="checkbox" checked={r.joiningOpen} onChange={e => joining.mutate(e.target.checked)} /> Open</label>
        </div>
        )}
      </header>

      <div role="tablist" aria-label="Classroom sections" className="segmented" style={{ alignSelf: 'flex-start', flexWrap: 'wrap' }}>
        {CLASSROOM_TABS.map(([k, label]) => (
          <button key={k} role="tab" type="button" aria-selected={tab === k} aria-pressed={tab === k} onClick={() => setTab(k)}>{label}</button>
        ))}
      </div>

      {tab === 'tests' && <ClassTests classroomId={r.id} chapters={chapters} />}
      {tab === 'marking' && <Marking classroomId={r.id} />}
      {tab === 'heatmap' && <ClassHeatmap classroomId={r.id} />}
      {tab === 'homework' && <ClassHomework classroomId={r.id} chapters={chapters} />}

      {tab === 'overview' && <>
      {weakest.length > 0 && (
        <section className="card">
          <h2>Needs attention</h2>
          <div className="grid-stats">
            {weakest.map(c => (
              <div key={c.chapterId} className="list-item stat">
                <span className="icon-tile tint-rose"><Icon name="bolt" /></span>
                <div><strong>{c.subject}: {c.title}</strong><div className="stat-label">Quiz average {c.quizAverage}% · {c.quizAttempts} attempts</div></div>
              </div>
            ))}
          </div>
        </section>
      )}

      <section className="card" style={{ overflowX: 'auto' }}>
        <h2>Students</h2>
        {students.length === 0 ? <p className="muted small">No students yet. Ask them to open Family → Classrooms in Googols and enter code {r.joinCode}.</p> : (
          <table className="table">
            <thead><tr><th>Student</th><th>Lessons</th><th>Quizzes</th><th>Quiz avg</th><th>Streak</th><th>Last active</th><th>Level</th><th /></tr></thead>
            <tbody>
              {students.map(s => (
                <tr key={s.studentId}>
                  <td><strong>{s.name}</strong></td>
                  <td>{s.lessonsCompleted}/{totalLessons}<div className="bar" style={{ width: 80 }}><span style={{ width: `${totalLessons ? (s.lessonsCompleted / totalLessons) * 100 : 0}%` }} /></div></td>
                  <td>{s.quizzesTaken}</td>
                  <td>{s.quizAverage === null ? '—' : `${s.quizAverage}%`}</td>
                  <td>{s.streakDays} d</td>
                  <td>{s.lastActiveDate ? new Date(s.lastActiveDate).toLocaleDateString() : '—'}</td>
                  <td>{s.level}</td>
                  <td><button className="btn btn-ghost btn-sm" onClick={() => remove.mutate(s.studentId)} aria-label={`Remove ${s.name}`}>Remove</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="card" style={{ overflowX: 'auto' }}>
        <h2>Chapters</h2>
        <table className="table">
          <thead><tr><th>Chapter</th><th>Lessons</th><th>Lessons completed</th><th>Quiz avg</th><th>Attempts</th></tr></thead>
          <tbody>
            {chapters.map(c => (
              <tr key={c.chapterId}>
                <td>{c.subject} · {c.sequence}. {c.title}</td>
                <td>{c.lessonCount}</td>
                <td>{c.lessonCount ? `${c.completionRate}%` : '—'}</td>
                <td>{c.quizAverage === null ? '—' : `${c.quizAverage}%`}</td>
                <td>{c.quizAttempts}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
      </>}
    </div>
  )
}

type ClassroomTab = 'overview' | 'heatmap' | 'homework' | 'tests' | 'marking'
const CLASSROOM_TABS: [ClassroomTab, string][] = [['overview', 'Overview'], ['heatmap', 'Heat map'], ['homework', 'Homework'], ['tests', 'Tests'], ['marking', 'Marking']]
