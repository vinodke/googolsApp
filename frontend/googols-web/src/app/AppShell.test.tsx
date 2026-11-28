import { fireEvent, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../core/auth/auth'
import { mockApi, wrap } from '../test-utils'
import { AppShell, type NavItem } from './AppShell'

afterEach(() => vi.restoreAllMocks())

const links: NavItem[] = [
  { to: '/', label: 'Home', icon: 'home', end: true },
  { to: '/subjects', label: 'Subjects', icon: 'book' },
  { to: '/ai-teacher', label: 'AI Teacher', icon: 'sparkles' },
  { to: '/homework', label: 'Homework', icon: 'pen' },
  { to: '/tests', label: 'Tests', icon: 'clipboard' },
  { to: '/saved', label: 'Saved', icon: 'bookmark' },
]

function shell(path: string) {
  mockApi({ 'GET /auth/manage/info': () => ({ status: 200, json: { email: 'ammu@example.com', isEmailConfirmed: true } }) })
  wrap(<AuthProvider><AppShell subtitle="Class 1" sectionLabel="Menu" links={links} signOutLabel="Sign out"><p>page</p></AppShell></AuthProvider>, path)
}

describe('Phone app shell', () => {
  it('keeps four tabs in the bottom bar and moves the rest into the More sheet', async () => {
    shell('/')
    const tabs = within(screen.getByRole('navigation', { name: 'Tabs' }))
    expect(tabs.getAllByRole('link').map(l => l.textContent)).toEqual(['Home', 'Subjects', 'AI Teacher', 'Homework'])
    expect(screen.queryByRole('dialog')).toBeNull()

    fireEvent.click(tabs.getByRole('button', { name: 'More' }))
    const sheet = within(await screen.findByRole('dialog', { name: 'Menu' }))
    expect(sheet.getAllByRole('link').map(l => l.textContent)).toEqual(['Tests', 'Saved'])
    expect(sheet.getByRole('button', { name: /Sign out/ })).toBeInTheDocument()

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('shows a back button and a screen title on detail pages', () => {
    shell('/lessons/l1')
    expect(screen.getByRole('button', { name: 'Back' })).toBeInTheDocument()
    expect(screen.getByRole('banner')).toHaveTextContent('Lesson')
  })
})
