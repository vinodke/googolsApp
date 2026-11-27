import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { wrap } from '../../test-utils'
import { AiTeacherPage } from './AiTeacherPage'

afterEach(() => vi.restoreAllMocks())

const me = {
  id: 'st1', displayName: 'Anu', preferredLanguage: 'en', totalXp: 0, level: 1, streakDays: 0, consentRequired: false,
  enrollment: { curriculumId: 'c1', curriculumName: 'Kerala', academicYear: '2026-27', classId: 'k8', className: 'Class 8', boardCode: 'KL-SCERT' },
}

function sse(events: [string, unknown][]) {
  const text = events.map(([e, d]) => `event: ${e}\ndata: ${JSON.stringify(d)}\n\n`).join('')
  const bytes = new TextEncoder().encode(text)
  // Split into small chunks so the parser has to join partial events.
  const body = new ReadableStream<Uint8Array>({
    start(c) { for (let i = 0; i < bytes.length; i += 7) c.enqueue(bytes.slice(i, i + 7)); c.close() },
  })
  return new Response(body, { status: 200, headers: { 'Content-Type': 'text/event-stream' } })
}

describe('AI teacher streaming, photos and feedback', () => {
  it('shows the answer as it streams, then the final reply, and sends a thumbs down with a reason', async () => {
    const calls: { key: string; body: unknown }[] = []
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const key = `${init?.method ?? 'GET'} ${String(input).replace('/api/v1', '')}`
      const body = init?.body ? JSON.parse(String(init.body)) : undefined
      calls.push({ key, body })
      if (key === 'GET /students/me') return new Response(JSON.stringify(me))
      if (key === 'GET /curriculum/classes/k8/subjects') return new Response(JSON.stringify([{ id: 's1', name: 'Biology', code: 'BIO', mediumLanguage: 'en' }]))
      if (key === 'GET /ai/speech/status') return new Response(JSON.stringify({ serverVoice: false }))
      if (key === 'POST /ai/messages/stream') return sse([
        ['start', { grounded: true }],
        ['delta', { text: 'Plants make ' }],
        ['delta', { text: 'food from light.' }],
        ['done', { reply: 'Plants make food from light.', sources: [{ contentId: 'l1', title: 'Photosynthesis', kind: 'ApprovedMaterial' }],
          groundedInApprovedContent: true, remainingToday: 14, conversationId: 'c9', messageId: 'm9' }],
      ])
      if (key === 'POST /ai/messages/m9/feedback') return new Response(null, { status: 204 })
      return new Response(JSON.stringify({ title: `No mock for ${key}` }), { status: 500 })
    })

    wrap(<AiTeacherPage />, '/ai-teacher')
    await screen.findByRole('option', { name: 'Biology' })
    fireEvent.change(screen.getByLabelText('Type your question…'), { target: { value: 'How do plants eat?' } })
    fireEvent.click(screen.getByRole('button', { name: 'Send' }))

    expect(await screen.findByText('Plants make food from light.')).toBeInTheDocument()
    expect(screen.getByText(/From your lessons: Photosynthesis/)).toBeInTheDocument()
    expect(screen.getByText('14 questions left today')).toBeInTheDocument()
    expect(calls.find(c => c.key === 'POST /ai/messages/stream')?.body).toMatchObject({ message: 'How do plants eat?', imageBase64: null })

    fireEvent.click(screen.getByRole('button', { name: 'Not helpful' }))
    fireEvent.change(screen.getByLabelText('What was wrong? (optional)'), { target: { value: 'Too short' } })
    fireEvent.click(screen.getAllByRole('button', { name: 'Send' })[0])
    await waitFor(() => expect(calls.find(c => c.key === 'POST /ai/messages/m9/feedback')?.body).toEqual({ helpful: false, comment: 'Too short' }))
    expect(await screen.findByText('Thanks! This helps us improve.')).toBeInTheDocument()
  })

  it('shows the error sent in the middle of a stream and removes the empty answer', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const key = `${init?.method ?? 'GET'} ${String(input).replace('/api/v1', '')}`
      if (key === 'GET /students/me') return new Response(JSON.stringify(me))
      if (key === 'GET /curriculum/classes/k8/subjects') return new Response(JSON.stringify([{ id: 's1', name: 'Biology', code: 'BIO', mediumLanguage: 'en' }]))
      if (key === 'POST /ai/messages/stream') return sse([['start', {}], ['error', { title: "The AI teacher couldn't finish that answer. Please try again." }]])
      return new Response('{}', { status: 200 })
    })
    wrap(<AiTeacherPage />, '/ai-teacher')
    await screen.findByRole('option', { name: 'Biology' })
    fireEvent.change(screen.getByLabelText('Type your question…'), { target: { value: 'Hi' } })
    fireEvent.click(screen.getByRole('button', { name: 'Send' }))
    expect(await screen.findByRole('alert')).toHaveTextContent("couldn't finish that answer")
    expect(screen.queryByLabelText('Teacher is typing')).not.toBeInTheDocument()
  })
})
