import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../../core/auth/auth'
import { mockApi, wrap } from '../../test-utils'
import { SiteLayout } from './SiteLayout'
import { ContactPage, HomePage } from './SitePages'

afterEach(() => vi.restoreAllMocks())

describe('Public website', () => {
  it('shows the menu with log in and sign up for visitors', async () => {
    mockApi({ 'GET /auth/manage/info': () => ({ status: 401 }) })
    wrap(<AuthProvider><SiteLayout><HomePage /></SiteLayout></AuthProvider>, '/')
    for (const name of ['Features', 'How it works', 'About us', 'Contact']) {
      expect(screen.getAllByRole('link', { name }).length).toBeGreaterThan(0)
    }
    expect((await screen.findAllByRole('link', { name: 'Log in' }))[0]).toHaveAttribute('href', '/login')
    expect(screen.getAllByRole('link', { name: 'Sign up free' })[0]).toHaveAttribute('href', '/register')
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('AI teacher')
  })

  it('sends the contact form and thanks the visitor', async () => {
    const calls = mockApi({ 'POST /contact': () => ({ status: 201, json: { id: 'm1' } }) })
    wrap(<ContactPage />, '/contact')
    fireEvent.change(screen.getByLabelText('Your name'), { target: { value: 'Anitha' } })
    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'anitha@example.com' } })
    fireEvent.change(screen.getByLabelText('Topic'), { target: { value: 'school' } })
    fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'We would like Googols for our school.' } })
    fireEvent.click(screen.getByRole('button', { name: /Send message/ }))
    expect(await screen.findByText('Thank you!')).toBeInTheDocument()
    await waitFor(() => expect(calls[0]?.body).toMatchObject({ name: 'Anitha', topic: 'school', website: '' }))
  })
})
