import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate, Outlet, useParams } from 'react-router'
import { AppShell, PageLoading, type NavItem } from '../../app/AppShell'
import { Icon } from '../../app/Icons'
import { api, errorMessage, fieldErrors } from '../../core/api/client'
import type { Division, DivisionDetail, Heatmap, Person, SchoolOverview, Slot } from '../../core/api/types'
import { isAdmin, isSchoolAdmin, useAccount } from '../../core/queries'
import { HeatmapTable } from '../../shared/HeatmapTable'
import { DAYS } from '../../shared/TimetableGrid'
import { Empty } from '../student/DashboardPage'
import '../tests/tests.css'
import '../admin/admin.css'

export function SchoolLayout() {
  const account = useAccount()
  const school = useQuery({ queryKey: ['school'], queryFn: () => api.get<SchoolOverview>('/school'), enabled: isSchoolAdmin(account.data) })
  if (account.isLoading) return <PageLoading />
  if (!isSchoolAdmin(account.data)) return <Navigate to="/" replace />
  const links: NavItem[] = [
    { to: '/school', label: 'Divisions', icon: 'school', end: true },
    { to: '/school/staff', label: 'Staff', icon: 'users' },
    { to: '/teacher', label: 'My classes', icon: 'grad' },
    ...(isAdmin(account.data) ? [{ to: '/admin', label: 'Admin', icon: 'layers' } as NavItem] : []),
  ]
  return (
    <AppShell subtitle={school.data?.name ?? 'School admin'} sectionLabel="School" links={links} signOutLabel="Sign out">
      {school.isError ? <div className="alert" role="alert">{errorMessage(school.error)}</div> : <Outlet />}
    </AppShell>
  )
}

export function SchoolHomePage() {
  const qc = useQueryClient()
  const school = useQuery({ queryKey: ['school'], queryFn: () => api.get<SchoolOverview>('/school') })
  const divisions = useQuery({ queryKey: ['school', 'divisions'], queryFn: () => api.get<Division[]>('/school/divisions') })
  const [classId, setClassId] = useState('')
  const [name, setName] = useState('')
  const create = useMutation({
    mutationFn: () => api.post('/school/divisions', { classId, name }),
    onSuccess: () => { setName(''); void qc.invalidateQueries({ queryKey: ['school'] }) },
  })
  const errors = fieldErrors(create.error)
  if (!school.data) return school.isError ? <div className="alert" role="alert">{errorMessage(school.error)}</div> : <PageLoading />
  const s = school.data
  function submit(e: FormEvent) { e.preventDefault(); create.mutate() }
  return (
    <div className="stack-lg">
      <header className="page-head">
        <div><h1>{s.name}</h1><p className="muted">{s.code}{s.city ? ` · ${s.city}` : ''} · {s.divisions} divisions · {s.students} students · {s.teachers} staff</p></div>
      </header>
      <form className="card stack" onSubmit={submit} aria-label="New division">
        <div className="card-title"><h2>Add a division</h2></div>
        <div className="inline-form" style={{ marginTop: 0 }}>
          <select className="input" aria-label="Class" value={classId} onChange={e => setClassId(e.target.value)}>
            <option value="">Class…</option>
            {s.classes.map(c => <option key={c.id} value={c.id}>{c.label}</option>)}
          </select>
          <input className="input narrow" aria-label="Division name" placeholder="e.g. 8A" maxLength={50} value={name} onChange={e => setName(e.target.value)} />
          <button className="btn btn-primary" disabled={!classId || !name.trim() || create.isPending}><Icon name="plus" size={16} /> Add</button>
        </div>
        {(errors.name || errors.classId) && <span className="field-error">{errors.name ?? errors.classId}</span>}
        <p className="small muted" style={{ margin: 0 }}>Each division gets a code. Students enter it in Googols (Family → Classrooms) to join; they then appear in every subject class of that division.</p>
      </form>
      {divisions.data?.length === 0 && <div className="card"><Empty icon="school" text="No divisions yet. Add one for each section, like 8A and 8B." /></div>}
      <div className="grid">
        {divisions.data?.map(d => (
          <Link key={d.id} to={`/school/divisions/${d.id}`} className="card card-link">
            <div className="row-between"><span className="icon-tile lg tint-indigo"><Icon name="users" /></span><span className="badge">{d.students} students</span></div>
            <h3 style={{ margin: 'var(--space-3) 0 2px' }}>{d.name}</h3>
            <span className="small muted">{d.classLabel} · {d.allocations} subject teachers</span>
            <div className="code-pill">Code <strong>{d.joinCode}</strong>{!d.joiningOpen && ' · closed'}</div>
          </Link>
        ))}
      </div>
    </div>
  )
}

