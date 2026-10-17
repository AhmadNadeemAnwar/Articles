import { useState } from 'react'
import { api } from '../api/client'
import type { ReaderFeedback } from '../api/types'
import { useAsync } from '../hooks/useAsync'
import { useConfirm, useToast } from '../components/feedback'
import { Empty, ErrorNote, PageHeader, Pagination, Spinner, formatDateTime, publicPostUrl } from '../components/ui'

/** How each choice a reader can tick is named here; the keys are what the API stores. */
export const likedLabels: Record<string, string> = {
  'clear-writing': 'Clear writing',
  useful: 'Useful',
  'made-me-think': 'Made me think',
  personal: 'Personal',
}

export const likedLabel = (key: string) => likedLabels[key] ?? key

/** Readers' short, anonymous notes after reacting to a post: what they liked and any suggestion. */
export function FeedbackPage() {
  const confirm = useConfirm()
  const notify = useToast()
  const [page, setPage] = useState(1)
  const [busyId, setBusyId] = useState<string | null>(null)
  const feedback = useAsync(() => api.feedback(page), [page])

  async function remove(item: ReaderFeedback) {
    const ok = await confirm({
      title: 'Delete this feedback?',
      message: <>The note about “{item.postTitle}” is deleted for good.</>,
      confirmLabel: 'Delete',
      danger: true,
    })
    if (!ok) return

    setBusyId(item.id)
    try {
      await api.deleteFeedback(item.id)
      notify('Feedback deleted.')
      feedback.reload()
    } catch (err) {
      notify(err instanceof Error ? err.message : 'That did not work.', 'error')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <>
      <PageHeader title="Feedback" />
      <p className="muted">
        After a reader claps or marks a post Insightful, they are offered a short card asking what they liked and for any
        suggestion. It is anonymous: no name, email or id comes with it.
      </p>

      {feedback.error && <ErrorNote message={feedback.error} onRetry={feedback.reload} />}
      {feedback.loading && !feedback.data && <Spinner />}

      {feedback.data && (feedback.data.items.length === 0 ? (
        <Empty title="No feedback yet">When readers send a note after reacting to a post, it appears here.</Empty>
      ) : (
        <>
          <ul className="cards">
            {feedback.data.items.map((item) => {
              const url = publicPostUrl(item.postSlug)
              return (
                <li key={item.id} className={`card${busyId === item.id ? ' is-busy' : ''}`}>
                  <div className="card__head">
                    <span className="muted">On {url ? <a href={url} target="_blank" rel="noopener noreferrer">{item.postTitle}</a> : item.postTitle}</span>
                    <span className="muted">· {formatDateTime(item.createdAt)}</span>
                  </div>
                  {item.liked.length > 0 && (
                    <div className="feedback-liked" aria-label="What they liked">
                      {item.liked.map((key) => <span key={key} className="badge badge--info">{likedLabel(key)}</span>)}
                    </div>
                  )}
                  {item.text && <p className="card__body">{item.text}</p>}
                  <div className="card__actions">
                    <button className="btn btn--small btn--danger" onClick={() => void remove(item)} disabled={busyId === item.id}>Delete</button>
                  </div>
                </li>
              )
            })}
          </ul>
          <Pagination page={page} totalPages={feedback.data.totalPages} onChange={setPage} />
        </>
      ))}
    </>
  )
}
