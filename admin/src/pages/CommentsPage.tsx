import { useState } from 'react'
import { api } from '../api/client'
import type { AdminComment } from '../api/types'
import { useAsync } from '../hooks/useAsync'
import { useConfirm, useToast } from '../components/feedback'
import { noteState, MAX_NOTE_LENGTH } from '../posts/globalNote'
import { Badge, Empty, ErrorNote, PageHeader, Pagination, Spinner, formatDateTime, publicPostUrl } from '../components/ui'

/** The note that appears at the end of every post. Saved explicitly; saving it empty removes it. */
function GlobalNotePanel() {
  const confirm = useConfirm()
  const notify = useToast()
  const saved = useAsync(() => api.globalNote(), [])
  const [draft, setDraft] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const current = saved.data?.text ?? ''
  const text = draft ?? current
  const state = noteState(current, text)

  async function save() {
    if (state.removing) {
      const ok = await confirm({
        title: 'Remove the note?',
        message: 'The note disappears from every post.',
        confirmLabel: 'Remove note',
        danger: true,
      })
      if (!ok) return
    }

    setSaving(true)
    try {
      const result = await api.saveGlobalNote(text)
      setDraft(null)
      saved.reload()
      notify(result.text === '' ? 'The note was removed from every post.' : 'The note now appears at the end of every post.')
    } catch (err) {
      notify(err instanceof Error ? err.message : 'The note could not be saved.', 'error')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="panel">
      <h2>Note on every post</h2>
      <p className="muted">
        Shown in a box at the end of every published post, headed “Note from” your name. Plain text: web addresses become links and line
        breaks are kept. Leave it empty and save to remove it.
      </p>

      {saved.error && <ErrorNote message={saved.error} onRetry={saved.reload} />}
      {saved.loading && !saved.data && <Spinner />}

      {saved.data && (
        <>
          <div className="field">
            <textarea aria-label="The note shown on every post" rows={4} value={text} maxLength={MAX_NOTE_LENGTH + 200} placeholder="For example: Thank you for reading. Write to me at https://…"
              onChange={(e) => setDraft(e.target.value)} />
          </div>
          <div className="note-actions">
            <span className={`muted${state.tooLong ? ' note-actions__over' : ''}`}>{state.left} characters left</span>
            <button type="button" className="btn btn--primary" disabled={!state.canSave || saving} onClick={() => void save()}>
              {saving ? 'Saving…' : state.removing ? 'Remove the note' : current === '' ? 'Show on every post' : 'Save the note'}
            </button>
          </div>
        </>
      )}
    </section>
  )
}

export function CommentsPage() {
  const confirm = useConfirm()
  const notify = useToast()
  const [page, setPage] = useState(1)
  const [busyId, setBusyId] = useState<string | null>(null)

  const comments = useAsync(() => api.comments(page), [page])

  async function remove(comment: AdminComment) {
    const ok = await confirm({
      title: 'Remove this comment?',
      message: <>The comment by @{comment.authorHandle} is blanked and shown as “deleted”. Replies under it stay in place.</>,
      confirmLabel: 'Remove comment',
      danger: true,
    })
    if (!ok) return

    setBusyId(comment.id)
    try {
      await api.deleteComment(comment.id)
      notify('Comment removed.')
      comments.reload()
    } catch (err) {
      notify(err instanceof Error ? err.message : 'That did not work.', 'error')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <>
      <PageHeader title="Comments" />
      <GlobalNotePanel />

      {comments.error && <ErrorNote message={comments.error} onRetry={comments.reload} />}
      {comments.loading && !comments.data && <Spinner />}

      {comments.data && (comments.data.items.length === 0 ? (
        <Empty title="No comments yet">When readers respond to posts, their comments appear here.</Empty>
      ) : (
        <>
          <ul className="cards">
            {comments.data.items.map((comment) => {
              const url = publicPostUrl(comment.postSlug)
              return (
                <li key={comment.id} className={`card${comment.isDeleted ? ' card--dim' : ''}${busyId === comment.id ? ' is-busy' : ''}`}>
                  <div className="card__head">
                    <strong>@{comment.authorHandle}</strong>
                    <span className="muted">on {url ? <a href={url} target="_blank" rel="noopener noreferrer">{comment.postTitle}</a> : comment.postTitle}</span>
                    <span className="muted">· {formatDateTime(comment.createdAt)}</span>
                    {comment.isReply && <Badge>reply</Badge>}
                    {comment.isDeleted && <Badge tone="bad">removed</Badge>}
                  </div>
                  <p className="card__body">{comment.isDeleted ? 'This comment was removed.' : comment.body}</p>
                  {!comment.isDeleted && (
                    <div className="card__actions">
                      <button className="btn btn--small btn--danger" onClick={() => remove(comment)} disabled={busyId === comment.id}>Remove</button>
                    </div>
                  )}
                </li>
              )
            })}
          </ul>
          <Pagination page={page} totalPages={comments.data.totalPages} onChange={setPage} />
        </>
      ))}
    </>
  )
}
