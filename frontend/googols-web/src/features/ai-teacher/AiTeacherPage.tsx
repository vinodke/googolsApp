import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { Icon } from '../../app/Icons'
import { api, errorMessage, streamEvents } from '../../core/api/client'
import type { AskTeacherResponse, Conversation, ConversationSummary, Lang, SourceRef, TeacherMode } from '../../core/api/types'
import { useBookmarkToggle } from '../../core/bookmarks'
import { strings } from '../../core/i18n/strings'
import { preparePhoto, type PreparedPhoto } from '../../core/media/image'
import { useDictation, useReadAloud } from '../../core/media/speech'
import { useStudentMe, useSubjects } from '../../core/queries'
import './ai-teacher.css'

interface Message {
  key: string
  id?: string                 // server id of an assistant turn (for feedback and read-aloud)
  role: 'user' | 'assistant'
  text: string
  sources?: SourceRef[]
  grounded?: boolean
  wellbeing?: boolean
  imageUrl?: string
  helpful?: boolean | null
  streaming?: boolean
}

let seq = 0
const nextKey = () => `m${++seq}`

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
  const [conversationId, setConversationId] = useState<string | null>(null)
  const [showHistory, setShowHistory] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [photo, setPhoto] = useState<PreparedPhoto | null>(null)
  const [autoRead, setAutoRead] = useState(false)
  const endRef = useRef<HTMLDivElement>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const abortRef = useRef<AbortController | null>(null)
  const qc = useQueryClient()
  const t = strings[lang]
  const reader = useReadAloud()

  const dictationBase = useRef('')
  const onHeard = useCallback((text: string) => setInput((dictationBase.current ? dictationBase.current + ' ' : '') + text), [])
  const dictation = useDictation(lang, onHeard)

  const history = useQuery({
    queryKey: ['ai', 'conversations', subjectId],
    queryFn: () => api.get<ConversationSummary[]>(`/ai/conversations?subjectId=${subjectId}`),
    enabled: !!subjectId && !me.data?.consentRequired,
  })

  async function openConversation(id: string) {
    const c = await api.get<Conversation>(`/ai/conversations/${id}`)
    setConversationId(c.id)
    setMessages(c.messages.map(m => ({
      key: nextKey(), id: m.id, role: m.role, text: m.text, sources: m.sources, grounded: m.grounded, helpful: m.helpful,
      imageUrl: m.hasImage ? `/api/v1/ai/messages/${m.id}/image` : undefined,
    })))
    setShowHistory(false)
  }
  function newChat() {
    abortRef.current?.abort()
    setConversationId(null)
    setMessages([])
    setShowHistory(false)
    setError(null)
  }

  useEffect(() => { endRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' }) }, [messages])
  useEffect(() => () => abortRef.current?.abort(), [])

  function patchLast(update: (m: Message) => Message) {
    setMessages(list => list.length === 0 ? list : [...list.slice(0, -1), update(list[list.length - 1])])
  }

  async function send(text: string, nextMode: TeacherMode = mode) {
    const trimmed = text.trim()
    if ((!trimmed && !photo) || !subjectId || busy) return
    if (dictation.listening) dictation.stop()
    reader.stop()
    setMode(nextMode)
    setError(null)
    const sentPhoto = photo
    setMessages(m => [...m,
      { key: nextKey(), role: 'user', text: trimmed || t.photoQuestion, imageUrl: sentPhoto?.dataUrl },
      { key: nextKey(), role: 'assistant', text: '', streaming: true }])
    setInput('')
    setPhoto(null)
    setBusy(true)

    const controller = new AbortController()
    abortRef.current = controller
    let streamError: string | null = null
    let final: AskTeacherResponse | null = null
    try {
      await streamEvents('/ai/messages/stream', {
        subjectId, chapterId, topicId: null, message: trimmed, mode: nextMode, language: lang, conversationId,
        history: conversationId ? [] : messages.slice(-10).filter(m => m.text).map(({ role, text }) => ({ role, text })),
        imageBase64: sentPhoto?.dataUrl ?? null,
      }, (event, data) => {
        if (event === 'delta') {
          const piece = (data as { text: string }).text
          patchLast(m => ({ ...m, text: m.text + piece }))
        } else if (event === 'done') {
          final = data as AskTeacherResponse
        } else if (event === 'error') {
          streamError = (data as { title?: string }).title ?? 'Something went wrong.'
        }
      }, controller.signal)
    } catch (e) {
      if (controller.signal.aborted) return
      streamError = errorMessage(e)
    } finally {
      setBusy(false)
    }

    const res = final as AskTeacherResponse | null
    if (streamError || !res) {
      setMessages(list => list.filter(m => !(m.streaming && !m.text)).map(m => (m.streaming ? { ...m, streaming: false } : m)))
      setError(streamError ?? 'Something went wrong.')
      return
    }
    patchLast(m => ({
      ...m, streaming: false, id: res.messageId ?? undefined, text: res.reply, sources: res.sources,
      grounded: res.groundedInApprovedContent, wellbeing: res.safetyNotice === 'wellbeing',
    }))
    setRemaining(res.remainingToday)
    if (res.conversationId) setConversationId(res.conversationId)
    void qc.invalidateQueries({ queryKey: ['ai', 'conversations'] })
    if (autoRead && res.messageId && res.safetyNotice !== 'wellbeing') void reader.speak(res.messageId, res.reply, lang)
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    void send(input)
  }

  async function pickPhoto(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    try {
      setPhoto(await preparePhoto(file))
      setError(null)
    } catch (err) {
      setError(errorMessage(err))
    }
  }

  function toggleMic() {
    if (dictation.listening) { dictation.stop(); return }
    dictationBase.current = input.trim()
    dictation.start()
  }

  if (me.data?.consentRequired) return (
    <div className="card empty">
      <span className="icon-tile lg tint-amber"><Icon name="shield" /></span>
      <p>{t.consentNeeded}</p>
      <Link to="/family" className="btn btn-primary"><Icon name="shield" size={18} /> {t.inviteParent}</Link>
    </div>
  )

  const voiceError = dictation.error ?? reader.error
  const canSend = (!!input.trim() || !!photo) && !!subjectId && !busy

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
        <label className="switch small" title="Read every answer aloud">
          <input type="checkbox" checked={autoRead} onChange={e => setAutoRead(e.target.checked)} /> <Icon name="volume" size={16} /> Auto
        </label>
        <button type="button" className="btn btn-ghost btn-sm" aria-expanded={showHistory} onClick={() => setShowHistory(v => !v)}>History</button>
        <button type="button" className="btn btn-ghost btn-sm" onClick={newChat}><Icon name="plus" size={16} /> New chat</button>
        <select className="input" aria-label="Subject" value={subjectId} onChange={e => { setSubjectId(e.target.value); newChat() }}>
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

      {showHistory && (
        <div className="history" role="region" aria-label="Previous chats">
          {history.data?.length === 0 && <p className="muted small" style={{ margin: 0 }}>No saved chats for this subject yet.</p>}
          {history.data?.map(c => (
            <button key={c.id} type="button" className={`history-item${c.id === conversationId ? ' active' : ''}`} onClick={() => void openConversation(c.id)}>
              <strong>{c.title}</strong><span className="muted small">{new Date(c.lastMessageAtUtc).toLocaleDateString()} · {c.messageCount / 2} questions</span>
            </button>
          ))}
        </div>
      )}

      <div className="chat" aria-live="polite" aria-busy={busy}>
        {messages.length === 0 && (
          <div className="empty">
            <span className="teacher-avatar lg" aria-hidden><Icon name="sparkles" size={30} /></span>
            <h2 style={{ marginTop: 'var(--space-4)' }}>{t.askTeacher}</h2>
            <p className="muted small">{t.typeQuestion}</p>
            <p className="muted small"><Icon name="mic" size={14} /> {t.speakQuestion} · <Icon name="camera" size={14} /> {t.addPhoto}</p>
          </div>
        )}
        {messages.map(m => (
          <div key={m.key} className={`bubble ${m.role}${m.wellbeing ? ' wellbeing' : ''}`}>
            {m.imageUrl && <img className="bubble-photo" src={m.imageUrl} alt={t.photoQuestion} />}
            {m.streaming && !m.text
              ? <div className="typing" aria-label="Teacher is typing"><span /><span /><span /></div>
              : <div className="bubble-text">{m.text}{m.streaming && <span className="caret" aria-hidden />}</div>}
            {m.role === 'assistant' && !m.wellbeing && !m.streaming && (
              <AnswerTools message={m} lang={lang} reader={reader} onRated={helpful => setMessages(list => list.map(x => (x.key === m.key ? { ...x, helpful } : x)))} />
            )}
          </div>
        ))}
        {error && <div className="alert" role="alert">{error}{/Premium/.test(error) && <> <Link to="/plans">See plans</Link></>}</div>}
        <div ref={endRef} />
      </div>

      <div className="quick">
        <button type="button" className="btn btn-chip" disabled={busy || messages.length === 0} onClick={() => void send(t.explainSimply, 'PersonalTeacher')}>{t.explainSimply}</button>
        <button type="button" className="btn btn-chip" disabled={busy || messages.length === 0} onClick={() => void send(t.giveExample, 'PersonalTeacher')}>{t.giveExample}</button>
        <button type="button" className="btn btn-chip" disabled={busy || messages.length === 0} onClick={() => void send(t.testMe, 'PracticeTeacher')}>{t.testMe}</button>
        {voiceError && <span className="small text-warning" role="status">{voiceError}</span>}
      </div>

      {photo && (
        <div className="photo-chip">
          <img src={photo.dataUrl} alt="" />
          <span className="small muted">{Math.round(photo.bytes / 1024)} KB · {t.photoQuestion}</span>
          <button type="button" className="icon-btn" aria-label={t.removePhoto} onClick={() => setPhoto(null)}><Icon name="x" size={16} /></button>
        </div>
      )}

      <form className="composer" onSubmit={submit}>
        <input ref={fileRef} type="file" accept="image/jpeg,image/png,image/*" capture="environment" hidden onChange={e => void pickPhoto(e)} data-testid="photo-input" />
        <button type="button" className="icon-btn" aria-label={t.addPhoto} title={t.addPhoto} onClick={() => fileRef.current?.click()} disabled={busy}>
          <Icon name="camera" size={20} />
        </button>
        <button type="button" className={`icon-btn${dictation.listening ? ' recording' : ''}`} aria-pressed={dictation.listening}
          aria-label={dictation.listening ? t.stopListening : t.speakQuestion} title={dictation.listening ? t.stopListening : t.speakQuestion}
          onClick={toggleMic} disabled={busy}>
          <Icon name={dictation.listening ? 'stop' : 'mic'} size={20} />
        </button>
        <label htmlFor="q" className="sr-only">{t.typeQuestion}</label>
        <textarea id="q" className="input" rows={2} maxLength={4000} placeholder={dictation.listening ? t.listening : t.typeQuestion} value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void send(input) } }} />
        <button className="btn btn-primary" disabled={!canSend}><Icon name="send" size={18} /> {t.send}</button>
      </form>
    </div>
  )
}