export function SchoolStaffPage() {
  const qc = useQueryClient()
  const staff = useQuery({ queryKey: ['school', 'staff'], queryFn: () => api.get<Person[]>('/school/staff') })
  const [email, setEmail] = useState('')
  const add = useMutation({ mutationFn: () => api.post('/school/staff', { email }), onSuccess: () => { setEmail(''); void qc.invalidateQueries({ queryKey: ['school'] }) } })
  const remove = useMutation({ mutationFn: (userId: string) => api.del(`/school/staff/${userId}`), onSuccess: () => qc.invalidateQueries({ queryKey: ['school'] }) })
  const errors = fieldErrors(add.error)
  return (
    <div className="stack-lg">
      <header className="page-head"><div><h1>Staff</h1><p className="muted">Teachers sign up on Googols first; then add them here with their email. They can then be given subjects in divisions.</p></div></header>
      <form className="card inline-form" style={{ marginTop: 0 }} onSubmit={e => { e.preventDefault(); add.mutate() }}>
        <input className="input" type="email" aria-label="Teacher's email" placeholder="Teacher's Googols email" value={email} onChange={e => setEmail(e.target.value)} />
        <button className="btn btn-primary" disabled={!email.includes('@') || add.isPending}><Icon name="plus" size={16} /> Add teacher</button>
        {(errors.email || add.isError) && <span className="field-error">{errors.email ?? errorMessage(add.error)}</span>}
      </form>
      <section className="card">
        <table className="table">
          <thead><tr><th>Name</th><th>Email</th><th>Role</th><th /></tr></thead>
          <tbody>
            {staff.data?.map(p => (
              <tr key={p.userId}>
                <td>{p.name || '—'}</td><td>{p.email}</td><td><span className="badge">{p.role === 'SchoolAdmin' ? 'School admin' : 'Teacher'}</span></td>
                <td>{p.role === 'Teacher' && <button className="btn btn-ghost btn-sm" onClick={() => remove.mutate(p.userId)} aria-label={`Remove ${p.email}`}>Remove</button>}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {remove.isError && <div className="alert" role="alert">{errorMessage(remove.error)}</div>}
      </section>
    </div>
  )
}

type DivisionTab = 'students' | 'teachers' | 'timetable' | 'heatmap'

export function DivisionPage() {
  const { id } = useParams()
  const qc = useQueryClient()
  const key = ['school', 'division', id]
  const detail = useQuery({ queryKey: key, queryFn: () => api.get<DivisionDetail>(`/school/divisions/${id}`), enabled: !!id })
  const [tab, setTab] = useState<DivisionTab>('students')
  const refresh = () => qc.invalidateQueries({ queryKey: ['school'] })
  const rotate = useMutation({ mutationFn: () => api.post(`/school/divisions/${id}/rotate-code`), onSuccess: refresh })
  const update = useMutation({ mutationFn: (v: { name: string; joiningOpen: boolean }) => api.put(`/school/divisions/${id}`, v), onSuccess: refresh })
  const removeStudent = useMutation({ mutationFn: (studentId: string) => api.del(`/school/divisions/${id}/students/${studentId}`), onSuccess: refresh })
  if (!detail.data) return detail.isError ? <div className="alert" role="alert">{errorMessage(detail.error)}</div> : <PageLoading />
  const { division: d, students } = detail.data
  return (
    <div className="stack-lg">
      <header className="page-head">
        <div>
          <div className="crumbs"><Link to="/school">Divisions</Link><Icon name="chevron" size={14} /><span>{d.name}</span></div>
          <h1>{d.name}</h1><p className="muted">{d.classLabel} · {d.students} students</p>
        </div>
        <div className="card row" style={{ padding: 'var(--space-3) var(--space-4)' }}>
          <span className="small muted">Join code</span><strong className="join-code">{d.joinCode}</strong>
          <button className="btn btn-ghost btn-sm" onClick={() => rotate.mutate()} disabled={rotate.isPending}>New code</button>
          <label className="check" style={{ alignItems: 'center', flexBasis: 'auto' }}><input type="checkbox" checked={d.joiningOpen} onChange={e => update.mutate({ name: d.name, joiningOpen: e.target.checked })} /> Open</label>
        </div>
      </header>
      <div role="tablist" aria-label="Division sections" className="segmented" style={{ alignSelf: 'flex-start' }}>
        {([['students', 'Students'], ['teachers', 'Subject teachers'], ['timetable', 'Timetable'], ['heatmap', 'Heat map']] as [DivisionTab, string][]).map(([k, label]) => (
          <button key={k} role="tab" type="button" aria-selected={tab === k} aria-pressed={tab === k} onClick={() => setTab(k)}>{label}</button>
        ))}
      </div>
      {tab === 'students' && (
        <section className="card" style={{ overflowX: 'auto' }}>
          {students.length === 0 ? <p className="muted">No students yet. Share code <strong>{d.joinCode}</strong> with the class.</p> : (
            <table className="table">
              <thead><tr><th>Student</th><th>Lessons</th><th>Level</th><th>XP</th><th>Streak</th><th>Last active</th><th /></tr></thead>
              <tbody>
                {students.map(s => (
                  <tr key={s.studentId}>
                    <td><strong>{s.name}</strong></td><td>{s.lessonsCompleted}</td><td>{s.level}</td><td>{s.totalXp}</td><td>{s.streakDays} d</td>
                    <td>{s.lastActiveDate ? new Date(s.lastActiveDate).toLocaleDateString() : '—'}</td>
                    <td><button className="btn btn-ghost btn-sm" onClick={() => removeStudent.mutate(s.studentId)} aria-label={`Remove ${s.name}`}>Remove</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      )}
      {tab === 'teachers' && <Allocations detail={detail.data} onChanged={refresh} />}
      {tab === 'timetable' && <TimetableEditor detail={detail.data} />}
      {tab === 'heatmap' && <DivisionHeatmap divisionId={d.id} />}
    </div>
  )
}

function Allocations({ detail, onChanged }: { detail: DivisionDetail; onChanged: () => void }) {
  const id = detail.division.id
  const [subjectId, setSubjectId] = useState('')
  const [teacher, setTeacher] = useState('')
  const add = useMutation({ mutationFn: () => api.post(`/school/divisions/${id}/allocations`, { subjectId, teacherUserId: teacher }), onSuccess: () => { setSubjectId(''); setTeacher(''); onChanged() } })
  const remove = useMutation({ mutationFn: (allocationId: string) => api.del(`/school/divisions/${id}/allocations/${allocationId}`), onSuccess: onChanged })
  return (
    <section className="card stack">
      <p className="small muted" style={{ margin: 0 }}>Each subject teacher gets a class in their Teacher area with every student of this division, so they can set homework and tests and see the heat map.</p>
      <table className="table">
        <thead><tr><th>Subject</th><th>Teacher</th><th /></tr></thead>
        <tbody>
          {detail.subjects.map(s => {
            const a = detail.allocations.find(x => x.subjectId === s.id)
            return (
              <tr key={s.id}>
                <td>{s.name}</td><td>{a ? a.teacher : <span className="muted">Not assigned</span>}</td>
                <td>{a && <button className="btn btn-ghost btn-sm" onClick={() => remove.mutate(a.id)}>Unassign</button>}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
      <form className="inline-form" style={{ marginTop: 0 }} onSubmit={e => { e.preventDefault(); add.mutate() }} aria-label="Assign a teacher">
        <select className="input" aria-label="Subject" value={subjectId} onChange={e => setSubjectId(e.target.value)}>
          <option value="">Subject…</option>{detail.subjects.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
        </select>
        <select className="input" aria-label="Teacher" value={teacher} onChange={e => setTeacher(e.target.value)}>
          <option value="">Teacher…</option>{detail.teachers.map(t => <option key={t.userId} value={t.userId}>{t.name || t.email}</option>)}
        </select>
        <button className="btn btn-primary" disabled={!subjectId || !teacher || add.isPending}>Assign</button>
        {add.isError && <span className="field-error">{errorMessage(add.error)}</span>}
      </form>
      {detail.teachers.length === 0 && <p className="small muted">Add teachers on the Staff page first.</p>}
    </section>
  )
}

interface Cell { subjectId: string; label: string }

function TimetableEditor({ detail }: { detail: DivisionDetail }) {
  const id = detail.division.id
  const qc = useQueryClient()
  const slots = useQuery({ queryKey: ['school', 'timetable', id], queryFn: () => api.get<Slot[]>(`/school/divisions/${id}/timetable`) })
  const [periods, setPeriods] = useState(8)
  const [days, setDays] = useState(6)
  const [cells, setCells] = useState<Record<string, Cell>>({})
  const [times, setTimes] = useState<Record<number, { start: string; end: string }>>({})
  useEffect(() => {
    if (!slots.data) return
    setCells(Object.fromEntries(slots.data.map(s => [`${s.dayOfWeek}-${s.period}`, { subjectId: s.subjectId ?? '', label: s.subjectId ? '' : (s.label ?? '') }])))
    const t: Record<number, { start: string; end: string }> = {}
    for (const s of slots.data) if (s.startTime) t[s.period] = { start: s.startTime, end: s.endTime ?? '' }
    setTimes(t)
    if (slots.data.length) setPeriods(Math.max(8, ...slots.data.map(s => s.period)))
  }, [slots.data])
  const save = useMutation({
    mutationFn: () => {
      const body: Slot[] = []
      for (let d = 1; d <= days; d++) for (let p = 1; p <= periods; p++) {
        const c = cells[`${d}-${p}`]
        if (!c || (!c.subjectId && !c.label.trim())) continue
        body.push({ dayOfWeek: d, period: p, subjectId: c.subjectId || null, teacherUserId: null, label: c.subjectId ? null : c.label.trim(),
          startTime: times[p]?.start || null, endTime: times[p]?.end || null })
      }
      return api.put(`/school/divisions/${id}/timetable`, { slots: body })
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['school', 'timetable', id] }),
  })
  const set = (key: string, v: Cell) => setCells(c => ({ ...c, [key]: v }))
  return (
    <section className="card stack" aria-label="Timetable editor">
      <div className="inline-form" style={{ marginTop: 0 }}>
        <label className="row small">Periods a day <input className="input narrow" type="number" min={1} max={10} value={periods} onChange={e => setPeriods(Math.min(10, Math.max(1, Number(e.target.value))))} /></label>
        <label className="row small">Days <select className="input narrow" value={days} onChange={e => setDays(Number(e.target.value))}><option value={5}>Mon–Fri</option><option value={6}>Mon–Sat</option></select></label>
      </div>
      <div className="hm-wrap">
        <table className="table tt">
          <thead><tr><th>Period</th><th>Time</th>{DAYS.slice(0, days).map(d => <th key={d}>{d}</th>)}</tr></thead>
          <tbody>
            {Array.from({ length: periods }, (_, i) => i + 1).map(p => (
              <tr key={p}>
                <th>{p}</th>
                <td style={{ minWidth: 150 }}>
                  <input className="input" type="time" aria-label={`Period ${p} start`} value={times[p]?.start ?? ''} onChange={e => setTimes(t => ({ ...t, [p]: { start: e.target.value, end: t[p]?.end ?? '' } }))} />
                  <input className="input" type="time" aria-label={`Period ${p} end`} value={times[p]?.end ?? ''} onChange={e => setTimes(t => ({ ...t, [p]: { start: t[p]?.start ?? '', end: e.target.value } }))} />
                </td>
                {Array.from({ length: days }, (_, i) => i + 1).map(d => {
                  const key = `${d}-${p}`
                  const c = cells[key] ?? { subjectId: '', label: '' }
                  return (
                    <td key={d}>
                      <select className="input" aria-label={`${DAYS[d - 1]} period ${p}`} value={c.subjectId || (c.label ? `label:${c.label}` : '')}
                        onChange={e => set(key, e.target.value.startsWith('label:') ? { subjectId: '', label: e.target.value.slice(6) } : { subjectId: e.target.value, label: '' })}>
                        <option value="">—</option>
                        {detail.subjects.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
                        {['Break', 'Lunch', 'Games', 'Library', 'Assembly'].map(l => <option key={l} value={`label:${l}`}>{l}</option>)}
                        {c.label && !['Break', 'Lunch', 'Games', 'Library', 'Assembly'].includes(c.label) && <option value={`label:${c.label}`}>{c.label}</option>}
                      </select>
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {save.isError && <div className="alert" role="alert">{errorMessage(save.error)}</div>}
      {save.isSuccess && <p className="small text-success">Timetable saved. Students and teachers see it now.</p>}
      <div><button className="btn btn-primary" onClick={() => save.mutate()} disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save timetable'}</button></div>
      <p className="small muted" style={{ margin: 0 }}>The teacher for each period comes from the subject teachers you assigned.</p>
    </section>
  )
}

function DivisionHeatmap({ divisionId }: { divisionId: string }) {
  const hm = useQuery({ queryKey: ['school', 'heatmap', divisionId], queryFn: () => api.get<Heatmap>(`/school/divisions/${divisionId}/heatmap`) })
  return (
    <section className="card">
      {hm.isError && <div className="alert" role="alert">{errorMessage(hm.error)}</div>}
      {hm.data && <HeatmapTable data={hm.data} />}
    </section>
  )
}
