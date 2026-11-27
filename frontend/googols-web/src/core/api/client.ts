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
  /** raw text body (e.g. CSV) */
  postText: async <T>(path: string, text: string, contentType = 'text/csv'): Promise<T> => {
    const res = await fetch(`/api/v1${path}`, { method: 'POST', credentials: 'include', headers: { 'Content-Type': contentType }, body: text })
    const raw = await res.text()
    const data = raw ? JSON.parse(raw) : undefined
    if (!res.ok) throw new ApiError(res.status, (data ?? {}) as ProblemDetails)
    return data as T
  },
  /** multipart upload (browser sets the boundary) */
  upload: async <T>(path: string, form: FormData): Promise<T> => {
    const res = await fetch(`/api/v1${path}`, { method: 'POST', credentials: 'include', body: form })
    const text = await res.text()
    const data = text ? JSON.parse(text) : undefined
    if (!res.ok) throw new ApiError(res.status, (data ?? {}) as ProblemDetails)
    return data as T
  },
}

function parseJson(text: string): unknown {
  try { return text ? JSON.parse(text) : undefined } catch { return undefined }
}

/**
 * POSTs and reads a server-sent-events stream ("event: x\ndata: {json}\n\n"), calling onEvent for each event.
 * If the server answers with plain JSON instead (an older server, or an immediate reply), that JSON arrives as one "done" event.
 */
export async function streamEvents(path: string, body: unknown, onEvent: (event: string, data: unknown) => void, signal?: AbortSignal): Promise<void> {
  const res = await fetch(`/api/v1${path}`, {
    method: 'POST', credentials: 'include', signal,
    headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new ApiError(res.status, (parseJson(await res.text()) ?? {}) as ProblemDetails)
  const type = res.headers.get('content-type') ?? ''
  if (!type.includes('text/event-stream') || !res.body) {
    onEvent('done', parseJson(await res.text()))
    return
  }
  const dispatch = (block: string) => {
    let event = 'message'
    const data: string[] = []
    for (const line of block.split('\n')) {
      if (line.startsWith('event:')) event = line.slice(6).trim()
      else if (line.startsWith('data:')) data.push(line.slice(5).trimStart())
    }
    if (data.length) onEvent(event, parseJson(data.join('\n')))
  }
  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  for (;;) {
    const { done, value } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n')
    let cut: number
    while ((cut = buffer.indexOf('\n\n')) >= 0) {
      dispatch(buffer.slice(0, cut))
      buffer = buffer.slice(cut + 2)
    }
  }
  buffer += decoder.decode()
  if (buffer.trim()) dispatch(buffer)
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
  // fetch() network failures are TypeErrors; other Errors carry a message written for the user (e.g. "Payment was cancelled.").
  if (err instanceof Error && !(err instanceof TypeError)) return err.message
  return 'Could not reach Googols. Check your connection and try again.'
}
