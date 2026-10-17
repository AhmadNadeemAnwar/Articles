import type { DailyCount } from '../api/types'

/**
 * A plain SVG bar chart of one number per day: no library, and a text summary for screen readers since the bars are not
 * readable by them. `what` names one counted thing in the tooltips ("published", "readers").
 */
export function BarChart({ days, what, summary }: { days: DailyCount[]; what: string; summary: string }) {
  const max = Math.max(1, ...days.map((d) => d.count))
  const width = 600
  const height = 120
  const slot = width / Math.max(1, days.length)

  return (
    <figure className="chart">
      <svg viewBox={`0 0 ${width} ${height + 18}`} role="img" aria-label={summary} preserveAspectRatio="none">
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
              <title>{`${day.date}: ${day.count} ${what}`}</title>
            </rect>
          )
        })}
        <text x={0} y={height + 14} className="axis">{days[0]?.date}</text>
        <text x={width} y={height + 14} textAnchor="end" className="axis">{days[days.length - 1]?.date}</text>
      </svg>
      <figcaption className="muted">{summary}</figcaption>
    </figure>
  )
}
