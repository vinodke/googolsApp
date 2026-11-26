import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AiTeacherPage } from './ai-teacher/AiTeacherPage'
import { OnboardingPage } from './onboarding/OnboardingPage'

// Fake API: maps "METHOD path" to a JSON response, records request bodies.
type Handler = (body: unknown) => { status: number; json?: unknown }
function mockApi(routes: Record<string, Handler>) {
  const calls: { key: string; body: unknown }[] = []
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = typeof input === 'string' ? input : input.toString()
    const key = `${init?.method ?? 'GET'} ${url.replace('/api/v1', '')}`
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ key, body })
    const handler = routes[key]
    if (!handler) return new Response(JSON.stringify({ title: `No mock for ${key}` }), { status: 500 })
    const r = handler(body)
    return new Response(r.json === undefined ? null : JSON.stringify(r.json), { status: r.status })
  })
  return calls
}

function wrap(ui: ReactNode, path = '/') {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="*" element={ui} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

afterEach(() => vi.restoreAllMocks())

const me = {
  id: 'st1', displayName: 'Anu', preferredLanguage: 'ml', totalXp: 0, level: 1, streakDays: 0, consentRequired: false,
  enrollment: { curriculumId: 'c1', curriculumName: 'Kerala State Syllabus', academicYear: '2026-27', classId: 'k8', className: 'Class 8', boardCode: 'KL-SCERT' },
}

describe('Onboarding', () => {
  it('walks board → year → class and posts the chosen syllabus and language', async () => {
    const calls = mockApi({
      'GET /students/me': () => ({ status: 404, json: { message: 'No student profile yet.' } }),
      'GET /curriculum/boards': () => ({ status: 200, json: [{ id: 'b1', name: 'Kerala State (SCERT)', code: 'KL-SCERT', countryCode: 'IN', isEntranceExam: false }] }),
      'GET /curriculum/boards/b1/curricula': () => ({ status: 200, json: [{ id: 'c1', name: 'Kerala State Syllabus', academicYear: '2026-27', version: 1 }] }),
      'GET /curriculum/curricula/c1/classes': () => ({ status: 200, json: [{ id: 'k8', gradeNumber: 8, gradeName: 'Class 8' }] }),
      'POST /students/me/onboarding': () => ({ status: 201, json: { id: 'st1' } }),
    })
    wrap(<OnboardingPage />, '/onboarding')

    fireEvent.change(await screen.findByLabelText('Your name'), { target: { value: 'Anu' } })
    await screen.findByRole('option', { name: 'Kerala State (SCERT)' })
    fireEvent.change(screen.getByLabelText('Syllabus'), { target: { value: 'b1' } })
    await screen.findByRole('option', { name: 'Kerala State Syllabus (2026-27)' })
    fireEvent.change(screen.getByLabelText('Academic year'), { target: { value: 'c1' } })
    await screen.findByRole('option', { name: 'Class 8' })
    fireEvent.change(screen.getByLabelText('Class'), { target: { value: 'k8' } })
    fireEvent.click(screen.getByRole('button', { name: 'Start learning' }))

    await waitFor(() => expect(calls.some(c => c.key === 'POST /students/me/onboarding')).toBe(true))
    expect(calls.find(c => c.key === 'POST /students/me/onboarding')!.body).toEqual({
      displayName: 'Anu', dateOfBirth: null, preferredLanguage: 'ml', curriculumId: 'c1', classId: 'k8',
    })
  })

  it('shows the server validation message under the class field', async () => {
    mockApi({
      'GET /students/me': () => ({ status: 404 }),
      'GET /curriculum/boards': () => ({ status: 200, json: [{ id: 'b1', name: 'Kerala State (SCERT)', code: 'KL-SCERT', countryCode: 'IN', isEntranceExam: false }] }),
      'GET /curriculum/boards/b1/curricula': () => ({ status: 200, json: [{ id: 'c1', name: 'Kerala State Syllabus', academicYear: '2026-27', version: 1 }] }),
      'GET /curriculum/curricula/c1/classes': () => ({ status: 200, json: [{ id: 'k8', gradeNumber: 8, gradeName: 'Class 8' }] }),
      'POST /students/me/onboarding': () => ({ status: 400, json: { title: 'One or more validation errors occurred.', errors: { classId: ['That class is not available for this curriculum.'] } } }),
    })
    wrap(<OnboardingPage />, '/onboarding')
    fireEvent.change(await screen.findByLabelText('Your name'), { target: { value: 'Anu' } })
    await screen.findByRole('option', { name: 'Kerala State (SCERT)' })
    fireEvent.change(screen.getByLabelText('Syllabus'), { target: { value: 'b1' } })
    await screen.findByRole('option', { name: 'Kerala State Syllabus (2026-27)' })
    fireEvent.change(screen.getByLabelText('Academic year'), { target: { value: 'c1' } })
    await screen.findByRole('option', { name: 'Class 8' })
    fireEvent.change(screen.getByLabelText('Class'), { target: { value: 'k8' } })
    fireEvent.click(screen.getByRole('button', { name: 'Start learning' }))
    expect(await screen.findByText('That class is not available for this curriculum.')).toBeInTheDocument()
  })
})

describe('AI Teacher', () => {
  const routes = (reply: Handler) => ({
    'GET /students/me': () => ({ status: 200, json: me }),
    'GET /curriculum/classes/k8/subjects': () => ({ status: 200, json: [{ id: 's1', name: 'Biology', code: 'BIO-8', mediumLanguage: 'ml' }] }),
    'POST /ai/messages': reply,
  })

  it('sends mode as a name (the bug found on the live site) and shows the reply with an honest label', async () => {
    const calls = mockApi(routes(() => ({
      status: 200,
      json: { reply: 'ഇലകൾ സൂര്യപ്രകാശം ഉപയോഗിച്ച് ഭക്ഷണം ഉണ്ടാക്കുന്നു.', sources: [], groundedInApprovedContent: false, remainingToday: 39 },
    })))
    wrap(<AiTeacherPage />, '/ai-teacher?subject=s1&chapter=ch1')

    await screen.findByRole('option', { name: 'Biology' })
    fireEvent.change(screen.getByLabelText('നിങ്ങളുടെ ചോദ്യം ടൈപ്പ് ചെയ്യൂ…'), { target: { value: 'Photosynthesis എന്താണ്?' } })
    fireEvent.click(screen.getByRole('button', { name: 'അയക്കുക' }))

    expect(await screen.findByText('ഇലകൾ സൂര്യപ്രകാശം ഉപയോഗിച്ച് ഭക്ഷണം ഉണ്ടാക്കുന്നു.')).toBeInTheDocument()
    expect(screen.getByText('പൊതുവായ വിശദീകരണം: പാഠപുസ്തകത്തിൽ നിന്നല്ല')).toBeInTheDocument()
    expect(screen.getByText('39 questions left today')).toBeInTheDocument()

    const sent = calls.find(c => c.key === 'POST /ai/messages')!.body as Record<string, unknown>
    expect(sent).toMatchObject({ subjectId: 's1', chapterId: 'ch1', mode: 'PersonalTeacher', language: 'ml', message: 'Photosynthesis എന്താണ്?' })
    expect(typeof sent.mode).toBe('string')
  })

  it('shows the child-friendly reason when a question is blocked', async () => {
    mockApi(routes(() => ({ status: 422, json: { title: "Please don't share phone numbers or email addresses here. Ask your question without them." } })))
    wrap(<AiTeacherPage />, '/ai-teacher')
    await screen.findByRole('option', { name: 'Biology' })
    fireEvent.change(screen.getByLabelText('നിങ്ങളുടെ ചോദ്യം ടൈപ്പ് ചെയ്യൂ…'), { target: { value: 'my phone is 9876543210' } })
    fireEvent.click(screen.getByRole('button', { name: 'അയക്കുക' }))
    expect(await screen.findByRole('alert')).toHaveTextContent("Please don't share phone numbers")
  })

  it('stays locked when parental consent is required', async () => {
    mockApi({ ...routes(() => ({ status: 200 })), 'GET /students/me': () => ({ status: 200, json: { ...me, consentRequired: true } }) })
    wrap(<AiTeacherPage />, '/ai-teacher')
    expect(await screen.findByText(/parent needs to give permission/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'അയക്കുക' })).not.toBeInTheDocument()
  })
})
