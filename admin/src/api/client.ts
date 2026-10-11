import type { GrammarMatch } from '../posts/grammar'
import type {
  ActivityEntry, AdminComment, AdminPost, AdminTag, Category, Collection, NotifyResult, Subscriber, SubscribersSummary, Frontmatter, Paged, PortfolioEntry, PortfolioStatus,
  PortfolioSummary, PostDraft, PostInput, PostRevision, PostRevisionDetail, PostStatus, PostTag, Profile, ProfileInput,
  GeneratedImage, GenerationStatus, GlobalNote, ThemeSuggestion, Session, SiteSettings, Stats, StoredImage,
} from './types'

// Empty in development (Vite proxies /api); the production build bakes in the API's absolute URL.
const API_BASE = ((import.meta.env.VITE_API_BASE as string | undefined) ?? '').replace(/\/+$/, '')

const SESSION_KEY = 'articles.admin.session'

/** Where the browser fetches an uploaded picture from. */
export const imageUrl = (id: string) => `${API_BASE}/api/v1/images/${id}`

/** A stored picture's path is relative to the API; a link to a picture elsewhere is used as it is. */
export const assetUrl = (pathOrUrl: string) => (pathOrUrl.startsWith('/api/v1/images/') ? `${API_BASE}${pathOrUrl}` : pathOrUrl)

/**
 * The session lives in sessionStorage, not localStorage: it disappears when the tab closes, so a
 * forgotten browser does not stay signed in to the admin portal.
 */
export function loadSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY)
    if (!raw) return null
    const session = JSON.parse(raw) as Session
    return new Date(session.expiresAt).getTime() > Date.now() ? session : null
  } catch {
    return null
  }
}

export function saveSession(session: Session | null) {
  try {
    if (session) sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
    else sessionStorage.removeItem(SESSION_KEY)
  } catch {
    // Storage can be unavailable (private mode); the session then lasts until the page is reloaded.
  }
}

/**
 * The signed-in person's handle, read from the session token. Only used to decide which posts get
 * an Edit button; the API is what actually enforces that you can only edit your own.
 */
export function sessionHandle(session: Session | null): string | null {
  try {
    const payload = session?.token.split('.')[1]
    if (!payload) return null
    const claims = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as { handle?: unknown }
    return typeof claims.handle === 'string' ? claims.handle : null
  } catch {
    return null
  }
}

export class ApiError extends Error {
  status: number

  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

let onSessionLost: (() => void) | null = null

/** Called when the API says the session is no longer valid, so the app can return to the sign-in page. */
export function setSessionLostHandler(handler: (() => void) | null) {
  onSessionLost = handler
}

async function request<T>(method: string, path: string, body?: unknown, auth = true): Promise<T> {
  const session = loadSession()
  // A form (a file upload) sets its own content type, including the boundary between its parts.
  const isForm = body instanceof FormData

  const response = await fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      ...(body !== undefined && !isForm ? { 'Content-Type': 'application/json' } : {}),
      ...(auth && session ? { Authorization: `Bearer ${session.token}` } : {}),
    },
    body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
  })

  if (response.status === 204) return undefined as T

  const text = await response.text()
  let payload: { detail?: string; title?: string; errors?: Record<string, string[]> } | null = null
  try {
    payload = text ? JSON.parse(text) : null
  } catch {
    payload = null
  }

  if (!response.ok) {
    // 401/403 on an authenticated call means the session expired or admin access was removed.
    if (auth && (response.status === 401 || response.status === 403)) onSessionLost?.()

    const message = payload?.errors
      ? Object.values(payload.errors).flat()[0]
      : payload?.detail ?? payload?.title ?? `Request failed (${response.status})`
    throw new ApiError(message, response.status)
  }

  return payload as T
}

