import { useEffect } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { CopyLinkButton } from '../components/CopyLinkButton'
import { Reactions } from '../components/Reactions'
import { RichText } from '../components/RichText'
import { Avatar, EmptyState, Spinner, TagPill, formatDate } from '../components/ui'
import { useAsync } from '../hooks/useAsync'
import { useTitle } from '../hooks/useTitle'
import { safeImageSrc } from '../lib/safeUrl'
import { postPath, slugFromParams } from '../lib/site'

export function PostPage() {
  const slug = slugFromParams(useParams())
  const location = useLocation()
  const navigate = useNavigate()

  const loaded = useAsync(() => api.post(slug), [slug])
  const post = loaded.data
  useTitle(post?.title ?? null)

  // However the post was reached (an old /read/ link, a category it has since left), the address bar settles on its own.
  const canonical = post ? postPath(post.slug, post.category?.slug) : null
  useEffect(() => {
    if (canonical && decodeURIComponent(location.pathname) !== decodeURIComponent(canonical)) {
      navigate({ pathname: canonical, search: location.search, hash: location.hash }, { replace: true })
    }
  }, [canonical, location.pathname, location.search, location.hash, navigate])

  const postId = post?.id
  useEffect(() => {
    // Not awaited and never shown: counting a reader must not get in the way of reading.
    if (postId) api.recordView(postId).catch(() => {})
  }, [postId])

  const related = useAsync(() => (postId ? api.related(postId) : Promise.resolve([])), [postId])

  if (loaded.loading) return <main className="main"><Spinner /></main>
  if (loaded.error || !post) {
    return (
      <main className="main">
        <EmptyState title="This article isn't available">
          <p className="muted">{loaded.error ?? 'It may have been taken down.'}</p>
          <Link className="btn" to="/">Back to home</Link>
        </EmptyState>
      </main>
    )
  }

  const link = `${window.location.origin}${postPath(post.slug, post.category?.slug)}`
  const cover = safeImageSrc(post.coverImageUrl)

  const hasRelated = Boolean(related.data && related.data.length > 0)

  return (
    <main className="main main--post">
      <article className="read__article">
        {post.category && (
          <Link className="article__category" to={`/?category=${encodeURIComponent(post.category.slug)}`}>{post.category.name}</Link>
        )}
        <div className="article__title-row">
          <h1 className="article__title">{post.title}</h1>
          <CopyLinkButton url={link} compact />
        </div>
        {post.subtitle && <p className="article__subtitle">{post.subtitle}</p>}

        <div className="article__byline">
          <Avatar author={post.author} />
          <div style={{ flex: 1 }}>
            <Link to={`/@${post.author.handle}`}><strong>{post.author.displayName}</strong></Link>
            <div className="faint" style={{ fontSize: '0.85rem' }}>
              {formatDate(post.publishedAt)} · {post.readingTimeMinutes} min read
            </div>
          </div>
        </div>

        {cover && <img className="article__cover" src={cover} alt="" referrerPolicy="no-referrer" />}

        <RichText contentJson={post.contentJson} />

        {post.tags.length > 0 && (
          <div className="pill-row" style={{ marginTop: '2.5rem' }}>
            {post.tags.map((tag) => <TagPill key={tag.id} tag={tag} />)}
          </div>
        )}

        <div className="actionbar">
          <Reactions postId={post.id} clapCount={post.clapCount} insightfulCount={post.insightfulCount} />
          <span className="actionbar__spacer" />
          <CopyLinkButton url={link} />
        </div>
      </article>

      {/* Beside the article on a wide screen, where there would otherwise be empty margin; below it on a narrow one. */}
      <aside className="read__aside" aria-label="About this article">
        <section className="author-card">
          <Avatar author={post.author} large />
          <div>
            <Link className="author-card__name" to={`/@${post.author.handle}`}>{post.author.displayName}</Link>
            {post.author.bio && <p className="author-card__bio">{post.author.bio}</p>}
          </div>
        </section>

        {hasRelated && (
          <section>
            <h2 className="sidebar__title">More on this topic</h2>
            <ul className="related">
              {related.data!.map((item) => (
                <li key={item.id}>
                  <Link to={postPath(item.slug, item.category?.slug)}>{item.title}</Link>
                  <span className="faint">{item.readingTimeMinutes} min read</span>
                </li>
              ))}
            </ul>
          </section>
        )}
      </aside>
    </main>
  )
}
