export type PostStatus = 'Draft' | 'Published' | 'Inactive'

export interface Author {
  id: string
  handle: string
  displayName: string
  avatarUrl: string | null
  bio: string | null
}

export interface Tag {
  id: string
  name: string
  slug: string
  postCount: number
}

/** A shelf for posts. `postCount` is how many published posts are on it. */
export interface Category {
  id: string
  name: string
  slug: string
  postCount: number
}

export interface Site {
  theme: string
  /** The owner's own three colours, sent only when the theme is "custom". */
  colors?: { main: string; accent?: string; background: string } | null
  categories: Category[]
  /** False until the site can send email; the subscribe form is hidden until then. */
  subscribeEnabled: boolean
}

export interface PostSummary {
  id: string
  slug: string | null
  title: string
  subtitle: string | null
  excerpt: string
  coverImageUrl: string | null
  readingTimeMinutes: number
  clapCount: number
  insightfulCount: number
  commentCount: number
  status: PostStatus
  publishedAt: string | null
  author: Author
  tags: Tag[]
  category: { id: string; name: string; slug: string } | null
}

export interface PostDetail extends Omit<PostSummary, 'excerpt'> {
  contentJson: string
  viewCount: number
  updatedAt: string
}

export type ReactionKind = 'clap' | 'insightful'

/** A post's reaction totals, and which of them this browser has given. */
export interface ReactionState {
  clapCount: number
  insightfulCount: number
  clapped: boolean
  markedInsightful: boolean
}

export interface Profile {
  id: string
  handle: string
  displayName: string
  bio: string | null
  avatarUrl: string | null
  websiteUrl: string | null
  joinedAt: string
  postCount: number
}

export interface Paged<T> {
  items: T[]
  pageNumber: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNextPage: boolean
}

export type PostSort = 'Latest' | 'Popular' | 'Trending'
