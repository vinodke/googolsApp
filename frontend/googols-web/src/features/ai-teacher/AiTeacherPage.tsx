import { useMutation } from '@tanstack/react-query'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { Icon } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { AskTeacherResponse, ChatTurn, Lang, SourceRef, TeacherMode } from '../../core/api/types'
import { strings } from '../../core/i18n/strings'
import { useStudentMe, useSubjects } from '../../core/queries'
import './ai-teacher.css'

interface Message extends ChatTurn { sources?: SourceRef[]; grounded?: boolean }

export function AiTeacherPage() {
  const me = useStudentMe()
  const subjects = useSubjects(me.data?.enrollment?.classId)
  const [params] = useSearchParams()
  const [chosenSubject, setSubjectId] = useState(params.get('subject') ?? '')
  const subjectId = chosenSubject || subjects.data?.[0]?.id || ''
  const chapterId = params.get('chapter')
  // Follow the student's preferred language until they pick one here (the profile may still be loading on first render).
  const [chosenLang, setLang] = useState<Lang | null>(null)
  const lang: Lang = chosenLang ?? me.data?.preferredLanguage ?? 'en'
  const [mode, setMode] = useState<TeacherMode>('PersonalTeacher')
  const [input, setInput] = useState('')
  const [messages, setMessages] = useState<Message[]>([])
  const [remaining, setRemaining] = useState<number | null>(null)
  const endRef = useRef<HTMLDivElement>(null)
  const t = strings[lang]

  useEffect(() => { endRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [messages])

  const ask = useMutation({
    mutationFn: (text: string) => api.post<AskTeacherResponse>('/ai/messages', {
      subjectId, chapterId, topicId: null, message: text, mode, language: lang,
      history: messages.slice(-10).map(({ role, text }) => ({ role, text })),
    }),
    onSuccess: res => {
      setMessages(m => [...m, { role: 'assistant', text: res.reply, sources: res.sources, grounded: res.groundedInApprovedContent }])
      setRemaining(res.remainingToday)
    },
  })

  function send(text: string, nextMode: TeacherMode = mode) {
    const trimmed = text.trim()
    if (!trimmed || !subjectId || ask.isPending) return
    setMode(nextMode)
    setMessages(m => [...m, { role: 'user', text: trimmed }])
    setInput('')
    ask.mutate(trimmed)
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    send(input)
  }

  if (me.data?.consentRequired) return (
    <div className="card empty">
      <span className="icon-tile lg tint-amber"><Icon name="shield" /></span>
      <p>{t.consentNeeded}</p>
      <Link to="/family" className="btn btn-primary"><Icon name="shield" size={18} /> {t.inviteParent}</Link>
    </div>
  )

  return (
    <div className="teacher card">
      <div className="teacher-bar">
        <div className="row" style={{ flex: 1, minWidth: 220 }}>
          <span className="teacher-avatar" aria-hidden><Icon name="sparkles" size={22} /></span>
          <div style={{ lineHeight: 1.2 }}>
            <strong>{t.navAiTeacher}</strong>
            <div className="muted small"><span className="online-dot" aria-hidden /> {remaining !== null ? t.remaining(remaining) : 'Online'}</div>
          </div>
        </div>
        <select className="input" aria-label="Subject" value={subjectId} onChange={e => setSubjectId(e.target.value)}>
          {subjects.data?.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
        </select>
        <div role="group" aria-label="Language" className="segmented">
          {(['ml', 'en'] as const).map(l => (
            <button key={l} type="button" aria-pressed={lang === l} onClick={() => setLang(l)}>
              {l === 'ml' ? 'മലയാളം' : 'English'}
            </button>
          ))}
        </div>
      </div>

      <div className="chat" aria-live="polite">
        {messages.length === 0 && (
          <div className="empty">
            <span className="teacher-avatar lg" aria-hidden><Icon name="sparkles" size={30} /></span>
            <h2 style={{ marginTop: 'var(--space-4)' }}>{t.askTeacher}</h2>
            <p className="muted small">{t.typeQuestion}</p>
          </div>
        )}
        {messages.map((m, i) => (
          <div key={i} className={`bubble ${m.role}`}>
            <div className="bubble-text">{m.text}</div>
            {m.role === 'assistant' && (
              <div className="bubble-meta">
                {m.grounded
                  ? <span className="badge badge-success"><Icon name="book" size={14} /> {t.sourcesUsed}: {m.sources?.map(s => s.title).join(', ')}</span>
                  : <span className="badge badge-warning">{t.generalExplanation}</span>}
              </div>
            )}
          </div>
        ))}
        {ask.isPending && <div className="bubble assistant"><div className="typing" aria-label="Teacher is typing"><span /><span /><span /></div></div>}
        {ask.isError && <div className="alert" role="alert">{errorMessage(ask.error)}</div>}
        <div ref={endRef} />
      </div>

      <div className="quick">
        <button type="button" className="btn btn-chip" disabled={ask.isPending || messages.length === 0} onClick={() => send(t.explainSimply, 'PersonalTeacher')}>{t.explainSimply}</button>
        <button type="button" className="btn btn-chip" disabled={ask.isPending || messages.length === 0} onClick={() => send(t.giveExample, 'PersonalTeacher')}>{t.giveExample}</button>
        <button type="button" className="btn btn-chip" disabled={ask.isPending || messages.length === 0} onClick={() => send(t.testMe, 'PracticeTeacher')}>{t.testMe}</button>
      </div>

      <form className="composer" onSubmit={submit}>
        <label htmlFor="q" className="sr-only">{t.typeQuestion}</label>
        <textarea id="q" className="input" rows={2} maxLength={4000} placeholder={t.typeQuestion} value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(input) } }} />
        <button className="btn btn-primary" disabled={!input.trim() || !subjectId || ask.isPending}><Icon name="send" size={18} /> {t.send}</button>
      </form>
    </div>
  )
}
