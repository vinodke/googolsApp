// Thin fetch wrapper. Auth is an HttpOnly cookie set by ASP.NET Core Identity (?useCookies=true),
// so no token is ever readable from JavaScript. Errors arrive as RFC 7807 problem details.

export interface ProblemDetails {
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
  message?: string
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails

  constructor(status: number, problem: ProblemDetails) {
    super(problem.title ?? problem.message ?? `Request failed (${status})`)
    this.status = status
    this.problem = problem
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`/api/v1${path}`, {
    method,
    credentials: 'include',
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (res.status === 204) return undefined as T
  const text = await res.text()
  const data = text ? JSON.parse(text) : undefined
  if (!res.ok) throw new ApiError(res.status, (data ?? {}) as ProblemDetails)
  return data as T
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  put: <T>(path: string, body?: unknown) => request<T>('PUT', path, body ?? {}),
  del: <T>(path: string) => request<T>('DELETE', path),
}

/** First validation message per field, for showing under inputs. */
export function fieldErrors(err: unknown): Record<string, string> {
  if (!(err instanceof ApiError) || !err.problem.errors) return {}
  return Object.fromEntries(Object.entries(err.problem.errors).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v[0]]))
}

export function errorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.problem.errors) return Object.values(err.problem.errors).flat()[0] ?? err.message
    return err.message
  }
  return 'Could not reach Googols. Check your connection and try again.'
}
