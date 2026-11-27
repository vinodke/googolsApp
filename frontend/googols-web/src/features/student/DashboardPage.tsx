import { Link } from 'react-router'
import { Icon, ProgressRing, tintFor } from '../../app/Icons'
import { xpForLevel } from '../../app/theme'
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
  const start = xpForLevel(me.data.level)
  const next = xpForLevel(me.data.level + 1)
  const progress = (me.data.totalXp - start) / (next - start)

  return (
    <div className="stack-lg">
      <section className="card card-hero">
        <div className="row-between" style={{ alignItems: 'flex-start' }}>
          <div>
            <span className="badge" style={{ background: 'rgb(255 255 255 / 0.2)', color: '#fff' }}>
              <Icon name="grad" size={14} /> {e ? `${e.boardCode} · ${e.className} · ${e.academicYear}` : 'Googols'}
            </span>
            <h1 style={{ marginTop: 'var(--space-3)', fontSize: 'var(--text-3xl)' }}>{t.welcome(me.data.displayName)}</h1>
            <p style={{ opacity: 0.9, maxWidth: '52ch' }}>{t.appTagline}</p>
            <div className="row" style={{ marginTop: 'var(--space-4)' }}>
              <Link to="/ai-teacher" className="btn btn-light"><Icon name="sparkles" size={18} /> {t.askTeacher}</Link>
              <Link to="/subjects" className="btn btn-glass">{t.continueLearning} <Icon name="chevron" size={18} /></Link>
            </div>
          </div>
        </div>
      </section>

      {me.data.consentRequired && <div className="alert alert-info" role="status">{t.consentNeeded}</div>}

      <section className="grid-stats" aria-label="Your stats">
        <Stat icon="trophy" tint="tint-indigo" value={`${t.level} ${me.data.level}`} label={`${next - me.data.totalXp} ${t.xp} → ${t.level} ${me.data.level + 1}`} />
        <Stat icon="star" tint="tint-amber" value={`${me.data.totalXp}`} label={`${t.xp}`} />
        <Stat icon="flame" tint="tint-rose" value={`${me.data.streakDays}`} label={t.streak(me.data.streakDays)} />
        <Stat icon="book" tint="tint-teal" value={`${subjects.data?.length ?? '–'}`} label={t.mySubjects} />
      </section>

      <div className="grid-2">
        <section className="card">
          <div className="card-title">
            <h2>{t.mySubjects}</h2>
            <Link to="/subjects" className="btn btn-chip">{t.navSubjects} <Icon name="chevron" size={16} /></Link>
          </div>
          {subjects.isLoading && <div className="grid">{[0, 1, 2].map(i => <div key={i} className="skeleton" style={{ height: 110 }} />)}</div>}
          {subjects.isError && <div className="alert" role="alert">Could not load subjects.</div>}
          {subjects.data?.length === 0 && <Empty icon="book" text={t.noSubjects} />}
          <div className="grid">
            {subjects.data?.map(s => (
              <Link key={s.id} to={`/subjects/${s.id}`} className="card card-link" style={{ background: 'var(--color-surface)' }}>
                <span className={`icon-tile ${tintFor(s.code)}`}><Icon name="book" /></span>
                <h3 style={{ margin: 'var(--space-3) 0 var(--space-1)' }}>{s.name}</h3>
                <span className="muted small">{s.code} · {s.mediumLanguage === 'ml' ? 'മലയാളം' : 'English'} medium</span>
              </Link>
            ))}
          </div>
        </section>

        <div className="stack-lg">
          <section className="card" style={{ textAlign: 'center' }}>
            <h2 style={{ textAlign: 'left' }}>{t.level} {me.data.level}</h2>
            <div style={{ display: 'grid', placeItems: 'center', margin: 'var(--space-3) 0' }}>
              <ProgressRing value={progress}>
                <strong>{Math.round(progress * 100)}%</strong>
                <span className="muted small">→ {t.level} {me.data.level + 1}</span>
              </ProgressRing>
            </div>
            <p className="muted small" style={{ margin: 0 }}>{me.data.totalXp} / {next} {t.xp}</p>
          </section>

          <section className="card">
            <h2>{t.recentXp}</h2>
            {xp.isLoading && <div className="skeleton" style={{ height: 48 }} />}
            {xp.data && xp.data.recent.length === 0 && <p className="muted small">{t.noXpYet}</p>}
            <ul className="list">
              {xp.data?.recent.map((r, i) => (
                <li key={i} className="list-item">
                  <span className="icon-tile tint-amber" style={{ width: 36, height: 36 }}><Icon name="bolt" size={18} /></span>
                  <span className="grow">{r.reason.replace(/-/g, ' ')}</span>
                  <strong>+{r.points} XP</strong>
                </li>
              ))}
            </ul>
          </section>
        </div>
      </div>
    </div>
  )
}

function Stat({ icon, tint, value, label }: { icon: 'trophy' | 'star' | 'flame' | 'book'; tint: string; value: string; label: string }) {
  return (
    <div className="card stat">
      <span className={`icon-tile lg ${tint}`}><Icon name={icon} /></span>
      <div><div className="stat-value">{value}</div><div className="stat-label">{label}</div></div>
    </div>
  )
}

export function Empty({ icon, text }: { icon: 'book' | 'sparkles' | 'layers'; text: string }) {
  return (
    <div className="empty">
      <span className="icon-tile lg tint-indigo"><Icon name={icon} /></span>
      <p style={{ margin: 0 }}>{text}</p>
    </div>
  )
}
