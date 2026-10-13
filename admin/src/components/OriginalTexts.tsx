import { useState } from 'react'
import { api } from '../api/client'
import type { SavedOriginal } from '../api/types'
import { docToHtml, parseDoc } from '../posts/docHtml'
import { plainText } from '../posts/improve'
import { useToast } from './feedback'
import { ErrorNote, formatDateTime } from './ui'

/**
 * The texts the writer replaced with the AI's version, kept exactly as they were, with their formatting. Pressing Copy
 * puts the original on the clipboard so pasting it into a section brings back the bold, italic, links and lists.
 */
export function OriginalTexts({ postId }: { postId: string }) {
  const notify = useToast()
  const [items, setItems] = useState<SavedOriginal[] | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setItems(await api.savedOriginals(postId))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The original texts could not be loaded.')
    } finally {
      setLoading(false)
    }
  }

  async function copy(item: SavedOriginal) {
    const doc = parseDoc(item.originalJson)
    if (!doc) {
      notify('That original could not be read.', 'error')
      return
    }

    try {
      const html = docToHtml(doc)
      const plain = plainText(doc)
      if (typeof ClipboardItem !== 'undefined' && navigator.clipboard?.write) {
        await navigator.clipboard.write([new ClipboardItem({ 'text/html': new Blob([html], { type: 'text/html' }), 'text/plain': new Blob([plain], { type: 'text/plain' }) })])
      } else {
        await navigator.clipboard.writeText(plain)
      }
      notify('Original copied. Paste it into a section to put it back with its formatting.')
    } catch {
      notify('The original could not be copied. Select the text and copy it by hand.', 'error')
    }
  }

  return (
    <section className="panel">
      <h2>Original texts</h2>
      <p className="muted">Every time you apply “Improve text with AI”, the text it replaced is kept here exactly as you wrote it, with its bold, italic, links and lists.</p>

      {items === null && <button type="button" className="btn btn--small" onClick={() => void load()} disabled={loading}>{loading ? 'Loading…' : 'Show the original texts'}</button>}
      {error && <ErrorNote message={error} onRetry={() => void load()} />}

      {items !== null && (items.length === 0 ? (
        <p className="muted">Nothing has been replaced yet.</p>
      ) : (
        <ul className="originals">
          {items.map((item) => {
            const doc = parseDoc(item.originalJson)
            return (
              <li key={item.id}>
                <details>
                  <summary>
                    <span>{formatDateTime(item.createdAt)}</span>
                    <span className="muted"> · {doc ? preview(plainText(doc)) : 'unreadable'}</span>
                  </summary>
                  {doc && <div className="improve__text prose" dangerouslySetInnerHTML={{ __html: docToHtml(doc) }} />}
                  <button type="button" className="btn btn--small" onClick={() => void copy(item)}>Copy with formatting</button>
                </details>
              </li>
            )
          })}
        </ul>
      ))}
    </section>
  )
}

function preview(text: string): string {
  const flat = text.replace(/\s+/g, ' ').trim()
  return flat.length > 70 ? `${flat.slice(0, 70)}…` : flat
}
