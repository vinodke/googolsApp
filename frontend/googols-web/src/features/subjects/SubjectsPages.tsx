import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { api } from '../../core/api/client'
import type { Chapter } from '../../core/api/types'
import { strings } from '../../core/i18n/strings'
import { useStudentMe, useSubjects } from '../../core/queries'

export function SubjectsPage() {
  const me = useStudentMe()
  const subjects = useSubjects(me.data?.enrollment?.classId)
  const t = strings[me.data?.preferredLanguage ?? 'en']
  return (
    <div>
      <h1>{t.mySubjects}</h1>
      {subjects.isLoading && <div className="skeleton" style={{ height: 96 }} />}
      {subjects.data?.length === 0 && <div className="card empty">{t.noSubjects}</div>}
      <div className="grid">
        {subjects.data?.map(s => (
          <Link key={s.id} to={`/subjects/${s.id}`} className="card" style={{ textDecoration: 'none', color: 'inherit' }}>
            <h3>{s.name}</h3>
            <span className="muted">{s.code}</span>
          </Link>
        ))}
      </div>
    </div>
  )
}

export function SubjectChaptersPage() {
  const { subjectId } = useParams()
  const me = useStudentMe()
  const subjects = useSubjects(me.data?.enrollment?.classId)
  const subject = subjects.data?.find(s => s.id === subjectId)
  const chapters = useQuery({
    queryKey: ['chapters', subjectId],
    queryFn: () => api.get<Chapter[]>(`/curriculum/subjects/${subjectId}/chapters`),
    enabled: !!subjectId,
  })
  return (
    <div>
      <p><Link to="/subjects">← Subjects</Link></p>
      <h1>{subject?.name ?? 'Chapters'}</h1>
      {chapters.isLoading && <div className="skeleton" style={{ height: 160 }} />}
      {chapters.isError && <div className="alert" role="alert">Could not load chapters.</div>}
      {chapters.data?.length === 0 && <div className="card empty">Chapters for this subject haven't been published yet.</div>}
      <ol style={{ display: 'grid', gap: 'var(--space-3)', padding: 0, listStyle: 'none' }}>
        {chapters.data?.map(c => (
          <li key={c.id} className="card" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 'var(--space-3)' }}>
            <span><span className="muted">{c.sequence}.</span> {c.title}</span>
            <Link className="btn btn-chip" to={`/ai-teacher?subject=${subjectId}&chapter=${c.id}`}>✨ Ask AI teacher</Link>
          </li>
        ))}
      </ol>
    </div>
  )
}
