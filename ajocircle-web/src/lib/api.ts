/**
 * Central API client. The session is an HttpOnly cookie; every unsafe request carries the CSRF header
 * the server requires. Errors are normalized from RFC 7807 ProblemDetails.
 */
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, code?: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
  }

  /** All messages, flattened — useful for a summary alert. */
  get details(): string[] {
    return Object.values(this.fieldErrors).flat()
  }
}

const BASE = '/api/v1'

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  let res: Response
  try {
    res = await fetch(BASE + path, {
      method,
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(method !== 'GET' ? { 'X-AjoCircle-Request': '1' } : {}),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    })
  } catch {
    throw new ApiError(0, 'We could not reach the server. Check your connection and try again.', 'network')
  }

  if (res.status === 204 || res.status === 202) return undefined as T
  const text = await res.text()
  const data = text ? safeJson(text) : undefined

  if (!res.ok) {
    const p = (data ?? {}) as { title?: string; detail?: string; code?: string; errors?: Record<string, string[]> }
    const fallback =
      res.status === 401 ? 'Please sign in to continue.'
      : res.status === 403 ? 'You do not have permission to do that.'
      : res.status === 404 ? 'We could not find that.'
      : res.status === 429 ? 'Too many attempts. Please wait a minute and try again.'
      : 'Something went wrong. Please try again.'
    throw new ApiError(res.status, p.title ?? p.detail ?? fallback, p.code, p.errors ?? {})
  }
  return data as T
}

function safeJson(text: string): unknown {
  try { return JSON.parse(text) } catch { return undefined }
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  patch: <T>(path: string, body: unknown) => request<T>('PATCH', path, body),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  del: <T>(path: string) => request<T>('DELETE', path),
}
