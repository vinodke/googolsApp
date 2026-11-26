import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AdminBoard } from '../../core/api/types'
import { AdminCurriculumPage } from './AdminCurriculumPage'

const tree: AdminBoard[] = [{
  id: 'b1', name: 'Kerala State (SCERT)', code: 'KL-SCERT', countryCode: 'IN', isEntranceExam: false,
  curricula: [
    { id: 'c1', name: 'Kerala State Syllabus', academicYear: '2026-27', version: 1, status: 'Draft', classes: [] },
    {
      id: 'c0', name: 'Kerala State Syllabus', academicYear: '2025-26', version: 1, status: 'Published', classes: [{
        id: 'k8', gradeNumber: 8, gradeName: 'Class 8', subjects: [{
          id: 's1', name: 'Biology', code: 'BIO-8', mediumLanguage: 'ml', sequence: 1,
          chapters: [{ id: 'ch1', title: 'Photosynthesis', sequence: 1, status: 'Published' }],
        }],
      }],
    },
  ],
}]

function renderPage() {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(tree), { status: 200 }))
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}><AdminCurriculumPage /></QueryClientProvider>)
}

afterEach(() => vi.restoreAllMocks())

describe('AdminCurriculumPage', () => {
  it('shows the catalog tree with statuses', async () => {
    renderPage()
    expect(await screen.findByText('Photosynthesis')).toBeInTheDocument()
    expect(screen.getByText('BIO-8', { exact: false })).toBeInTheDocument()
    expect(screen.getAllByText('Draft').length).toBeGreaterThan(0)
  })

  it('blocks publishing an academic year that has no classes', async () => {
    renderPage()
    await screen.findByText('Photosynthesis')
    const publish = screen.getAllByRole('button', { name: 'Publish' })
    expect(publish[0]).toBeDisabled()
    expect(publish[0]).toHaveAttribute('title', 'Add a class first')
    expect(screen.getAllByRole('button', { name: 'Unpublish' }).length).toBe(2) // published year + published chapter
  })
})
