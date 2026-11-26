import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api, errorMessage, fieldErrors } from './client'

afterEach(() => vi.restoreAllMocks())

describe('api client', () => {
  it('sends cookies and parses JSON', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ ok: 1 }), { status: 200 }))
    await expect(api.get('/students/me')).resolves.toEqual({ ok: 1 })
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/students/me', expect.objectContaining({ credentials: 'include', method: 'GET' }))
  })

  it('turns problem details into ApiError with field messages', async () => {
    const problem = { title: 'One or more validation errors occurred.', status: 400, errors: { DisplayName: ['Required'] } }
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(problem), { status: 400 }))
    const err = await api.post('/students/me/onboarding', {}).catch(e => e)
    expect(err).toBeInstanceOf(ApiError)
    expect(fieldErrors(err)).toEqual({ displayName: 'Required' })
    expect(errorMessage(err)).toBe('Required')
  })

  it('gives a friendly message when offline', () => {
    expect(errorMessage(new TypeError('Failed to fetch'))).toMatch(/connection/)
  })
})
