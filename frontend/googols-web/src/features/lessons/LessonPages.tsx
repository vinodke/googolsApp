import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { Icon, tintFor } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { ChapterProgress, CompleteLessonResult, QuizHistory, StudentChapter, StudentLesson } from '../../core/api/types'
import { strings } from '../../core/i18n/strings'
import { useStudentMe, useSubjects } from '../../core/queries'
import { Empty } from '../student/DashboardPage'
import { LessonBody } from './LessonBody'
import './lessons.css'

const pct = (done: number, total: number) => (total === 0 ? 0 : Math.round((done / total) * 100))

/** Subject page: chapters with lesson progress. */
export function SubjectChaptersPage() {
  const { subjectId } = useParams()
  const me = useStudentMe()
  const t = strings[me.data?.preferredLanguage ?? 'en']
  const subjects = useSubjects(me.data?.enrollment?.classId)
  const subject = subjects.data?.find(s => s.id === subjectId)
  const chapters = useQuery({
    queryKey: ['learning', 'subject', subjectId],
    queryFn: () => api.get<ChapterProgress[]>(`/learning/subjects/${subjectId}/chapters`),
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
          <li key={c.id}>
            <Link to={`/chapters/${c.id}`} className="card card-link chapter-row">
              <span className="icon-tile tint-indigo" style={{ fontWeight: 800 }}>{c.sequence}</span>
              <div className="grow">
                <strong>{c.title}</strong>
                <div className="row small muted" style={{ gap: 'var(--space-2)', marginTop: 4 }}>
                  <span>{c.totalLessons === 0 ? 'Lessons coming soon' : `${c.completedLessons} / ${c.totalLessons} lessons`}</span>
                </div>
                {c.totalLessons > 0 && <div className="bar" style={{ marginTop: 8 }}><span style={{ width: `${pct(c.completedLessons, c.totalLessons)}%` }} /></div>}
              </div>
              {c.totalLessons > 0 && c.completedLessons === c.totalLessons
                ? <span className="badge badge-success"><Icon name="check" size={14} /> Done</span>
                : <Icon name="chevron" />}
            </Link>
          </li>
        ))}
      </ol>
    </div>
  )
}

export function ChapterPage() {
  const { chapterId } = useParams()
  const me = useStudentMe()
  const t = strings[me.data?.preferredLanguage ?? 'en']
  const chapter = useQuery({
    queryKey: ['learning', 'chapter', chapterId],
    queryFn: () => api.get<StudentChapter>(`/learning/chapters/${chapterId}`),
    enabled: !!chapterId,
  })
  const quiz = useQuery({
    queryKey: ['learning', 'quiz-history', chapterId],
    queryFn: () => api.get<QuizHistory>(`/learning/chapters/${chapterId}/quiz-history`),
    enabled: !!chapterId,
  })
  const c = chapter.data
  const done = c?.lessons.filter(l => l.completed).length ?? 0
  const best = quiz.data?.attempts.reduce((m, a) => Math.max(m, a.percent), 0)
  return (
    <div>
      <header className="page-head">
        <div>
          <div className="crumbs">
            <Link to="/subjects">{t.navSubjects}</Link><Icon name="chevron" size={14} />
            {c ? <Link to={`/subjects/${c.subjectId}`}>{c.subjectName}</Link> : <span>…</span>}<Icon name="chevron" size={14} />
            <span>{c?.title ?? ''}</span>
          </div>
          <h1>{c ? `${c.sequence}. ${c.title}` : ' '}</h1>
          {c && c.lessons.length > 0 && <p className="muted">{done} of {c.lessons.length} lessons done</p>}
        </div>
        {c && <Link className="btn btn-primary" to={`/ai-teacher?subject=${c.subjectId}&chapter=${c.id}`}><Icon name="sparkles" size={18} /> {t.askTeacher}</Link>}
      </header>
      {chapter.isLoading && <div className="skeleton" style={{ height: 160 }} />}
      {chapter.isError && <div className="alert" role="alert">{errorMessage(chapter.error)}</div>}
      {c && c.lessons.length === 0 && <div className="card"><Empty icon="book" text="Lessons for this chapter are being prepared. You can already ask the AI teacher about it." /></div>}
      {quiz.data?.available && (
        <Link to={`/chapters/${chapterId}/quiz`} className="card card-link chapter-row" style={{ marginBottom: 'var(--space-4)' }}>
          <span className="icon-tile lg tint-amber"><Icon name="bolt" /></span>
          <div className="grow">
            <strong>Chapter quiz</strong>
            <div className="small muted">{quiz.data.questionCount} questions{quiz.data.attempts.length > 0 ? ` · best score ${Math.round(best ?? 0)}%` : ' · not tried yet'}</div>
          </div>
          <span className="btn btn-primary btn-sm">{quiz.data.attempts.length > 0 ? 'Try again' : 'Start'}</span>
        </Link>
      )}
      <ol className="list" style={{ gap: 'var(--space-3)' }}>
        {c?.lessons.map(l => (
          <li key={l.id}>
            <Link to={`/lessons/${l.id}`} className="card card-link chapter-row">
              <span className={`icon-tile ${l.completed ? 'tint-lime' : 'tint-sky'}`}><Icon name={l.completed ? 'check' : 'book'} /></span>
              <div className="grow">
                <strong>{l.title}</strong>
                <div className="small muted">{l.estimatedMinutes} min{l.hasAttachment ? ' · includes a file' : ''}</div>
              </div>
              {l.completed ? <span className="badge badge-success">Done</span> : <Icon name="chevron" />}
            </Link>
          </li>
        ))}
      </ol>
    </div>
  )
}

