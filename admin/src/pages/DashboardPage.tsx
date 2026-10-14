import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { DailyCount, TopPost } from '../api/types'
import { useAsync } from '../hooks/useAsync'
import { ErrorNote, PageHeader, Spinner, StatTile, formatNumber, publicPostUrl } from '../components/ui'

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
            <BarChart days={stats.data.publishedLast30Days} />
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

/** A plain SVG chart: no library, and a text summary for screen readers since the bars are not readable by them. */
function BarChart({ days }: { days: DailyCount[] }) {
  const max = Math.max(1, ...days.map((d) => d.count))
  const total = days.reduce((sum, d) => sum + d.count, 0)
  const width = 600
  const height = 120
  const slot = width / days.length

  return (
    <figure className="chart">
      <svg viewBox={`0 0 ${width} ${height + 18}`} role="img" aria-label={`${total} posts published in the last 30 days`} preserveAspectRatio="none">
        {days.map((day, i) => {
          const barHeight = day.count === 0 ? 2 : Math.max(6, (day.count / max) * height)
          return (
            <rect
              key={day.date}
              x={i * slot + 2}
              y={height - barHeight}
              width={Math.max(slot - 4, 2)}
              height={barHeight}
              rx={2}
              className={day.count === 0 ? 'bar bar--empty' : 'bar'}
            >
              <title>{`${day.date}: ${day.count} published`}</title>
            </rect>
          )
        })}
        <text x={0} y={height + 14} className="axis">{days[0]?.date}</text>
        <text x={width} y={height + 14} textAnchor="end" className="axis">{days[days.length - 1]?.date}</text>
      </svg>
      <figcaption className="muted">{total === 0 ? 'No posts published in this period.' : `${total} published in the last ${days.length} days; the busiest day had ${max}.`}</figcaption>
    </figure>
  )
}
