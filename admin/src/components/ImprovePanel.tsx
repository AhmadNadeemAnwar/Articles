import { useEffect, useRef, useState } from 'react'
import type { Editor } from '@tiptap/react'
import { api } from '../api/client'
import { docToHtml } from '../posts/docHtml'
import { applyUnits, chunkUnits, collectUnits, formattedBlocks, MAX_IMPROVE_CHARACTERS } from '../posts/improve'
import type { DocNode } from '../posts/improve'
import { ErrorNote } from './ui'

interface Proposal {
  /** The section as it is now, as written. */
  original: DocNode
  improved: DocNode
  replaced: number
  kept: number
  total: number
  formatted: number
  usedToday: number
  dailyLimit: number
}

/** "Improve text with AI" for one text section: nothing changes until the writer presses Apply, and the original is kept first. */
export function ImprovePanel({ editor, postId }: { editor: Editor; postId: string | null }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const [enabled, setEnabled] = useState<boolean | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [proposal, setProposal] = useState<Proposal | null>(null)
  const [applying, setApplying] = useState(false)
  const [applied, setApplied] = useState(false)

  useEffect(() => {
    let cancelled = false
    api.improveStatus().then((s) => { if (!cancelled) setEnabled(s.enabled) }).catch(() => { if (!cancelled) setEnabled(false) })
    return () => { cancelled = true }
  }, [])

  useEffect(() => {
    const element = dialog.current
    if (!element) return
    if (proposal && !element.open) element.showModal()
    if (!proposal && element.open) element.close()
  }, [proposal])

  if (!enabled) return null

  const currentDoc = () => editor.getJSON() as unknown as DocNode

  async function improve() {
    const original = currentDoc()
    const { units, slots } = collectUnits(original)
    if (units.length === 0) {
      setError('There is no text to improve yet.')
      return
    }
    if (units.reduce((n, u) => n + u.length, 0) > MAX_IMPROVE_CHARACTERS) {
      setError('This section is too long to improve in one go. Split it into two sections and improve each.')
      return
    }

    setError(null)
    setApplied(false)
    try {
      const chunks = chunkUnits(units)
      const improved = [...units]
      let usedToday = 0
      let dailyLimit = 0
      for (let part = 0; part < chunks.length; part++) {
        setBusy(chunks.length > 1 ? `Improving… part ${part + 1} of ${chunks.length}` : 'Improving…')
        const result = await api.improveText(chunks[part].map((i) => units[i]))
        chunks[part].forEach((unitIndex, k) => { improved[unitIndex] = result.units[k] })
        usedToday = result.usedToday
        dailyLimit = result.dailyLimit
      }

      const { doc, replaced, kept } = applyUnits(original, slots, units, improved)
      if (replaced === 0) {
        setError(kept > 0 ? 'The AI’s wording could not be used safely, so nothing was changed. Try again.' : 'The AI found nothing to improve here. The text reads well as it is.')
        return
      }
      setProposal({ original, improved: doc, replaced, kept, total: units.length, formatted: formattedBlocks(original), usedToday, dailyLimit })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The text could not be improved.')
    } finally {
      setBusy(null)
    }
  }

  async function apply() {
    if (!proposal) return
    if (!postId) {
      setError('Save the draft once first, so the original can be kept with the post.')
      return
    }
    // The section must still be what the AI was shown, or its answer no longer fits.
    if (JSON.stringify(currentDoc()) !== JSON.stringify(proposal.original)) {
      setProposal(null)
      setError('The text changed while the AI was working. Press Improve text with AI again.')
      return
    }

    setApplying(true)
    try {
      // The original is kept first: if that fails, nothing is replaced.
      await api.saveOriginal(postId, JSON.stringify(proposal.original), JSON.stringify(proposal.improved))
      editor.chain().focus().setContent(proposal.improved as never).run()
      setApplied(true)
      setProposal(null)
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The original could not be kept, so the text was not replaced.')
    } finally {
      setApplying(false)
    }
  }

  const close = () => setProposal(null)

  return (
    <>
      <button type="button" className="btn btn--small btn--ai" disabled={busy !== null || !postId}
        title={postId ? 'Reword this section with AI: better grammar and sentences that connect' : 'Save the draft once first, so the original can be kept with the post'}
        onClick={() => void improve()}>
        {busy ?? 'Improve text with AI'}
      </button>
      {applied && <span className="field__hint" role="status">Replaced. Press Ctrl+Z to undo, or find the original under “Original texts”.</span>}
      {error && <div className="grammar__error"><ErrorNote message={error} /></div>}

      <dialog ref={dialog} className="dialog dialog--wide" onCancel={(event) => { event.preventDefault(); close() }}
        onClick={(event) => { if (event.target === dialog.current) close() }}>
        {proposal && (
          <div className="dialog__body">
            <h2>Improved text</h2>
            <p className="muted">
              {proposal.replaced} of {proposal.total} {proposal.total === 1 ? 'paragraph' : 'paragraphs'} reworded: grammar fixed and sentences connected
              with clearer words. Your meaning is kept.
              {proposal.kept > 0 && <> {proposal.kept} could not be changed safely and stay as you wrote them.</>}
              {proposal.formatted > 0 && <> Bold, italic and links are kept.</>}
            </p>

            <div className="improve">
              <section>
                <h3>Improved</h3>
                <div className="improve__text prose" dangerouslySetInnerHTML={{ __html: docToHtml(proposal.improved) }} />
              </section>
              <section>
                <h3>Your original</h3>
                <div className="improve__text prose improve__text--original" dangerouslySetInnerHTML={{ __html: docToHtml(proposal.original) }} />
              </section>
            </div>

            <p className="field__hint">
              Apply keeps your original under “Original texts” and puts this version in. {Math.max(0, proposal.dailyLimit - proposal.usedToday)} AI goes left today.
            </p>
            <div className="dialog__actions">
              <button type="button" className="btn" onClick={close} disabled={applying}>Cancel</button>
              <button type="button" className="btn" onClick={() => { close(); void improve() }} disabled={applying}>Try again</button>
              <button type="button" className="btn btn--primary" onClick={() => void apply()} disabled={applying}>
                {applying ? 'Applying…' : 'Apply'}
              </button>
            </div>
          </div>
        )}
      </dialog>
    </>
  )
}