export function LessonPage() {
  const { lessonId } = useParams()
  const qc = useQueryClient()
  const me = useStudentMe()
  const t = strings[me.data?.preferredLanguage ?? 'en']
  const lesson = useQuery({
    queryKey: ['learning', 'lesson', lessonId],
    queryFn: () => api.get<StudentLesson>(`/learning/lessons/${lessonId}`),
    enabled: !!lessonId,
  })
  const complete = useMutation({
    mutationFn: () => api.post<CompleteLessonResult>(`/learning/lessons/${lessonId}/complete`),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['learning'] })
      void qc.invalidateQueries({ queryKey: ['student', 'me'] })
      void qc.invalidateQueries({ queryKey: ['xp'] })
    },
  })
  const l = lesson.data
  if (lesson.isLoading) return <div className="skeleton" style={{ height: 300 }} />
  if (lesson.isError || !l) return <div className="alert" role="alert">{errorMessage(lesson.error)}</div>
  const isDone = l.completed || complete.isSuccess

  return (
    <div className="lesson-layout">
      <article className="card lesson-card">
        <div className="crumbs">
          <Link to={`/subjects/${l.subjectId}`}>{l.subjectName}</Link><Icon name="chevron" size={14} />
          <Link to={`/chapters/${l.chapterId}`}>{l.chapterTitle}</Link>
        </div>
        <h1>{l.title}</h1>
        <div className="row small muted" style={{ marginBottom: 'var(--space-4)' }}>
          <span>{l.estimatedMinutes} min read</span>
          <span className={`badge ${l.kind === 'OfficialSyllabus' ? 'badge-success' : ''}`}>{l.kind === 'OfficialSyllabus' ? 'From the textbook' : 'Teacher notes'}</span>
        </div>
        <LessonBody text={l.body} />
        {l.attachmentFileName && (
          <a className="attachment" href={`/api/v1/learning/lessons/${l.id}/attachment`}>
            <span className="icon-tile tint-rose"><Icon name="book" /></span>
            <span className="grow"><strong>{l.attachmentFileName}</strong><span className="small muted" style={{ display: 'block' }}>{formatBytes(l.attachmentBytes)} · download</span></span>
          </a>
        )}
      </article>

      <aside className="stack lesson-side">
        <div className="card">
          {isDone ? (
            <div className="stack" style={{ textAlign: 'center' }}>
              <span className="icon-tile lg tint-lime" style={{ margin: '0 auto' }}><Icon name="check" /></span>
              <strong>Lesson complete</strong>
              {complete.data && complete.data.xpAwarded > 0 && <span className="badge badge-warning" style={{ justifySelf: 'center' }}><Icon name="star" size={14} /> +{complete.data.xpAwarded} XP</span>}
              {complete.data?.chapterCompleted && <span className="badge badge-success" style={{ justifySelf: 'center' }}><Icon name="trophy" size={14} /> Chapter finished!</span>}
              {complete.data?.newBadges?.length ? <Link to="/achievements" className="badge" style={{ justifySelf: 'center' }}><Icon name="trophy" size={14} /> New badge! See your rewards</Link> : null}
              {complete.data && complete.data.streakDays > 0 && <span className="small muted">{t.streak(complete.data.streakDays)}</span>}
            </div>
          ) : (
            <div className="stack">
              <strong>Finished reading?</strong>
              <button className="btn btn-primary" onClick={() => complete.mutate()} disabled={complete.isPending}>
                <Icon name="check" size={18} /> {complete.isPending ? 'Saving…' : 'Mark as complete'}
              </button>
              {complete.isError && <div className="alert" role="alert">{errorMessage(complete.error)}</div>}
            </div>
          )}
        </div>
        <Link className="card card-link row" to={`/ai-teacher?subject=${l.subjectId}&chapter=${l.chapterId}`}>
          <span className="teacher-avatar" aria-hidden style={{ width: 40, height: 40 }}><Icon name="sparkles" /></span>
          <span className="grow"><strong>{t.askTeacher}</strong><span className="small muted" style={{ display: 'block' }}>About {l.chapterTitle}</span></span>
        </Link>
        <div className="row-between">
          {l.previousLessonId ? <Link className="btn btn-ghost btn-sm" to={`/lessons/${l.previousLessonId}`}><Icon name="arrowLeft" size={16} /> Previous</Link> : <span />}
          {l.nextLessonId && <Link className="btn btn-primary btn-sm" to={`/lessons/${l.nextLessonId}`}>Next lesson <Icon name="chevron" size={16} /></Link>}
        </div>
      </aside>
    </div>
  )
}

function formatBytes(n: number | null) {
  if (!n) return ''
  return n > 1024 * 1024 ? `${(n / 1024 / 1024).toFixed(1)} MB` : `${Math.ceil(n / 1024)} KB`
}
