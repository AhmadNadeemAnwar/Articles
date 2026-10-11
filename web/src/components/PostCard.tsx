import { Link } from 'react-router-dom'
import type { PostSummary } from '../api/types'
import { postPath } from '../lib/site'
import { Avatar, ErrorNote, Spinner, TagPill, formatDate } from './ui'

export function PostCard({ post }: { post: PostSummary }) {
  const href = postPath(post.slug, post.category?.slug)

  return (
    <article className="post-card">
      <div className="post-card__byline">
        <Avatar author={post.author} />
        <Link to={`/@${post.author.handle}`}>{post.author.displayName}</Link>
        {post.publishedAt && <span className="faint">· {formatDate(post.publishedAt)}</span>}
        {post.category && <Link className="post-card__category" to={`/?category=${encodeURIComponent(post.category.slug)}`}>{post.category.name}</Link>}
      </div>

      <Link to={href}>
        <h2 className="post-card__title">{post.title}</h2>
        <p className="post-card__excerpt">{post.excerpt}</p>
      </Link>

      <div className="post-card__meta">
        <span>{post.readingTimeMinutes} min read</span>
        {post.clapCount > 0 && <span>· {post.clapCount} {post.clapCount === 1 ? 'clap' : 'claps'}</span>}
        {post.insightfulCount > 0 && <span>· {post.insightfulCount} found it insightful</span>}
        {post.tags.length > 0 && (
          <span className="pill-row" style={{ marginLeft: 'auto' }}>
            {post.tags.slice(0, 2).map((tag) => <TagPill key={tag.id} tag={tag} />)}
          </span>
        )}
      </div>
    </article>
  )
}

export function PostList({ posts, emptyLabel }: { posts: PostSummary[]; emptyLabel: string }) {
  if (posts.length === 0) {
    return <div className="empty"><p>{emptyLabel}</p></div>
  }

  return (
    <div className="post-list">
      {posts.map((post) => <PostCard key={post.id} post={post} />)}
    </div>
  )
}

interface Paged {
  posts: PostSummary[]
  loading: boolean
  loadingMore: boolean
  error: string | null
  hasMore: boolean
  loadMore: () => void
}

/** A list that starts with one page and grows when the reader asks for more. */
export function PagedPostList({ list, emptyLabel }: { list: Paged; emptyLabel: string }) {
  if (list.loading) return <Spinner />
  // A failure on the first page has nothing to show; a failure on a later page keeps what is there.
  if (list.error && list.posts.length === 0) return <ErrorNote message={list.error} />

  return (
    <>
      <PostList posts={list.posts} emptyLabel={emptyLabel} />
      {list.error && <ErrorNote message={list.error} />}
      {list.hasMore && (
        <div className="load-more">
          <button className="btn" onClick={list.loadMore} disabled={list.loadingMore}>
            {list.loadingMore ? 'Loading…' : 'Load more'}
          </button>
        </div>
      )}
    </>
  )
}
