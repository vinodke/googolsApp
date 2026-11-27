import { Link } from 'react-router'
import { Icon, tintFor } from '../../app/Icons'
import { strings } from '../../core/i18n/strings'
import { useLearningSummary, useStudentMe, useSubjects } from '../../core/queries'
import { Empty } from '../student/DashboardPage'

export function SubjectsPage() {
  const me = useStudentMe()
  const subjects = useSubjects(me.data?.enrollment?.classId)
  const summary = useLearningSummary(!!me.data)
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
            <SubjectBar p={summary.data?.subjects.find(x => x.subjectId === s.id)} />
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

export function SubjectBar({ p }: { p?: { totalLessons: number; completedLessons: number } }) {
  if (!p || p.totalLessons === 0) return null
  const v = Math.round((p.completedLessons / p.totalLessons) * 100)
  return (
    <div style={{ margin: 'var(--space-2) 0' }}>
      <div className="row-between small muted"><span>{p.completedLessons}/{p.totalLessons} lessons</span><span>{v}%</span></div>
      <div className="bar"><span style={{ width: `${v}%` }} /></div>
    </div>
  )
}
