import { fireEvent, render, screen } from '@testing-library/react'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, wrap } from '../../test-utils'
import { HeatmapTable } from '../../shared/HeatmapTable'
import { WeeklyReportPage } from '../parent/WeeklyReportPage'
import { HomeworkPage } from './HomeworkPage'

afterEach(() => vi.restoreAllMocks())

describe('Heat map', () => {
  it('colours cells by mastery and leaves untested cells blank', () => {
    render(<HeatmapTable data={{
      chapters: [{ chapterId: 'c1', title: 'Cells', subject: 'Biology', average: 55 }, { chapterId: 'c2', title: 'Plants', subject: 'Biology', average: null }],
      students: [{ studentId: 's1', name: 'Anu', cells: [30, null] }, { studentId: 's2', name: 'Ravi', cells: [80, null] }],
    }} />)
    expect(screen.getByTitle('Anu · Cells: 30%')).toHaveClass('hm-low')
    expect(screen.getByTitle('Ravi · Cells: 80%')).toHaveClass('hm-high')
    expect(screen.getByTitle('Anu · Plants: not tested')).toHaveClass('hm-none')
  })
})

describe('Homework', () => {
  it('links lesson homework and lets the student mark a task done with a note', async () => {
    const calls = mockApi({
      'GET /learning/homework': () => ({ status: 200, json: [
        { id: 'h1', title: 'Read chapter 1', instructions: null, kind: 'Lesson', classroom: '8A Biology', lessonId: 'l1', chapterId: null, examId: null, target: 'Cells', dueAtUtc: null, done: false, doneAtUtc: null, overdue: false, myText: null },
        { id: 'h2', title: 'Draw a leaf', instructions: 'Label the parts.', kind: 'Task', classroom: '8A Biology', lessonId: null, chapterId: null, examId: null, target: null, dueAtUtc: '2026-11-20T10:00:00Z', done: false, doneAtUtc: null, overdue: true, myText: null },
      ] }),
      'GET /learning/timetable': () => ({ status: 200, json: [] }),
      'POST /learning/homework/h2/done': () => ({ status: 204 }),
    })
    wrap(<HomeworkPage />)
    expect(await screen.findByRole('link', { name: /Open lesson/ })).toHaveAttribute('href', '/lessons/l1')
    expect(screen.getByText('Overdue')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Note for Draw a leaf'), { target: { value: 'Done in my notebook' } })
    fireEvent.click(screen.getByRole('button', { name: 'Mark as done' }))
    await vi.waitFor(() => expect(calls.find(c => c.key === 'POST /learning/homework/h2/done')?.body).toEqual({ text: 'Done in my notebook' }))
  })
})

describe('Weekly parent report', () => {
  it('shows the week and turns the weekly email off', async () => {
    const report = {
      studentId: 'st1', name: 'Anu', weekStart: '2026-11-23', weekEnd: '2026-11-29', lessonsCompleted: 4, lessons: ['Cells'], quizzes: 2, tests: 1,
      averageScore: 72, bestScore: 90, xpGained: 180, activeDays: 4, streakDays: 3, aiQuestions: 6, practised: [{ title: 'Cells', subject: 'Biology', mastery: 72 }],
      weakAreas: [{ title: 'Plants', subject: 'Biology', mastery: 35 }], homeworkSet: 2, homeworkDone: 1, homeworkOverdue: 1, lessonsLastWeek: 2, xpLastWeek: 90,
      emailEnabled: true, emailConfigured: false,
    }
    const calls = mockApi({
      [`GET /parents/children/st1/weekly-report?week=${new Date().getFullYear()}-${String(new Date().getMonth() + 1).padStart(2, '0')}-${String(new Date().getDate()).padStart(2, '0')}`]: () => ({ status: 200, json: report }),
      'PUT /parents/children/st1/report-email': () => ({ status: 204 }),
    })
    wrap(<Routes><Route path="/parent/children/:studentId/report" element={<WeeklyReportPage />} /></Routes>, '/parent/children/st1/report')
    expect(await screen.findByText("Anu's week")).toBeInTheDocument()
    expect(screen.getByText(/up from 2 last week/)).toBeInTheDocument()
    expect(screen.getByText('Plants')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Send me the weekly email' }))
    await vi.waitFor(() => expect(calls.find(c => c.key === 'PUT /parents/children/st1/report-email')?.body).toEqual({ enabled: false }))
  })
})
