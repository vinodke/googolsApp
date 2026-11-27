import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, wrap } from '../../test-utils'
import { LessonBody } from './LessonBody'
import { LessonPage } from './LessonPages'
import { Route, Routes } from 'react-router'

afterEach(() => vi.restoreAllMocks())

describe('LessonBody', () => {
  it('renders headings, lists and bold without injecting HTML', () => {
    const { container } = render(<LessonBody text={'## Leaves\n\nLeaves are **green**.\n\n- sunlight\n- water\n\n<img src=x onerror=alert(1)>'} />)
    expect(screen.getByRole('heading', { name: 'Leaves' })).toBeInTheDocument()
    expect(screen.getByText('green').tagName).toBe('STRONG')
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
    expect(container.querySelector('img')).toBeNull()
    expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeInTheDocument()
  })

  it("shows the lesson's own pictures and nothing else", () => {
    const text = '![പറവകൾ](pic:1)\n\n![Summary](pic:poster)\n\n![x](https://evil.example/a.png)\n\n![y](pic:../../secret)'
    const { container } = render(<LessonBody text={text} lessonId="l1" />)
    const imgs = [...container.querySelectorAll('img')].map(i => i.getAttribute('src'))
    expect(imgs).toEqual(['/api/v1/learning/lessons/l1/pictures/pic-1', '/api/v1/learning/lessons/l1/pictures/poster'])
    expect(screen.getByText('പറവകൾ').tagName).toBe('FIGCAPTION')
    expect(screen.getByText('![x](https://evil.example/a.png)')).toBeInTheDocument()
  })
})

const me = {
  id: 'st1', displayName: 'Anu', preferredLanguage: 'en', totalXp: 0, level: 1, streakDays: 0, consentRequired: false,
  enrollment: { curriculumId: 'c1', curriculumName: 'Kerala State Syllabus', academicYear: '2026-27', classId: 'k8', className: 'Class 8', boardCode: 'KL-SCERT' },
}
const lesson = {
  id: 'l1', title: 'What is photosynthesis?', body: 'Plants make food.', language: 'en', kind: 'OfficialSyllabus', estimatedMinutes: 8,
  chapterId: 'ch1', chapterTitle: 'Photosynthesis', subjectId: 's1', subjectName: 'Biology', completed: false,
  attachmentFileName: 'pages.pdf', attachmentBytes: 2048, previousLessonId: null, nextLessonId: 'l2',
}

describe('Lesson page', () => {
  it('marks the lesson complete and shows the XP earned', async () => {
    const calls = mockApi({
      'GET /students/me': () => ({ status: 200, json: me }),
      'GET /learning/lessons/l1': () => ({ status: 200, json: lesson }),
      'POST /learning/lessons/l1/complete': () => ({ status: 200, json: { xpAwarded: 30, chapterCompleted: true, streakDays: 2, totalXp: 130 } }),
    })
    wrap(<Routes><Route path="/lessons/:lessonId" element={<LessonPage />} /></Routes>, '/lessons/l1')
    expect(await screen.findByRole('heading', { name: 'What is photosynthesis?' })).toBeInTheDocument()
    expect(screen.getByText('From the textbook')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /pages.pdf/ })).toHaveAttribute('href', '/api/v1/learning/lessons/l1/attachment')
    fireEvent.click(screen.getByRole('button', { name: /Mark as complete/ }))
    expect(await screen.findByText('+30 XP')).toBeInTheDocument()
    expect(screen.getByText('Chapter finished!')).toBeInTheDocument()
    await waitFor(() => expect(calls.some(c => c.key === 'POST /learning/lessons/l1/complete')).toBe(true))
    expect(screen.getByRole('link', { name: /Next lesson/ })).toHaveAttribute('href', '/lessons/l2')
  })

  it('shows a picture attachment inside the lesson instead of as a download', async () => {
    mockApi({
      'GET /students/me': () => ({ status: 200, json: me }),
      'GET /learning/lessons/l1': () => ({ status: 200, json: { ...lesson, attachmentFileName: 'infographic.jpg' } }),
    })
    wrap(<Routes><Route path="/lessons/:lessonId" element={<LessonPage />} /></Routes>, '/lessons/l1')
    const img = await screen.findByRole('img', { name: /Picture summary/ })
    expect(img).toHaveAttribute('src', '/api/v1/learning/lessons/l1/attachment')
    expect(screen.queryByText(/download/)).toBeNull()
  })
})
