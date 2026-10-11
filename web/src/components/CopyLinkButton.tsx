import { useEffect, useRef, useState } from 'react'

const RESET_DELAY_MS = 2000

interface Props {
  url: string
  label?: string
  /** The address as shown to readers (Articles/<category>/<post>), written beside the button. The copied link is still `url`. */
  shown?: string
  /** Icon-only round button, sized to sit next to a heading rather than in a button row. */
  compact?: boolean
}

/**
 * Copies an absolute URL to the clipboard and shows brief "Copied" confirmation.
 *
 * Falls back to a hidden textarea + execCommand when the async Clipboard API is unavailable —
 * that happens on plain http origins and some older or embedded browsers, and a share button
 * that silently does nothing there is worse than not having one.
 */
export function CopyLinkButton({ url, label = 'Copy link', shown, compact = false }: Props) {
  const [copied, setCopied] = useState(false)
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => () => {
    if (timerRef.current) clearTimeout(timerRef.current)
  }, [])

  async function copy() {
    const ok = await writeToClipboard(url)
    if (!ok) return

    setCopied(true)
    if (timerRef.current) clearTimeout(timerRef.current)
    timerRef.current = setTimeout(() => setCopied(false), RESET_DELAY_MS)
  }

  const statusText = copied ? 'Link copied to clipboard' : ''

  const address = shown ? <span className="link-address" title={shown}>{shown}</span> : null

  if (compact) {
    return (
      <span className="link-copy link-copy--compact">
        {address}
        <button
          className={`btn btn--icon${copied ? ' btn--active' : ''}`}
          onClick={copy}
          aria-label={copied ? 'Link copied' : `${label} to this post`}
          title={copied ? 'Link copied' : label}
        >
          <LinkIcon />
          <span className="sr-only" role="status" aria-live="polite">{statusText}</span>
        </button>
      </span>
    )
  }

  return (
    <span className="link-copy">
      {address}
      <button className="btn" onClick={copy} aria-label={`${label} to this post`}>
        {copied ? 'Link copied' : label}
        <span className="sr-only" role="status" aria-live="polite">{statusText}</span>
      </button>
    </span>
  )
}

function LinkIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" />
      <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" />
    </svg>
  )
}

async function writeToClipboard(text: string): Promise<boolean> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text)
      return true
    } catch {
      // Falls through to the execCommand fallback below.
    }
  }

  const textarea = document.createElement('textarea')
  textarea.value = text
  // Keep it in the document (off-screen) — execCommand needs a real selectable node.
  textarea.style.position = 'fixed'
  textarea.style.left = '-9999px'
  textarea.setAttribute('readonly', '')
  document.body.appendChild(textarea)
  textarea.select()

  let ok = false
  try {
    ok = document.execCommand('copy')
  } catch {
    ok = false
  } finally {
    document.body.removeChild(textarea)
  }

  return ok
}
