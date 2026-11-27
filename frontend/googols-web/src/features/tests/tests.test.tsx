import { fireEvent, screen } from '@testing-library/react'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, wrap } from '../../test-utils'
import { MissionsCard } from '../progress/ProgressPages'
import { ExamAttemptPage } from './TestsPages'

afterEach(() => vi.restoreAllMocks())

const now = new Date('2026-11-27T06:00:00Z')
const attempt = {
  attemptId: 'a1', examId: 'e1', title: 'Biology practice test', kind: 'Practice', startedAtUtc: now.toISOString(),
  deadlineUtc: new Date(now.getTime() + 10 * 60_000).toISOString(), serverNowUtc: now.toISOString(), submitted: false,
  questions: [{ id: 'q1', prompt: 'Leaves need?', options: ['Sunlight', 'Sand'] }],
  written: [{ id: 'w1', prompt: 'Explain photosynthesis.', maxMarks: 3 }],
  answers: [null], writtenAnswers: [], result: null,
}

describe('Timed test', () => {
  it('shows the countdown, submits MCQ and written answers, and labels AI marks as provisional', async () => {
    const calls = mockApi({
      'GET /learning/exam-attempts/a1': () => ({ status: 200, json: attempt }),
      'PUT /learning/exam-attempts/a1/answers': () => ({ status: 204 }),
      'POST /learning/exam-attempts/a1/submit': () => ({ status: 200, json: {
        mcqScore: 1, mcqTotal: 1, writtenScore: 2, writtenMax: 3, percent: 75, provisional: true, xpAwarded: 25, newBadges: [], late: false,
        review: [{ questionId: 'q1', prompt: 'Leaves need?', options: ['Sunlight', 'Sand'], chosen: 0, correct: 0, isCorrect: true, explanation: null }],
        written: [{ answerId: 'wa1', questionId: 'w1', prompt: 'Explain photosynthesis.', answer: 'Plants use light.', score: 2, max: 3, status: 'Provisional',
          criteria: [{ criterion: 'Mentions light', awarded: 1, max: 1, comment: null }, { criterion: 'Names glucose', awarded: 0, max: 1, comment: 'Missing' }],
          feedback: 'Name the food made.', teacherComment: null, modelAnswer: null }],
      } }),
    })
    wrap(<Routes><Route path="/tests/attempts/:attemptId" element={<ExamAttemptPage />} /></Routes>, '/tests/attempts/a1')
    expect(await screen.findByRole('timer')).toHaveTextContent(/(10|9):\d\d/)
    fireEvent.click(screen.getByRole('radio', { name: /Sunlight/ }))
    fireEvent.change(screen.getByLabelText('Answer to: Explain photosynthesis.'), { target: { value: 'Plants use light.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Submit (2/2)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Submit test' }))
    expect(await screen.findByText('75%')).toBeInTheDocument()
    expect(screen.getByText(/Your teacher may change the marks/)).toBeInTheDocument()
    expect(screen.getByText('Name the food made.', { exact: false })).toBeInTheDocument()
    expect(calls.find(c => c.key === 'POST /learning/exam-attempts/a1/submit')?.body).toEqual({ answers: [0], written: [{ questionId: 'w1', text: 'Plants use light.' }] })
  })
})

describe('Daily missions', () => {
  it('lets the student collect the bonus only when all missions are done', async () => {
    const calls = mockApi({
      'GET /learning/missions': () => ({ status: 200, json: {
        date: '2026-11-27', allDone: true, claimed: false, bonusXp: 20, weekStart: '2026-11-23', challengeXp: 50,
        missions: [{ code: 'lesson', title: 'Finish a lesson', progress: 1, target: 1, done: true }],
        challenges: [{ code: 'lessons-5', title: 'Lesson marathon', description: 'Finish 5 lessons this week', progress: 2, target: 5, done: false, claimed: false }],
      } }),
      'POST /learning/missions/claim': () => ({ status: 200, json: { xpAwarded: 20 } }),
    })
    wrap(<MissionsCard />)
    fireEvent.click(await screen.findByRole('button', { name: 'Collect +20 XP' }))
    expect(screen.getByRole('button', { name: '2/5' })).toBeDisabled()
    await vi.waitFor(() => expect(calls.some(c => c.key === 'POST /learning/missions/claim')).toBe(true))
  })
})
