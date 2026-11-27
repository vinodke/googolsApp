import { useQuery } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { Lang } from '../api/types'

// Voice for the AI teacher: the browser's own speech recognition for asking, and its voices (or the
// server's Azure Malayalam / English voice when set up) for reading answers aloud.

/* eslint-disable @typescript-eslint/no-explicit-any */
type Recognition = any

function recognitionCtor(): (new () => Recognition) | null {
  if (typeof window === 'undefined') return null
  const w = window as any
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null
}

export const dictationSupported = () => recognitionCtor() !== null

const dictationErrors: Record<string, string> = {
  'not-allowed': 'Allow the microphone for Googols to speak your question.',
  'service-not-allowed': 'Allow the microphone for Googols to speak your question.',
  'no-speech': "I didn't hear anything. Tap the mic and try again.",
  'audio-capture': 'No microphone was found on this device.',
  'language-not-supported': "This browser can't listen in this language. Please type instead.",
  network: 'Voice typing needs an internet connection.',
}

/** Speech-to-text into the question box. onText receives everything heard so far in this session. */
export function useDictation(lang: Lang, onText: (text: string) => void) {
  const rec = useRef<Recognition | null>(null)
  const [listening, setListening] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const stop = useCallback(() => rec.current?.stop(), [])
  const start = useCallback(() => {
    const Ctor = recognitionCtor()
    if (!Ctor) { setError('Voice typing works in Chrome and Edge. Please type instead.'); return }
    setError(null)
    const r = new Ctor()
    r.lang = lang === 'ml' ? 'ml-IN' : 'en-IN'
    r.interimResults = true
    r.continuous = false
    r.onresult = (e: any) => {
      let text = ''
      for (let i = 0; i < e.results.length; i++) text += e.results[i][0].transcript
      onText(text)
    }
    r.onerror = (e: any) => { if (e.error !== 'aborted') setError(dictationErrors[e.error] ?? 'Voice typing stopped. Please try again.') }
    r.onend = () => setListening(false)
    rec.current = r
    try { r.start(); setListening(true) } catch { setListening(false) }
  }, [lang, onText])

  useEffect(() => () => rec.current?.abort?.(), [])
  return { listening, error, start, stop, supported: dictationSupported() }
}

/** Removes markdown symbols so voices don't read them out. */
export const speakable = (text: string) => text.replace(/[*#_`>|~]+/g, ' ').replace(/[ \t]{2,}/g, ' ').trim()

function browserVoice(lang: Lang): SpeechSynthesisVoice | undefined {
  if (typeof window === 'undefined' || !('speechSynthesis' in window)) return undefined
  const voices = window.speechSynthesis.getVoices()
  if (lang === 'ml') return voices.find(v => v.lang.toLowerCase().startsWith('ml'))
  return voices.find(v => v.lang.toLowerCase() === 'en-in') ?? voices.find(v => v.lang.toLowerCase().startsWith('en'))
}

/** Read-aloud for answers. Malayalam prefers the server voice (most devices have none); English uses the device voice. */
export function useReadAloud() {
  const status = useQuery({
    queryKey: ['ai', 'speech-status'],
    queryFn: () => api.get<{ serverVoice: boolean }>('/ai/speech/status'),
    staleTime: Infinity,
  })
  const [speakingId, setSpeakingId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const audio = useRef<HTMLAudioElement | null>(null)

  const stop = useCallback(() => {
    if (typeof window !== 'undefined' && 'speechSynthesis' in window) window.speechSynthesis.cancel()
    if (audio.current) { audio.current.pause(); audio.current = null }
    setSpeakingId(null)
  }, [])

  const speak = useCallback(async (id: string, text: string, lang: Lang) => {
    stop()
    setError(null)
    const clean = speakable(text)
    if (!clean) return
    const voice = browserVoice(lang)
    const server = status.data?.serverVoice ?? false
    if (voice && !(lang === 'ml' && server)) {
      const u = new SpeechSynthesisUtterance(clean)
      u.voice = voice
      u.lang = voice.lang
      u.onend = () => setSpeakingId(s => (s === id ? null : s))
      u.onerror = () => setSpeakingId(null)
      window.speechSynthesis.speak(u)
      setSpeakingId(id)
      return
    }
    if (!server) {
      setError(lang === 'ml' ? "This device has no Malayalam voice yet. Read-aloud in Malayalam needs the server voice to be set up." : "This device can't read aloud.")
      return
    }
    setSpeakingId(id)
    try {
      const res = await fetch('/api/v1/ai/speech', {
        method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: clean, language: lang }),
      })
      if (!res.ok) throw new ApiError(res.status, await res.json().catch(() => ({})))
      const url = URL.createObjectURL(await res.blob())
      const a = new Audio(url)
      audio.current = a
      a.onended = () => { URL.revokeObjectURL(url); setSpeakingId(s => (s === id ? null : s)) }
      await a.play()
    } catch (e) {
      setSpeakingId(null)
      setError(e instanceof ApiError ? e.message : "Couldn't read this aloud. Please try again.")
    }
  }, [status.data, stop])

  useEffect(() => stop, [stop])
  return { speak, stop, speakingId, error }
}
