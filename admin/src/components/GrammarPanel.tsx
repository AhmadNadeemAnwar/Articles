import { useState } from 'react'
import type { ReactNode } from 'react'
import type { Editor } from '@tiptap/react'
import { api } from '../api/client'
import { MAX_CHECK_CHARACTERS, afterApplying, buildTextMap, summary, toDocRange, toSuggestions } from '../posts/grammar'
import type { DocNode, Suggestion } from '../posts/grammar'
import { ErrorNote } from './ui'

/** Checks one text section for spelling and grammar. Nothing changes until the writer accepts a suggestion. */
export function GrammarPanel({ editor, children }: { editor: Editor; children?: ReactNode }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [suggestions, setSuggestions] = useState<Suggestion[] | null>(null)

  const doc = () => editor.state.doc as unknown as DocNode

  async function check() {
    const { text } = buildTextMap(doc())
    if (text.trim() === '') {
      setError('There is nothing to check yet.')
      return
    }
    if (text.length > MAX_CHECK_CHARACTERS) {
      setError('This section is too long to check in one go. Split it into two sections and check each.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      const { matches } = await api.checkGrammar(text)
      setSuggestions(toSuggestions(text, matches))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The grammar check could not run.')
    } finally {
      setBusy(false)
    }
  }

  /** Where the suggestion is now, or null if the writer has edited that spot since the check. */
  function locate(suggestion: Suggestion) {
    const range = toDocRange(buildTextMap(doc()), suggestion.offset, suggestion.length)
    if (!range || editor.state.doc.textBetween(range.from, range.to) !== suggestion.original) return null
    return range
  }

  function show(suggestion: Suggestion) {
    const range = locate(suggestion)
    if (!range) return stale()
    editor.chain().focus().setTextSelection(range).run()
  }

  function apply(suggestion: Suggestion, replacement: string) {
    const range = locate(suggestion)
    if (!range) return stale()
    // insertText keeps the bold, italic or link on the words being replaced.
    editor.view.dispatch(editor.state.tr.insertText(replacement, range.from, range.to))
    setSuggestions((current) => (current ? afterApplying(current, suggestion, replacement) : current))
  }

  function stale() {
    setError('That part of the text has changed since the check. Run the check again.')
    setSuggestions(null)
  }

  const ignore = (suggestion: Suggestion) =>
    setSuggestions((current) => (current ? current.filter((s) => s.id !== suggestion.id) : current))

  return (
    <div className="grammar">
      <div className="grammar__bar">
        <button type="button" className="btn btn--small" disabled={busy} onClick={check}>
          {busy ? 'Checking…' : 'Check grammar'}
        </button>
        {children}
        {suggestions && !busy && <span className="field__hint" role="status">{summary(suggestions.length)}</span>}
      </div>

      {error && <ErrorNote message={error} />}

      {suggestions && suggestions.length > 0 && (
        <ul className="grammar__list">
          {suggestions.map((suggestion) => (
            <li key={suggestion.id} className="grammar__item">
              <span className="grammar__word">{suggestion.original}</span>
              <p className="grammar__message">{suggestion.message}</p>
              <div className="grammar__actions">
                {suggestion.replacements.map((replacement) => (
                  <button key={replacement} type="button" className="btn btn--small btn--primary" onClick={() => apply(suggestion, replacement)}>
                    {replacement}
                  </button>
                ))}
                <button type="button" className="btn btn--small" onClick={() => show(suggestion)}>Show in text</button>
                <button type="button" className="btn btn--small" onClick={() => ignore(suggestion)}>Ignore</button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
