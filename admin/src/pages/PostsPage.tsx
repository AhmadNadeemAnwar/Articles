import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, sessionHandle } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import type { AdminPost, PostStatus } from '../api/types'
import { useAsync } from '../hooks/useAsync'
import { useConfirm, useToast } from '../components/feedback'
import { Empty, ErrorNote, PageHeader, Pagination, Spinner, formatDate, formatNumber, publicPostUrl, statusLabels } from '../components/ui'
import { describeNotifyResult } from './SubscribersPage'
import { NO_CATEGORY, categoryChoice, categoryParam, isFiltering, movedMessage } from '../posts/categoryMove'

const STATUSES: PostStatus[] = ['Draft', 'Published', 'Inactive']

/** Hands the browser a file to save. The address is released straight away so it does not linger. */
function saveAsFile(name: string, text: string) {
  const url = URL.createObjectURL(new Blob([text], { type: 'application/json' }))
  const link = document.createElement('a')
  link.href = url
  link.download = name
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

/** " and its comment" / " and all 3 of its comments" / nothing when there are none. */
export function commentsClause(count: number): string {
  if (count <= 0) return ''
  return count === 1 ? ' and its comment' : ` and all ${formatNumber(count)} of its comments`
}

export function PostsPage() {
  const confirm = useConfirm()
  const notify = useToast()
  const myHandle = sessionHandle(useAuth().session)

  const [status, setStatus] = useState('')
  const [category, setCategory] = useState('')
  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)

  async function exportAll() {
    setExporting(true)
    try {
      const data = await api.exportPosts()
      saveAsFile(`articles-posts-${new Date().toISOString().slice(0, 10)}.json`, JSON.stringify(data, null, 2))
      notify(`Saved ${formatNumber(data.postCount)} ${data.postCount === 1 ? 'post' : 'posts'} to your downloads.`)
    } catch (err) {
      notify(err instanceof Error ? err.message : 'The export did not work.', 'error')
    } finally {
      setExporting(false)
    }
  }

  // Wait for a pause in typing before searching, so each keystroke is not a request.
  useEffect(() => {
    const timer = setTimeout(() => { setTerm(search.trim()); setPage(1) }, 300)
    return () => clearTimeout(timer)
  }, [search])

  const posts = useAsync(() => api.posts({ status, q: term, category: categoryParam(category), pageNumber: page, pageSize: 20 }), [status, term, category, page])
  // The list of categories feeds the filter and each row's dropdown; if it cannot be loaded the rows just show the current one.
  const categories = useAsync(() => api.categories().catch(() => []), [])
  const shelves = categories.data ?? []
  // The Notify button only appears once email is set up; if this cannot be loaded it simply stays hidden.
  const mail = useAsync(() => api.subscribersSummary().catch(() => null), [])

  async function notifySubscribers(post: AdminPost) {
    setBusyId(post.id)
    try {
      const { waiting } = await api.notifyWaiting(post.id)
      if (waiting === 0) {
        notify(post.notifiedAt ? 'Every subscriber has already been told about this post.' : 'There are no confirmed subscribers yet.', 'error')
        return
      }

      const ok = await confirm({
        title: 'Email subscribers about this post?',
        message: (
          <>
            {formatNumber(waiting)} {waiting === 1 ? 'subscriber gets' : 'subscribers get'} one email with a link to “{post.title}”.
            {post.notifiedAt && <> Those already told on {formatDate(post.notifiedAt)} are not emailed again.</>} This cannot be undone.
          </>
        ),
        confirmLabel: waiting === 1 ? 'Send 1 email' : `Send ${formatNumber(waiting)} emails`,
      })
      if (!ok) return

      const outcome = describeNotifyResult(await api.notifyPost(post.id))
      notify(outcome.message, outcome.kind)
      posts.reload()
    } catch (err) {
      notify(err instanceof Error ? err.message : 'The emails could not be sent.', 'error')
    } finally {
      setBusyId(null)
    }
  }

  async function changeStatus(post: AdminPost, target: PostStatus) {
    if (target === post.status) return

    const ok = await confirm(
      target === 'Published'
        ? { title: 'Publish this post?', message: <>“{post.title}” goes live on the site straight away{post.slug ? ', at the same address as before' : ''}.</>, confirmLabel: 'Publish' }
        : post.status === 'Published'
          ? {
              title: 'Take this post down?',
              message: <>“{post.title}” comes off the site at once and becomes {statusLabels[target].toLowerCase()}. Nothing is deleted, and it keeps its address for when it is published again.</>,
              confirmLabel: 'Take down',
            }
          : { title: `Change to ${statusLabels[target].toLowerCase()}?`, message: <>“{post.title}” stays hidden from readers either way.</>, confirmLabel: 'Change' },
    )
    if (!ok) return

    await act(post, () => api.adminSetPostStatus(post.id, target), target === 'Published' ? 'Post published.' : `Post is now ${statusLabels[target].toLowerCase()}.`)
  }

  async function changeCategory(post: AdminPost, value: string) {
    const target = categoryChoice(value)
    if (target === post.categoryId) return
    const name = target === null ? null : shelves.find((c) => c.id === target)?.name ?? 'that category'
    await act(post, () => api.adminSetPostCategory(post.id, target), movedMessage(post.title, name))
  }

  async function remove(post: AdminPost) {
    const ok = await confirm({
      title: 'Delete this post for good?',
      message: <>“{post.title}”{commentsClause(post.comments)} will be permanently deleted. This cannot be undone.</>,
      confirmLabel: 'Delete permanently',
      danger: true,
    })
    if (!ok) return

    await act(post, () => api.deletePost(post.id), 'Post deleted.')
  }

  async function act(post: AdminPost, action: () => Promise<void>, success: string) {
    setBusyId(post.id)
    try {
      await action()
      notify(success)
      posts.reload()
    } catch (err) {
      notify(err instanceof Error ? err.message : 'That did not work.', 'error')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <>
      <PageHeader title="Posts">
        <button className="btn" onClick={exportAll} disabled={exporting} title="Save every post, including drafts, to a file on this device">
          {exporting ? 'Exporting…' : 'Export all'}
        </button>
        <Link className="btn btn--primary" to="/posts/new">New post</Link>
      </PageHeader>

      <div className="filters">
        <input type="search" placeholder="Search title or author" aria-label="Search posts" value={search} onChange={(e) => setSearch(e.target.value)} />
        <select aria-label="Filter by status" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1) }}>
          <option value="">All statuses</option>
          <option value="Published">Published</option>
          <option value="Draft">Drafts</option>
          <option value="Inactive">Not active</option>
        </select>
        <select aria-label="Filter by category" value={category} onChange={(e) => { setCategory(e.target.value); setPage(1) }}>
          <option value="">All categories</option>
          <option value={NO_CATEGORY}>No category</option>
          {shelves.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
      </div>

      {posts.error && <ErrorNote message={posts.error} onRetry={posts.reload} />}
      {posts.loading && !posts.data && <Spinner />}

      {posts.data && (posts.data.items.length === 0 ? (
        <Empty title={isFiltering({ status, term, category }) ? 'No posts match' : 'No posts yet'}>
          {isFiltering({ status, term, category }) ? 'Try a different search, status or category.' : <Link className="btn btn--primary" to="/posts/new">Write your first post</Link>}
        </Empty>
      ) : (
        <>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Title</th><th>Author</th><th>Status</th><th>Category</th><th>Published</th>
                  <th className="num">Views</th><th className="num">Claps</th><th className="num">Insightful</th><th></th>
                </tr>
              </thead>
              <tbody>
                {posts.data.items.map((post) => {
                  const url = publicPostUrl(post.slug)
                  return (
                    <tr key={post.id} className={busyId === post.id ? 'is-busy' : undefined}>
                      <td>
                        <div className="cell-title">{url && post.status === 'Published' ? <a href={url} target="_blank" rel="noopener noreferrer">{post.title}</a> : post.title}</div>
                        {post.tags.length > 0 && <div className="cell-sub">{post.tags.join(' · ')}</div>}
                      </td>
                      <td>@{post.authorHandle}</td>
                      <td>
                        <select className={`status-select status-select--${post.status.toLowerCase()}`} aria-label={`Status of ${post.title}`}
                          value={post.status} disabled={busyId === post.id} onChange={(e) => changeStatus(post, e.target.value as PostStatus)}>
                          {STATUSES.map((value) => <option key={value} value={value}>{statusLabels[value]}</option>)}
                        </select>
                      </td>
                      <td>
                        <select className="category-select" aria-label={`Category of ${post.title}`} value={post.categoryId ?? ''}
                          disabled={busyId === post.id || categories.loading} onChange={(e) => changeCategory(post, e.target.value)}>
                          <option value="">No category</option>
                          {shelves.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                          {post.categoryId && !shelves.some((c) => c.id === post.categoryId) && <option value={post.categoryId}>{post.category ?? '(current category)'}</option>}
                        </select>
                      </td>
                      <td>
                        {formatDate(post.publishedAt)}
                        {post.notifiedAt && <div className="cell-sub">Emailed {formatDate(post.notifiedAt)}</div>}
                      </td>
                      <td className="num">{formatNumber(post.views)}</td>
                      <td className="num">{formatNumber(post.claps)}</td>
                      <td className="num">{formatNumber(post.insightful)}</td>
                      <td className="actions">
                        {post.status === 'Published' && mail.data?.emailConfigured && (
                          <button className="btn btn--small" onClick={() => notifySubscribers(post)} disabled={busyId === post.id}
                            title="Email subscribers a link to this post">
                            {post.notifiedAt ? 'Notify again' : 'Notify subscribers'}
                          </button>
                        )}
                        {post.authorHandle === myHandle && <Link className="btn btn--small" to={`/posts/${post.id}/edit`}>Edit</Link>}
                        <button className="btn btn--small btn--danger" onClick={() => remove(post)} disabled={busyId === post.id}>Delete</button>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          <p className="muted small">{formatNumber(posts.data.totalCount)} {posts.data.totalCount === 1 ? 'post' : 'posts'}</p>
          <Pagination page={page} totalPages={posts.data.totalPages} onChange={setPage} />
        </>
      ))}
    </>
  )
}