function AnswerTools({ message, lang, reader, onRated }: {
  message: Message
  lang: Lang
  reader: ReturnType<typeof useReadAloud>
  onRated: (helpful: boolean) => void
}) {
  const t = strings[lang]
  const [askWhy, setAskWhy] = useState(false)
  const [comment, setComment] = useState('')
  const [thanks, setThanks] = useState(false)
  const speaking = reader.speakingId === message.id
  const bookmark = useBookmarkToggle('answer', message.id)

  async function rate(helpful: boolean, why?: string) {
    await api.post(`/ai/messages/${message.id}/feedback`, { helpful, comment: why ?? null })
    onRated(helpful)
    setThanks(true)
    setAskWhy(false)
  }

  return (
    <div className="bubble-meta">
      {message.grounded
        ? <span className="badge badge-success"><Icon name="book" size={14} /> {t.sourcesUsed}: {message.sources?.map(s => s.title).join(', ')}</span>
        : <span className="badge badge-warning">{t.generalExplanation}</span>}
      {message.id && <div className="answer-tools">
        <button type="button" className="icon-btn sm" aria-label={speaking ? t.stopReading : t.readAloud} title={speaking ? t.stopReading : t.readAloud}
          onClick={() => (speaking ? reader.stop() : void reader.speak(message.id!, message.text, lang))}>
          <Icon name={speaking ? 'stop' : 'volume'} size={16} />
        </button>
        <button type="button" className="icon-btn sm" aria-pressed={message.helpful === true} aria-label={t.helpful} title={t.helpful}
          onClick={() => void rate(true)}><Icon name="thumbUp" size={16} /></button>
        <button type="button" className="icon-btn sm" aria-pressed={message.helpful === false} aria-label={t.notHelpful} title={t.notHelpful}
          onClick={() => { setAskWhy(true); setThanks(false) }}><Icon name="thumbDown" size={16} /></button>
        <button type="button" className="icon-btn sm" aria-pressed={bookmark.saved} aria-label={bookmark.saved ? t.saved : t.saveAnswer}
          title={bookmark.saved ? t.saved : t.saveAnswer} onClick={bookmark.toggle} disabled={bookmark.pending}><Icon name="bookmark" size={16} /></button>
        {thanks && <span className="small muted">{t.thanksFeedback}</span>}
      </div>}
      {askWhy && (
        <form className="why" onSubmit={e => { e.preventDefault(); void rate(false, comment.trim() || undefined) }}>
          <input className="input" maxLength={500} placeholder={t.whatWasWrong} aria-label={t.whatWasWrong} value={comment} onChange={e => setComment(e.target.value)} />
          <button className="btn btn-sm btn-primary">{t.sendFeedback}</button>
        </form>
      )}
    </div>
  )
}
