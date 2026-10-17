import { useState } from 'react'
import { api } from '../api/client'
import type { ViewShare } from '../api/types'
import { useAsync } from '../hooks/useAsync'
import { BarChart } from '../components/BarChart'
import { ErrorNote, PageHeader, Spinner, StatTile, formatNumber, publicPostUrl } from '../components/ui'

export const PERIODS = [7, 30, 90] as const

let countryNames: Intl.DisplayNames | null = null

/** "Malaysia" for "MY"; "Not known" when the country could not be told. */
export function countryLabel(code: string): string {
  if (!code) return 'Not known'
  try {
    countryNames ??= new Intl.DisplayNames(['en'], { type: 'region' })
    return countryNames.of(code) ?? code
  } catch {
    return code
  }
}

export const deviceLabel = (key: string) => ({ phone: 'Phone', tablet: 'Tablet', computer: 'Computer' } as Record<string, string>)[key] ?? (key || 'Not known')

export const sourceLabel = (key: string) => key || 'Direct, or from within your sites'

/** A whole-number share of the total, as a percentage; 0 when there is nothing to share. */
export function percent(part: number, total: number): number {
  return total <= 0 ? 0 : Math.round((part / total) * 100)
}

/** What the readers chart says in words. */
export function readersSummary(days: { count: number }[], returning: number): string {
  const total = days.reduce((sum, d) => sum + d.count, 0)
  if (total === 0) return `No readers in the last ${days.length} days yet.`
  const busiest = Math.max(...days.map((d) => d.count))
  return `${formatNumber(total)} readers in the last ${days.length} days, ${formatNumber(returning)} of them returning; the busiest day had ${formatNumber(busiest)}.`
}

/**
 * Readers per day, for one post or all of them, and the coarse facts about them: country, device and where they came
 * from. Everything is a count; nothing here identifies a reader.
 */
export function ViewsPage() {
  const [postId, setPostId] = useState('')
  const [days, setDays] = useState<number>(30)
  const posts = useAsync(() => api.posts({ status: 'Published', pageSize: 100 }), [])
  const stats = useAsync(() => api.views(postId || null, days), [postId, days])

  const total = stats.data?.views ?? 0

  return (
    <>
      <PageHeader title="Views" />
      <p className="muted">
        Readers per day, counted once a day per reader. Country, device and where readers came from are kept as totals only:
        never an address, a name or a full link. Daily figures start on 17 October 2026; readers before that are in the all-time total.
      </p>

      <div className="filters">
        <select aria-label="Which post" value={postId} onChange={(e) => setPostId(e.target.value)}>
          <option value="">All posts</option>
          {posts.data?.items.map((post) => <option key={post.id} value={post.id}>{post.title}</option>)}
        </select>
        <div className="size-pick" role="radiogroup" aria-label="Period">
          {PERIODS.map((p) => (
            <button key={p} type="button" role="radio" aria-checked={days === p}
              className={`size-pick__option${days === p ? ' size-pick__option--on' : ''}`} onClick={() => setDays(p)}>
              {p} days
            </button>
          ))}
        </div>
      </div>

      {stats.error && <ErrorNote message={stats.error} onRetry={stats.reload} />}
      {stats.loading && !stats.data && <Spinner />}

      {stats.data && (
        <>
          <div className="tiles">
            <StatTile label="All time" value={formatNumber(stats.data.allTime)} hint="every reader ever counted" />
            <StatTile label="Readers" value={formatNumber(stats.data.views)} hint={`last ${stats.data.days} days`} />
            <StatTile label="New readers" value={formatNumber(stats.data.views - stats.data.returning)} hint="first time on that post" />
            <StatTile label="Returning" value={formatNumber(stats.data.returning)} hint={`${percent(stats.data.returning, total)}% came back another day`} />
          </div>

          <section className="panel">
            <h2>Readers per day</h2>
            <BarChart days={stats.data.daily.map((d) => ({ date: d.date, count: d.views }))} what="readers"
              summary={readersSummary(stats.data.daily.map((d) => ({ count: d.views })), stats.data.returning)} />
          </section>

          <div className="two-col">
            <Breakdown title="Countries" rows={stats.data.countries} total={total} label={countryLabel} />
            <Breakdown title="Devices" rows={stats.data.devices} total={total} label={deviceLabel} />
            <Breakdown title="Where they came from" rows={stats.data.sources} total={total} label={sourceLabel} />
            {stats.data.topPosts.length > 0 && (
              <section className="panel">
                <h2>Most read</h2>
                <ol className="toplist">
                  {stats.data.topPosts.map((post) => {
                    const url = publicPostUrl(post.slug)
                    return (
                      <li key={post.postId}>
                        {url ? <a href={url} target="_blank" rel="noopener noreferrer">{post.title}</a> : <span>{post.title}</span>}
                        <span className="muted">{formatNumber(post.views)} {post.views === 1 ? 'reader' : 'readers'}</span>
                      </li>
                    )
                  })}
                </ol>
              </section>
            )}
          </div>
        </>
      )}
    </>
  )
}

function Breakdown({ title, rows, total, label }: { title: string; rows: ViewShare[]; total: number; label: (key: string) => string }) {
  return (
    <section className="panel">
      <h2>{title}</h2>
      {rows.length === 0 ? (
        <p className="muted">No readers in this period yet.</p>
      ) : (
        <ul className="shares">
          {rows.map((row) => (
            <li key={row.key || '-'}>
              <span className="shares__label">{label(row.key)}</span>
              <span className="shares__bar" aria-hidden="true"><span style={{ width: `${percent(row.views, total)}%` }} /></span>
              <span className="shares__value">{formatNumber(row.views)} · {percent(row.views, total)}%</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
