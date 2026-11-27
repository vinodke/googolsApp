import { fireEvent, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, wrap } from '../../test-utils'
import { AdminAiPage } from './AdminAiPage'

afterEach(() => vi.restoreAllMocks())

const usage = {
  days: 14, usdToInr: 85, helpful: 3, notHelpful: 1,
  totals: { calls: 12, inputTokens: 9000, outputTokens: 4000, speechCharacters: 0, costUsd: 0.01, costInr: 0.85 },
  byFeature: [{ feature: 'teacher', calls: 12, tokens: 13000, costUsd: 0.01 }],
  daily: [{ day: '2026-11-27', calls: 12, costUsd: 0.01 }],
  recentNotHelpful: [{ messageId: 'm1', question: 'What is osmosis?', answer: 'It is…', comment: 'Wrong chapter', at: '2026-11-27T05:00:00Z', language: 'en' }],
}

describe('Admin AI quality', () => {
  it('shows cost by feature, the not-helpful answers, and runs the golden set', async () => {
    const calls = mockApi({
      'GET /admin/ai/usage?days=14': () => ({ status: 200, json: usage }),
      'GET /admin/ai/evals/cases': () => ({ status: 200, json: [{ id: 'e1', subjectId: 's1', subjectName: 'Biology', chapterId: null, chapterTitle: null,
        question: 'പ്രകാശസംശ്ലേഷണം എന്താണ്?', language: 'ml', mode: 'PersonalTeacher', expectedKeywords: 'chlorophyll', forbiddenPhrases: null, expectGrounded: true }] }),
      'GET /admin/ai/evals/runs': () => ({ status: 200, json: [] }),
      'GET /curriculum/admin/tree': () => ({ status: 200, json: [] }),
      'POST /admin/ai/evals/run': () => ({ status: 200, json: {
        run: { id: 'r1', createdAtUtc: '2026-11-27T05:00:00Z', promptVersion: 'teacher-prompt-v2', model: 'gpt-4.1-mini', total: 1, passed: 0, averageScore: 0.75 },
        results: [{ caseId: 'e1', question: 'പ്രകാശസംശ്ലേഷണം എന്താണ്?', language: 'ml', passed: false, score: 0.75, answer: 'സസ്യങ്ങൾ…', grounded: true, error: null,
          checks: [{ name: 'Right language', passed: true, detail: null }, { name: 'Key points covered', passed: false, detail: 'Missing: chlorophyll' }] }],
      } }),
    })
    wrap(<AdminAiPage />, '/admin/ai')
    expect(await screen.findByText('AI teacher chat')).toBeInTheDocument()
    expect(screen.getByText('75% of 4')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: 'Student ratings' }))
    expect(screen.getByText('What is osmosis?')).toBeInTheDocument()
    expect(screen.getByText('Wrong chapter')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: 'Test questions' }))
    await screen.findByText('പ്രകാശസംശ്ലേഷണം എന്താണ്?')
    fireEvent.click(screen.getByRole('button', { name: /Run all tests/ }))
    expect(await screen.findByText(/0\/1 passed/)).toBeInTheDocument()
    expect(screen.getByText(/Missing: chlorophyll/)).toBeInTheDocument()
    expect(calls.some(c => c.key === 'POST /admin/ai/evals/run')).toBe(true)
  })
})
