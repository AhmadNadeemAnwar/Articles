import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'

/** What a reader can say they liked, in the order shown. The keys are what the API stores. */
export const LIKED_OPTIONS: { key: string; label: string }[] = [
  { key: 'clear-writing', label: 'Clear writing' },
  { key: 'useful', label: 'Useful' },
  { key: 'made-me-think', label: 'Made me think' },
  { key: 'personal', label: 'Personal' },
]

export const MAX_FEEDBACK = 500

const storageKey = (postId: string) => `articles.feedback.${postId}`

/** Whether this browser has already sent or closed the prompt for this post, so it is offered only once. */
export function feedbackHandled(postId: string, storage: Pick<Storage, 'getItem'> | null = safeStorage()): boolean {
  try {
    return storage?.getItem(storageKey(postId)) != null
  } catch {
    return false
  }
}

function remember(postId: string, outcome: 'sent' | 'closed') {
  try {
    safeStorage()?.setItem(storageKey(postId), outcome)
  } catch {
    // Not remembered: the prompt may be offered again on this post, which is harmless.
  }
}

function safeStorage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

/** Something to send: at least one thing liked, or some words, within the limit. */
export function canSend(liked: string[], text: string): boolean {
  return (liked.length > 0 || text.trim() !== '') && text.trim().length <= MAX_FEEDBACK
}

/**
 * A small card offered after a reader claps or marks a post Insightful: what they liked, and any suggestion. Anonymous,
 * one per post, and closing it means it is not offered again.
 */
export function FeedbackPrompt({ postId, onDone }: { postId: string; onDone: () => void }) {
  const [liked, setLiked] = useState<string[]>([])
  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const card = useRef<HTMLElement>(null)

  useEffect(() => {
    card.current?.focus()
  }, [])

  useEffect(() => {
    if (!sent) return
    const timer = setTimeout(onDone, 2500)
    return () => clearTimeout(timer)
  }, [sent, onDone])

  function close() {
    remember(postId, 'closed')
    onDone()
  }

  async function send() {
    if (!canSend(liked, text)) return
    setSending(true)
    setError(null)
    try {
      await api.sendFeedback(postId, liked, text.trim())
      remember(postId, 'sent')
      setSent(true)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'It could not be sent. Please try again.')
    } finally {
      setSending(false)
    }
  }

  const toggle = (key: string) => setLiked((current) => (current.includes(key) ? current.filter((k) => k !== key) : [...current, key]))

  if (sent) {
    return (
      <section className="feedback feedback--sent" role="status" tabIndex={-1} ref={card}>
        <p>Thank you! Your note has been sent.</p>
      </section>
    )
  }

  return (
    <section className="feedback" aria-labelledby={`feedback-${postId}`} tabIndex={-1} ref={card}
      onKeyDown={(event) => { if (event.key === 'Escape') close() }}>
      <div className="feedback__head">
        <h2 id={`feedback-${postId}`}>Thanks! Anything you liked, or a suggestion?</h2>
        <button type="button" className="feedback__close" aria-label="Close" title="Close" onClick={close}>×</button>
      </div>

      <div className="feedback__chips" role="group" aria-label="What you liked">
        {LIKED_OPTIONS.map(({ key, label }) => (
          <button key={key} type="button" className={`feedback__chip${liked.includes(key) ? ' feedback__chip--on' : ''}`}
            aria-pressed={liked.includes(key)} onClick={() => toggle(key)}>
            {label}
          </button>
        ))}
      </div>

      <label className="sr-only" htmlFor={`feedback-text-${postId}`}>A suggestion (optional)</label>
      <textarea id={`feedback-text-${postId}`} className="feedback__text" rows={3} maxLength={MAX_FEEDBACK + 50}
        placeholder="A suggestion, if you have one (optional)" value={text} onChange={(event) => setText(event.target.value)} />

      {error && <p className="feedback__error" role="alert">{error}</p>}
      <div className="feedback__actions">
        <span className="feedback__note">Anonymous: no name or email is sent.</span>
        <button type="button" className="btn btn--primary" disabled={!canSend(liked, text) || sending} onClick={() => void send()}>
          {sending ? 'Sending…' : 'Send'}
        </button>
      </div>
    </section>
  )
}
