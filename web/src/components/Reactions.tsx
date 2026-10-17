import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { ReactionKind, ReactionState } from '../api/types'
import { ErrorNote } from './ui'

interface Props {
  postId: string
  /** Totals that came with the post, shown until this browser's own state has loaded. */
  clapCount: number
  insightfulCount: number
  /** Told when this reader gives a reaction (not when they take one back), so the page can ask for feedback. */
  onGiven?: () => void
}

const KINDS: { kind: ReactionKind; icon: string; name: string; give: string; takeBack: string }[] = [
  { kind: 'clap', icon: '👏', name: 'Clap', give: 'Clap for this post', takeBack: 'Take back your clap' },
  { kind: 'insightful', icon: '💡', name: 'Insightful', give: 'Mark this post as insightful', takeBack: 'Take back your mark' },
]

/** What a screen reader says for a button that shows only a picture and a number. */
export function reactionLabel(name: string, count: number, mine: boolean): string {
  const people = count === 1 ? '1 reader' : `${count} readers`
  return `${name}: ${people}${mine ? ', including you' : ''}`
}

/**
 * What the reactions look like straight after this reader presses one, before the server has answered: the icon
 * fills (or empties) and the count moves by one. The server's answer replaces it a moment later.
 */
export function afterPress(state: ReactionState, kind: ReactionKind): ReactionState {
  if (kind === 'clap') {
    const on = !state.clapped
    return { ...state, clapped: on, clapCount: Math.max(0, state.clapCount + (on ? 1 : -1)) }
  }
  const on = !state.markedInsightful
  return { ...state, markedInsightful: on, insightfulCount: Math.max(0, state.insightfulCount + (on ? 1 : -1)) }
}

/**
 * The two reactions under a post, as icons with a count. An icon is grey until this reader presses
 * it; pressing fills it with colour and plays a short pop, and pressing again takes it back.
 */
export function Reactions({ postId, clapCount, insightfulCount, onGiven }: Props) {
  const [state, setState] = useState<ReactionState | null>(null)
  const [pending, setPending] = useState<ReactionKind | null>(null)
  const [popped, setPopped] = useState<ReactionKind | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setState(null)
    setPopped(null)
    // If this fails the totals from the post are shown and the buttons still work.
    api.reactions(postId).then((loaded) => { if (!cancelled) setState(loaded) }).catch(() => {})
    return () => { cancelled = true }
  }, [postId])

  async function toggle(kind: ReactionKind) {
    // One at a time: a second click before the first answer would otherwise undo it at once.
    if (pending) return
    const before: ReactionState = state ?? { clapCount, insightfulCount, clapped: false, markedInsightful: false }
    const guess = afterPress(before, kind)

    // Shown at once: the server can take a few seconds to answer when it has been idle, and a reader should not wait.
    setState(guess)
    const given = kind === 'clap' ? guess.clapped : guess.markedInsightful
    setPopped(given ? kind : null)
    if (given) onGiven?.()
    setPending(kind)
    setError(null)
    try {
      setState(await api.toggleReaction(postId, kind))
    } catch (err) {
      setState(before)
      setPopped(null)
      setError(err instanceof Error ? err.message : 'Your reaction could not be saved.')
    } finally {
      setPending(null)
    }
  }

  const count = { clap: state?.clapCount ?? clapCount, insightful: state?.insightfulCount ?? insightfulCount }
  const mine = { clap: state?.clapped ?? false, insightful: state?.markedInsightful ?? false }

  return (
    <>
      <div className="reactions" role="group" aria-label="Reactions">
        {KINDS.map(({ kind, icon, name, give, takeBack }) => (
          <button
            key={kind}
            type="button"
            className={`reaction${mine[kind] ? ' reaction--on' : ''}${popped === kind ? ' reaction--pop' : ''}`}
            aria-pressed={mine[kind]}
            aria-label={reactionLabel(name, count[kind], mine[kind])}
            title={mine[kind] ? takeBack : give}
            aria-busy={pending === kind}
            onClick={() => toggle(kind)}
            onAnimationEnd={() => setPopped((current) => (current === kind ? null : current))}
          >
            <span className="reaction__icon" aria-hidden="true">{icon}</span>
            <span className="reaction__count" aria-hidden="true">{count[kind]}</span>
          </button>
        ))}
      </div>
      {error && <ErrorNote message={error} />}
    </>
  )
}
