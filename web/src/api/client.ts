import { getVisitorId } from '../lib/visitor'
import type { Paged, PostDetail, PostSort, PostSummary, Profile, ReactionKind, ReactionState, Site, Tag } from './types'

// Empty in development, where Vite proxies /api to the local API. In production the SPA and the
// API live on different origins, so the build bakes in the API's absolute URL.
export const API_BASE = ((import.meta.env.VITE_API_BASE as string | undefined) ?? '').replace(/\/+$/, '')

/** Carries the HTTP status so callers can distinguish "not found" from "not allowed". */
export class ApiError extends Error {
  status: number

  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

/**
 * Readers have no accounts, so no request carries a sign-in token.
 *
 * @param asVisitor Sends this browser's random visitor id. Only reactions and view counting need
 * it, so it is left off everything else and ordinary reads stay identical for every reader.
 */
async function request<T>(method: string, path: string, asVisitor = false, body?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      ...(asVisitor ? { 'X-Visitor-Id': getVisitorId() } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  // 202: accepted with nothing to say, as when a confirmation email has been sent.
  if (response.status === 202) return undefined as T

  if (response.status === 204) return undefined as T

  const text = await response.text()
  let payload: { detail?: string; title?: string; errors?: Record<string, string[]> } | null = null
  try {
    payload = text ? JSON.parse(text) : null
  } catch {
    // A sleeping or failing server can answer with a plain error page instead of JSON.
    payload = null
  }

  if (!response.ok) {
    // The API returns RFC 7807 problem details; validation failures add an `errors` map.
    const message = payload?.errors
      ? Object.values(payload.errors).flat()[0]
      : payload?.detail ?? payload?.title ?? `Request failed (${response.status})`

    throw new ApiError(message, response.status)
  }

  return payload as T
}

function query(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const qs = search.toString()
  return qs ? `?${qs}` : ''
}

export interface PostFilters {
  q?: string
  tag?: string
  author?: string
  category?: string
  sort?: PostSort
  pageNumber?: number
  pageSize?: number
}

export const api = {
  /** The theme the owner chose and the categories posts are shelved in. */
  site: () => request<Site>('GET', '/api/v1/site'),

  posts: (filters: PostFilters = {}) =>
    request<Paged<PostSummary>>('GET', `/api/v1/posts${query({ ...filters })}`),

  post: (slug: string) => request<PostDetail>('GET', `/api/v1/posts/${encodeURIComponent(slug)}`),

  related: (id: string, limit = 4) =>
    request<PostSummary[]>('GET', `/api/v1/posts/${id}/related${query({ limit })}`),

  reactions: (id: string) => request<ReactionState>('GET', `/api/v1/posts/${id}/reactions`, true),

  /** Gives the reaction, or takes it back if this browser already gave it. */
  /** A reader's anonymous note after reacting: what they liked, and any suggestion. */
  sendFeedback: (id: string, liked: string[], text: string) =>
    request<void>('POST', `/api/v1/posts/${id}/feedback`, true, { liked, text }),

  toggleReaction: (id: string, kind: ReactionKind) =>
    request<ReactionState>('POST', `/api/v1/posts/${id}/reactions/${kind}`, true),

  /** Tells the API a reader has this post open. Counted at most once per browser per day. */
  recordView: (id: string) => request<void>('POST', `/api/v1/posts/${id}/view`, true),

  /** Sends a confirmation email. The answer is the same whether or not the address was already subscribed. */
  subscribe: (email: string) => request<void>('POST', '/api/v1/subscribers', false, { email }),
  confirmSubscription: (token: string) => request<void>('POST', '/api/v1/subscribers/confirm', false, { token }),
  unsubscribe: (token: string) => request<void>('POST', '/api/v1/subscribers/unsubscribe', false, { token }),

  profile: (handle: string) => request<Profile>('GET', `/api/v1/users/${encodeURIComponent(handle)}`),

  popularTags: (limit = 20) => request<Tag[]>('GET', `/api/v1/tags${query({ limit })}`),
}