const query = (params: Record<string, string | number | undefined | null>) => {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

export const api = {
  login: (email: string, code: string) =>
    request<Session>('POST', '/api/v1/admin/auth/login', { email, code }, false),

  me: () => request<{ email: string; displayName: string }>('GET', '/api/v1/admin/me'),

  stats: () => request<Stats>('GET', '/api/v1/admin/stats'),

  activity: (pageNumber = 1) => request<Paged<ActivityEntry>>('GET', `/api/v1/admin/activity${query({ pageNumber, pageSize: 50 })}`),

  /** Every post as one object, for saving to a file. */
  exportPosts: () => request<{ postCount: number }>('GET', '/api/v1/admin/export'),

  subscribersSummary: () => request<SubscribersSummary>('GET', '/api/v1/admin/subscribers/summary'),
  subscribers: (pageNumber = 1) => request<Paged<Subscriber>>('GET', `/api/v1/admin/subscribers${query({ pageNumber, pageSize: 50 })}`),
  removeSubscriber: (id: string) => request<void>('DELETE', `/api/v1/admin/subscribers/${id}`),
  /** How many confirmed subscribers have not yet been told about this post. */
  notifyWaiting: (postId: string) => request<{ waiting: number }>('GET', `/api/v1/admin/posts/${postId}/notify`),
  notifyPost: (postId: string) => request<NotifyResult>('POST', `/api/v1/admin/posts/${postId}/notify`),

  settings: () => request<SiteSettings>('GET', '/api/v1/admin/settings'),
  updateSettings: (theme: string, colors?: { main: string; accent: string; background: string }) =>
    request<SiteSettings>('PUT', '/api/v1/admin/settings', { theme, ...colors }),

  globalNote: () => request<GlobalNote>('GET', '/api/v1/admin/global-note'),
  saveGlobalNote: (text: string) => request<GlobalNote>('PUT', '/api/v1/admin/global-note', { text }),

  themeAiStatus: () => request<GenerationStatus>('GET', '/api/v1/admin/settings/ai-colours'),
  askAiForColours: () => request<ThemeSuggestion>('POST', '/api/v1/admin/settings/ai-colours'),

  categories: () => request<Category[]>('GET', '/api/v1/admin/categories'),
  createCategory: (name: string) => request<Category>('POST', '/api/v1/admin/categories', { name }),
  renameCategory: (id: string, name: string) => request<Category>('PUT', `/api/v1/admin/categories/${id}`, { name }),
  deleteCategory: (id: string) => request<void>('DELETE', `/api/v1/admin/categories/${id}`),

  profile: () => request<Profile>('GET', '/api/v1/auth/me'),
  updateProfile: (body: ProfileInput) => request<Profile>('PUT', '/api/v1/users/me', body),

  /** `category` is nothing for all posts, "none" for posts in no category, or a category id. */
  posts: (params: { status?: string; q?: string; category?: string; pageNumber?: number; pageSize?: number }) =>
    request<Paged<AdminPost>>('GET', `/api/v1/admin/posts${query(params)}`),
  /** Any post, whoever wrote it. */
  adminSetPostStatus: (id: string, status: PostStatus) => request<void>('POST', `/api/v1/admin/posts/${id}/status`, { status }),
  /** Moves any post to a category, or out of its category when `categoryId` is null. Changes nothing else. */
  adminSetPostCategory: (id: string, categoryId: string | null) => request<void>('POST', `/api/v1/admin/posts/${id}/category`, { categoryId }),
  deletePost: (id: string) => request<void>('DELETE', `/api/v1/admin/posts/${id}`),

  // Writing uses the same routes as the public site: these act on your own posts only.
  postForEdit: (id: string) => request<PostDraft>('GET', `/api/v1/posts/${id}/edit`),
  createPost: (body: PostInput) => request<PostDraft>('POST', '/api/v1/posts', body),
  updatePost: (id: string, body: PostInput) => request<PostDraft>('PUT', `/api/v1/posts/${id}`, body),
  uploadImage: (file: Blob, name: string) => {
    const form = new FormData()
    form.append('file', file, name)
    return request<StoredImage>('POST', '/api/v1/images', form)
  },
  imageGenerationStatus: () => request<GenerationStatus>('GET', '/api/v1/admin/images/generation'),
  generateImage: (prompt: string) => request<GeneratedImage>('POST', '/api/v1/admin/images/generate', { prompt }),
  /** Spelling and grammar suggestions for plain text. */
  checkGrammar: (text: string) => request<{ matches: GrammarMatch[] }>('POST', '/api/v1/admin/grammar/check', { text }),
  revisions: (id: string) => request<PostRevision[]>('GET', `/api/v1/posts/${id}/revisions`),
  revision: (id: string, revisionId: string) => request<PostRevisionDetail>('GET', `/api/v1/posts/${id}/revisions/${revisionId}`),
  suggestTags: (q: string) => request<PostTag[]>('GET', `/api/v1/tags/suggest${query({ q })}`),

  comments: (pageNumber = 1) => request<Paged<AdminComment>>('GET', `/api/v1/admin/comments${query({ pageNumber, pageSize: 25 })}`),
  deleteComment: (id: string) => request<void>('DELETE', `/api/v1/admin/comments/${id}`),

  tags: () => request<AdminTag[]>('GET', '/api/v1/admin/tags'),
  renameTag: (id: string, name: string) => request<AdminTag>('PUT', `/api/v1/admin/tags/${id}`, { name }),
  mergeTag: (id: string, targetTagId: string) => request<void>('POST', `/api/v1/admin/tags/${id}/merge`, { targetTagId }),
  deleteTag: (id: string) => request<void>('DELETE', `/api/v1/admin/tags/${id}`),

  portfolioStatus: () => request<PortfolioStatus>('GET', '/api/v1/admin/portfolio/status'),
  portfolioList: (collection: Collection) => request<PortfolioSummary[]>('GET', `/api/v1/admin/portfolio/${collection}`),
  portfolioGet: (collection: Collection, slug: string) =>
    request<PortfolioEntry>('GET', `/api/v1/admin/portfolio/${collection}/${encodeURIComponent(slug)}`),
  portfolioSave: (collection: Collection, slug: string, sha: string | null, frontmatter: Frontmatter, body: string) =>
    request<PortfolioEntry>('PUT', `/api/v1/admin/portfolio/${collection}/${encodeURIComponent(slug)}`, { sha, frontmatter, body }),
  portfolioDelete: (collection: Collection, slug: string, sha: string) =>
    request<void>('DELETE', `/api/v1/admin/portfolio/${collection}/${encodeURIComponent(slug)}${query({ sha })}`),
}
