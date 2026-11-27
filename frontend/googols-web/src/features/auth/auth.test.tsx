import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, wrap } from '../../test-utils'
import { AuthProvider } from '../../core/auth/auth'
import { LoginPage } from './AuthPages'

afterEach(() => vi.restoreAllMocks())

describe('Sign in', () => {
  it('offers Continue with Google when the server has it switched on, and explains Google errors', async () => {
    mockApi({
      'GET /auth/manage/info': () => ({ status: 401 }),
      'GET /auth/providers': () => ({ status: 200, json: ['Google'] }),
    })
    wrap(<AuthProvider><LoginPage /></AuthProvider>, '/login?error=google-unverified')
    const google = await screen.findByRole('link', { name: /Continue with Google/ })
    expect(google).toHaveAttribute('href', '/api/v1/auth/external/google?returnUrl=%2F')
    expect(screen.getByRole('alert')).toHaveTextContent('isn\'t verified')
  })
})
