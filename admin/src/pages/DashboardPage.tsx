import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { TopPost } from '../api/types'
import { useAsync } from '../hooks/useAsync'
import { ErrorNote, PageHeader, Spinner, StatTile, formatNumber, publicPostUrl } from '../components/ui'
import { BarChart } from '../components/BarChart'

export function DashboardPage() {
  const stats = useAsync(() => api.stats(), [])
  // Shown only once email is set up; if it cannot be loaded the tile is simply left out.
  const mail = useAsync(() => api.subscribersSummary().catch(() => null), [])

  return (
    <>
      <PageHeader title="Dashboard">
        <button className="btn" onClick={stats.reload} disabled={stats.loading}>Refresh</button>
        <Link className="btn btn--primary" to="/posts/new">New post</Link>
      </PageHeader>

      {stats.error && <ErrorNote message={stats.error} onRetry={stats.reload} />}
      {stats.loading && !stats.data && <Spinner />}

      {stats.data && (
        <>
          <section className="tiles" aria-label="Totals">
            <StatTile label="Published posts" value={formatNumber(stats.data.publishedPosts)} />
            <StatTile label="Drafts" value={formatNumber(stats.data.draftPosts)} hint={stats.data.inactivePosts ? `${stats.data.inactivePosts} not active` : undefined} />
            <StatTile label="Views" value={formatNumber(stats.data.views)} hint="one per reader per day" />
            <StatTile label="Claps" value={formatNumber(stats.data.claps)} hint="one per reader" />
            <StatTile label="Insightful" value={formatNumber(stats.data.insightful)} hint="one per reader" />
            <StatTile label="Topics" value={formatNumber(stats.data.tags)} />
            {mail.data?.emailConfigured && <StatTile label="Subscribers" value={formatNumber(mail.data.confirmed)} hint="confirmed by email" />}
            {/* Readers can no longer comment; the number only appears while older comments still exist. */}
            {stats.data.comments > 0 && <StatTile label="Comments" value={formatNumber(stats.data.comments)} hint="from before comments were closed" />}
          </section>

          <section className="panel">
            <h2>Posts published, last 30 days</h2>
            <BarChart days={stats.data.publishedLast30Days} what="published" summary={publishedSummary(stats.data.publishedLast30Days)} />
          </section>

          <div className="two-col">
            <TopList title="Most viewed" posts={stats.data.topByViews} metric="views" />
            <TopList title="Most clapped" posts={stats.data.topByClaps} metric="claps" />
          </div>
        </>
      )}
    </>
  )
}

/** "1 view", "14 views", "1 clap". */
/** What the publishing chart says in words, for everyone and for screen readers. */
export function publishedSummary(days: { count: number }[]): string {
  const total = days.reduce((sum, d) => sum + d.count, 0)
  const max = Math.max(0, ...days.map((d) => d.count))
  return total === 0 ? 'No posts published in this period.' : `${total} published in the last ${days.length} days; the busiest day had ${max}.`
}

export function countLabel(count: number, metric: 'views' | 'claps'): string {
  return `${formatNumber(count)} ${count === 1 ? metric.slice(0, -1) : metric}`
}

/** Only posts that have any of the metric belong in a "most" list; a row of zeros says nothing. */
export function withAny(posts: TopPost[], metric: 'views' | 'claps'): TopPost[] {
  return posts.filter((post) => post[metric] > 0)
}

function TopList({ title, posts, metric }: { title: string; posts: TopPost[]; metric: 'views' | 'claps' }) {
  const shown = withAny(posts, metric)
  return (
    <section className="panel">
      <h2>{title}</h2>
      {shown.length === 0 ? (
        <p className="muted">{metric === 'views' ? 'No views yet.' : 'No claps yet.'}</p>
      ) : (
        <ol className="toplist">
          {shown.map((post) => {
            const url = publicPostUrl(post.slug)
            return (
              <li key={post.id}>
                {url ? <a href={url} target="_blank" rel="noopener noreferrer">{post.title}</a> : <span>{post.title}</span>}
                <span className="muted">{countLabel(post[metric], metric)}</span>
              </li>
            )
          })}
        </ol>
      )}
    </section>
  )
}

