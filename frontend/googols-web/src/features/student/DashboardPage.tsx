import { Link } from 'react-router'
import { strings } from '../../core/i18n/strings'
import { useStudentMe, useSubjects, useXp } from '../../core/queries'

// Every number here comes from the API. Sections without data show an honest empty state.
export function DashboardPage() {
  const me = useStudentMe()
  const subjects = useSubjects(me.data?.enrollment?.classId)
  const xp = useXp()
  if (!me.data) return null
  const t = strings[me.data.preferredLanguage]
  const e = me.data.enrollment

  return (
    <div style={{ display: 'grid', gap: 'var(--space-5)' }}>
      <section className="card card-hero">
        <h1>{t.welcome(me.data.displayName)}</h1>
        {e && <p style={{ margin: 0, opacity: 0.9 }}>{e.boardCode} · {e.className} · {e.academicYear}</p>}
        <div style={{ marginTop: 'var(--space-4)', display: 'flex', gap: 'var(--space-2)', flexWrap: 'wrap' }}>
          <Link to="/ai-teacher" className="btn" style={{ background: '#fff', color: 'var(--g-indigo-700)' }}>✨ {t.askTeacher}</Link>
          <Link to="/subjects" className="btn" style={{ background: 'rgb(255 255 255 / 0.15)', color: '#fff' }}>{t.continueLearning}</Link>
        </div>
      </section>

      {me.data.consentRequired && (
        <div className="alert" role="status" style={{ background: 'var(--color-highlight)', color: 'var(--color-primary)' }}>{t.consentNeeded}</div>
      )}

      <section>
        <h2>{t.mySubjects}</h2>
        {subjects.isLoading && <div className="grid">{[0, 1, 2].map(i => <div key={i} className="skeleton" style={{ height: 96 }} />)}</div>}
        {subjects.isError && <div className="alert" role="alert">Could not load subjects.</div>}
        {subjects.data?.length === 0 && <div className="card empty">{t.noSubjects}</div>}
        <div className="grid">
          {subjects.data?.map(s => (
            <Link key={s.id} to={`/subjects/${s.id}`} className="card" style={{ textDecoration: 'none', color: 'inherit' }}>
              <h3>{s.name}</h3>
              <span className="badge">{s.mediumLanguage === 'ml' ? 'മലയാളം' : 'English'} medium</span>
            </Link>
          ))}
        </div>
      </section>

      <section className="card">
        <h2>{t.recentXp}</h2>
        {xp.isLoading && <div className="skeleton" style={{ height: 48 }} />}
        {xp.data && xp.data.recent.length === 0 && <p className="muted">{t.noXpYet}</p>}
        <ul style={{ listStyle: 'none', padding: 0, margin: 0, display: 'grid', gap: 'var(--space-2)' }}>
          {xp.data?.recent.map((r, i) => (
            <li key={i} style={{ display: 'flex', justifyContent: 'space-between' }}>
              <span>{r.reason.replace(/-/g, ' ')}</span><strong>+{r.points} XP</strong>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}
