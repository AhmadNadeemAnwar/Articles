import { docToHtml, parseDoc } from '../posts/docHtml'
import { plainText } from '../posts/improve'
import type { KeptVersion } from '../posts/sections'
import { useConfirm, useToast } from './feedback'
import { formatDateTime } from './ui'

/** "Original", "Improved", or "Improved 14 Oct, 10:30" for a version stored as "Improved_2026-10-14T10:30:00Z". */
export function versionLabel(state: string): string {
  if (state === 'Original') return 'Original (your own wording)'
  if (state === 'Improved') return 'First improved version'
  const at = state.startsWith('Improved_') ? state.slice('Improved_'.length) : ''
  return at && !Number.isNaN(new Date(at).getTime()) ? `Improved ${formatDateTime(at)}` : state
}

const preview = (text: string) => {
  const flat = text.replace(/\s+/g, ' ').trim()
  return flat.length > 70 ? `${flat.slice(0, 70)}…` : flat
}

/**
 * The earlier versions of a text section, kept inside the post beside the text readers see. They are folded away until
 * opened, and readers never see them. Copy puts one on the clipboard with its bold, italic and links, so pasting brings them
 * back; Put back replaces the section's text with that version.
 */
export function KeptVersions({ versions, onPutBack, onRemove }: { versions: KeptVersion[]; onPutBack: (doc: string) => void; onRemove: () => void }) {
  const confirm = useConfirm()
  const notify = useToast()

  async function copy(version: KeptVersion) {
    const doc = parseDoc(version.doc)
    if (!doc) {
      notify('That version could not be read.', 'error')
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
      notify('Copied with its formatting. Paste it into a section to use it.')
    } catch {
      notify('It could not be copied. Select the text and copy it by hand.', 'error')
    }
  }

  async function putBack(version: KeptVersion) {
    const ok = await confirm({
      title: 'Put this version back?',
      message: 'The text above is replaced with this version. The other versions stay kept, and Ctrl+Z in the text undoes it.',
      confirmLabel: 'Put this version back',
    })
    if (ok) onPutBack(version.doc)
  }

  async function remove() {
    const ok = await confirm({
      title: 'Remove the earlier versions?',
      message: 'Your original wording and every earlier version of this section are deleted from the post. The text above stays.',
      confirmLabel: 'Remove them',
      danger: true,
    })
    if (ok) onRemove()
  }

  return (
    <details className="kept">
      <summary>
        Earlier versions ({versions.length}) <span className="muted">· kept in the post, hidden from readers</span>
      </summary>
      <ul className="kept__list">
        {[...versions].reverse().map((version, index) => {
          const doc = parseDoc(version.doc)
          return (
            <li key={`${version.state}-${index}`}>
              <h4>{versionLabel(version.state)}{doc && <span className="muted"> · {preview(plainText(doc))}</span>}</h4>
              {doc && <div className="improve__text prose" dangerouslySetInnerHTML={{ __html: docToHtml(doc) }} />}
              <div className="kept__actions">
                <button type="button" className="btn btn--small" onClick={() => void copy(version)}>Copy with formatting</button>
                <button type="button" className="btn btn--small" onClick={() => void putBack(version)}>Put this version back</button>
              </div>
            </li>
          )
        })}
      </ul>
      <button type="button" className="btn btn--small btn--danger" onClick={() => void remove()}>Remove the earlier versions</button>
    </details>
  )
}
