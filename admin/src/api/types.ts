export interface Session {
  token: string
  expiresAt: string
  email: string
  displayName: string
}

export interface Paged<T> {
  items: T[]
  pageNumber: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNextPage: boolean
}

export interface TopPost {
  id: string
  title: string
  slug: string | null
  views: number
  claps: number
  comments: number
}

export interface DailyCount {
  date: string
  count: number
}

export interface Stats {
  publishedPosts: number
  draftPosts: number
  inactivePosts: number
  users: number
  comments: number
  claps: number
  insightful: number
  views: number
  bookmarks: number
  tags: number
  topByViews: TopPost[]
  topByClaps: TopPost[]
  publishedLast30Days: DailyCount[]
}

export type PostStatus = 'Draft' | 'Published' | 'Inactive'

export interface AdminPost {
  id: string
  title: string
  slug: string | null
  status: PostStatus
  authorHandle: string
  authorName: string
  publishedAt: string | null
  updatedAt: string
  views: number
  claps: number
  insightful: number
  comments: number
  tags: string[]
  category: string | null
  /** When subscribers were last emailed about this post, or null if never. */
  notifiedAt: string | null
  /** The category's id, or null for a post in no category. */
  categoryId: string | null
}

export interface Subscriber {
  id: string
  email: string
  status: 'Pending' | 'Confirmed' | 'Unsubscribed'
  createdAt: string
  confirmedAt: string | null
}

export interface SubscribersSummary {
  /** False until a mail service key and "from" address are set on the host. */
  emailConfigured: boolean
  confirmed: number
  pending: number
  unsubscribed: number
  dailyLimit: number
}

export interface NotifyResult {
  sent: number
  failed: number
  /** Confirmed subscribers who have still not been told about the post. */
  remaining: number
  notifiedAt: string | null
}

export interface PostTag {
  id: string
  name: string
  slug: string
  postCount: number
}

/** A post as its author sees it in the editor. */
export interface PostDraft {
  id: string
  slug: string | null
  title: string
  subtitle: string | null
  contentJson: string
  coverImageUrl: string | null
  status: PostStatus
  tags: PostTag[]
  category: { id: string; name: string; slug: string } | null
}

/** A picture stored by the API. `path` is what a post keeps; it is relative to the API. */
export interface StoredImage {
  id: string
  path: string
  contentType: string
  size: number
}

/** How the signed-in person appears to readers. */
export interface Profile {
  id: string
  email: string
  handle: string
  displayName: string
  bio: string | null
  avatarUrl: string | null
  websiteUrl: string | null
}

export interface ProfileInput {
  displayName: string
  bio: string | null
  avatarUrl: string | null
  websiteUrl: string | null
}

export interface ActivityEntry {
  id: string
  at: string
  actor: string
  action: string
  subject: string
}

export interface PostRevision {
  id: string
  title: string
  createdAt: string
}

export interface PostRevisionDetail extends PostRevision {
  contentJson: string
}

export interface PostInput {
  title: string
  subtitle: string | null
  contentJson: string
  coverImageUrl: string | null
  tags: string[]
  categoryId: string | null
}

/** A shelf for posts. `postCount` is how many published posts are on it. */
export interface Category {
  id: string
  name: string
  slug: string
  postCount: number
}

export interface SiteSettings {
  theme: string
  availableThemes: string[]
  /** The custom colours last saved, or a starting suggestion. In use only when `theme` is "custom". */
  colors: { main: string; accent: string; background: string }
}

export interface AdminComment {
  id: string
  body: string
  isDeleted: boolean
  isReply: boolean
  createdAt: string
  authorHandle: string
  postId: string
  postTitle: string
  postSlug: string | null
}

export interface AdminTag {
  id: string
  name: string
  slug: string
  postCount: number
  followers: number
}

export type Collection = 'blog' | 'projects' | 'updates'

export interface PortfolioStatus {
  configured: boolean
  repo: string | null
  branch: string | null
}

export interface PortfolioSummary {
  slug: string
  fileName: string
  title: string
  date: string | null
  draft: boolean
  sha: string
  problem: string | null
}

export type Frontmatter = Record<string, unknown>

export interface PortfolioEntry {
  collection: Collection
  slug: string
  sha: string
  frontmatter: Frontmatter
  body: string
}

/** Whether picture generation is set up, and how much of today's allowance is used. */
export interface GenerationStatus {
  enabled: boolean
  usedToday: number
  dailyLimit: number
}

/** A generated picture, as a preview. It is stored only if the writer keeps it. */
export interface GeneratedImage {
  imageBase64: string
  contentType: string
  usedToday: number
  dailyLimit: number
}
