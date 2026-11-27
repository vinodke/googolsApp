import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { Icon, tintFor } from '../../app/Icons'
import { api } from '../../core/api/client'
import type { Chapter } from '../../core/api/types'
import { strings } from '../../core/i18n/strings'
import { useStudentMe, useSubjects } from '../../core/queries'
import { Empty } from '../student/DashboardPage'

export function SubjectsPage() {
  const me = useStudentMe()
  const subjects = useSubjects(me.data?.enrollment?.classId)
  const t = strings[me.data?.preferredLanguage ?? 'en']
  const e = me.data?.enrollment
  return (
    <div>
      <header className="page-head">
        <div>
          <div className="crumbs"><Link to="/">{t.navHome}</Link><Icon name="chevron" size={14} /><span>{t.navSubjects}</span></div>
          <h1>{t.mySubjects}</h1>
          {e && <p className="muted">{e.curriculumName} · {e.className} · {e.academicYear}</p>}
        </div>
      </header>
      {subjects.isLoading && <div className="grid">{[0, 1, 2].map(i => <div key={i} className="skeleton" style={{ height: 150 }} />)}</div>}
      {subjects.data?.length === 0 && <div className="card"><Empty icon="book" text={t.noSubjects} /></div>}
      <div className="grid">
        {subjects.data?.map(s => (
          <Link key={s.id} to={`/subjects/${s.id}`} className="card card-link">
            <div className="row-between">
              <span className={`icon-tile lg ${tintFor(s.code)}`}><Icon name="book" /></span>
              <span className="badge">{s.mediumLanguage === 'ml' ? 'മലയാളം' : 'English'}</span>
            </div>
            <h3 style={{ margin: 'var(--space-4) 0 var(--space-1)', fontSize: 'var(--text-lg)' }}>{s.name}</h3>
            <div className="row-between">
              <span className="muted small">{s.code}</span>
              <Icon name="chevron" size={18} />
            </div>
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
  const t = strings[me.data?.preferredLanguage ?? 'en']
  const chapters = useQuery({
    queryKey: ['chapters', subjectId],
    queryFn: () => api.get<Chapter[]>(`/curriculum/subjects/${subjectId}/chapters`),
    enabled: !!subjectId,
  })
  return (
    <div>
      <header className="page-head">
        <div className="row" style={{ alignItems: 'center' }}>
          <span className={`icon-tile lg ${tintFor(subject?.code ?? '')}`}><Icon name="book" /></span>
          <div>
            <div className="crumbs"><Link to="/subjects">{t.navSubjects}</Link><Icon name="chevron" size={14} /><span>{subject?.name ?? '…'}</span></div>
            <h1>{subject?.name ?? 'Chapters'}</h1>
          </div>
        </div>
        <Link className="btn btn-primary" to={`/ai-teacher?subject=${subjectId}`}><Icon name="sparkles" size={18} /> {t.askTeacher}</Link>
      </header>
      {chapters.isLoading && <div className="skeleton" style={{ height: 160 }} />}
      {chapters.isError && <div className="alert" role="alert">Could not load chapters.</div>}
      {chapters.data?.length === 0 && <div className="card"><Empty icon="layers" text="Chapters for this subject haven't been published yet." /></div>}
      <ol className="list" style={{ gap: 'var(--space-3)' }}>
        {chapters.data?.map(c => (
          <li key={c.id} className="card row-between">
            <div className="row">
              <span className="icon-tile tint-indigo" style={{ fontWeight: 800 }}>{c.sequence}</span>
              <strong>{c.title}</strong>
            </div>
            <Link className="btn btn-chip" to={`/ai-teacher?subject=${subjectId}&chapter=${c.id}`}><Icon name="sparkles" size={16} /> Ask AI teacher</Link>
          </li>
        ))}
      </ol>
    </div>
  )
}
