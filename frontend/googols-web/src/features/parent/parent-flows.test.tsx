import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, wrap } from '../../test-utils'
import { FamilyPage } from '../family/FamilyPage'
import { LinkChildPage, ParentHomePage } from './ParentPages'

afterEach(() => vi.restoreAllMocks())

const me = {
  id: 'st1', displayName: 'Anu', preferredLanguage: 'en', totalXp: 0, level: 1, streakDays: 0, consentRequired: true,
  enrollment: { curriculumId: 'c1', curriculumName: 'Kerala State Syllabus', academicYear: '2026-27', classId: 'k8', className: 'Class 8', boardCode: 'KL-SCERT' },
}
const child = {
  studentId: 'st1', displayName: 'Anu', className: 'Class 8', boardCode: 'KL-SCERT', academicYear: '2026-27',
  level: 2, totalXp: 150, streakDays: 3, lastActiveDate: '2026-11-26', consentRequired: true, consentGranted: false,
  consentVersion: null, consentGrantedAtUtc: null, recentXp: [],
}

describe('Student family page', () => {
  it('creates a one-time parent code and lists linked parents', async () => {
    mockApi({
      'GET /students/me': () => ({ status: 200, json: me }),
      'GET /students/me/parents': () => ({ status: 200, json: [{ linkId: 'l1', relationship: 'mother', parentEmail: 'an•••@gmail.com', status: 'Verified', consentGranted: false }] }),
      'POST /students/me/parent-code': () => ({ status: 200, json: { code: 'ABCD-EFGH', expiresAtUtc: '2026-12-04T00:00:00Z' } }),
    })
    wrap(<FamilyPage />, '/family')
    expect(await screen.findByText('an•••@gmail.com')).toBeInTheDocument()
    expect(screen.getByText('Consent pending')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Create code/ }))
    expect(await screen.findByTestId('parent-code')).toHaveTextContent('ABCD-EFGH')
  })
})

describe('Parent linking and consent', () => {
  it('sends the code and relationship, and shows the server error for a bad code', async () => {
    const calls = mockApi({
      'POST /parents/link': () => ({ status: 400, json: { errors: { Code: ['That code is wrong or has expired. Ask your child to create a new one.'] } } }),
    })
    wrap(<LinkChildPage />, '/parent/link')
    const button = screen.getByRole('button', { name: 'Link child' })
    expect(button).toBeDisabled()
    fireEvent.change(screen.getByLabelText("Child's code"), { target: { value: 'abcd-efgh' } })
    fireEvent.change(screen.getByLabelText('You are their'), { target: { value: 'father' } })
    fireEvent.click(button)
    expect(await screen.findByText(/wrong or has expired/)).toBeInTheDocument()
    expect(calls.find(c => c.key === 'POST /parents/link')?.body).toEqual({ code: 'ABCD-EFGH', relationship: 'father' })
  })

  it('only allows consent after the parent ticks the notice, and sends the notice version', async () => {
    const calls = mockApi({
      'GET /parents/children': () => ({ status: 200, json: [child] }),
      'POST /parents/children/st1/consent': () => ({ status: 204 }),
    })
    wrap(<ParentHomePage />, '/parent')
    expect(await screen.findByText('Consent needed')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Review and give consent' }))
    const give = screen.getByRole('button', { name: 'Give consent' })
    expect(give).toBeDisabled()
    fireEvent.click(screen.getByRole('checkbox'))
    fireEvent.click(give)
    await waitFor(() => expect(calls.some(c => c.key === 'POST /parents/children/st1/consent')).toBe(true))
    expect(calls.find(c => c.key === 'POST /parents/children/st1/consent')?.body).toEqual({ accept: true, consentVersion: '2026-11' })
  })
})
