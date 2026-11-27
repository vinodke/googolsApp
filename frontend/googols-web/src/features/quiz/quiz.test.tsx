import { fireEvent, screen } from '@testing-library/react'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, wrap } from '../../test-utils'
import { QuizPage } from './QuizPage'

afterEach(() => vi.restoreAllMocks())

describe('Chapter quiz', () => {
  it('answers questions one at a time, submits the chosen indexes and shows the review', async () => {
    const calls = mockApi({
      'POST /learning/chapters/ch1/quiz': () => ({ status: 200, json: { attemptId: 'a1', chapterId: 'ch1', chapterTitle: 'Photosynthesis', questions: [
        { id: 'q1', prompt: 'Leaves need?', options: ['Sunlight', 'Sand'] },
        { id: 'q2', prompt: 'Gas taken in?', options: ['Oxygen', 'Carbon dioxide'] },
      ] } }),
      'POST /learning/quiz-attempts/a1/submit': () => ({ status: 200, json: { attemptId: 'a1', score: 1, total: 2, percent: 50, xpAwarded: 15, streakDays: 1, review: [
        { questionId: 'q1', prompt: 'Leaves need?', options: ['Sunlight', 'Sand'], chosen: 0, correct: 0, isCorrect: true, explanation: null },
        { questionId: 'q2', prompt: 'Gas taken in?', options: ['Oxygen', 'Carbon dioxide'], chosen: 0, correct: 1, isCorrect: false, explanation: 'Plants take in CO2.' },
      ] } }),
    })
    wrap(<Routes><Route path="/chapters/:chapterId/quiz" element={<QuizPage />} /></Routes>, '/chapters/ch1/quiz')
    fireEvent.click(screen.getByRole('button', { name: 'Start quiz' }))
    fireEvent.click(await screen.findByRole('radio', { name: /Sunlight/ }))
    fireEvent.click(screen.getByRole('button', { name: /Next/ }))
    fireEvent.click(screen.getByRole('radio', { name: /Oxygen/ }))
    fireEvent.click(screen.getByRole('button', { name: /Submit \(2\/2 answered\)/ }))
    expect(await screen.findByText('+15 XP')).toBeInTheDocument()
    expect(screen.getByText('Plants take in CO2.')).toBeInTheDocument()
    expect(calls.find(c => c.key === 'POST /learning/quiz-attempts/a1/submit')?.body).toEqual({ answers: [0, 0] })
  })
})
