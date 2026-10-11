/** The author's note is plain text. This turns web addresses in it into links without ever treating any of it as markup. */

export interface NotePart {
  text: string
  /** Set only for a web address, and only ever an https or http one. */
  href?: string
}

const ADDRESS = /https?:\/\/[^\s<>"']+/gi
/** Punctuation that ends a sentence rather than an address. */
const TRAILING = /[.,;:!?)\]}»”’]+$/

export function noteParts(text: string): NotePart[] {
  const parts: NotePart[] = []
  let last = 0
  for (const match of text.matchAll(ADDRESS)) {
    const start = match.index ?? 0
    const raw = match[0]
    const address = raw.replace(TRAILING, '')
    if (start > last) parts.push({ text: text.slice(last, start) })

    let safe = false
    try {
      const url = new URL(address)
      safe = url.protocol === 'https:' || url.protocol === 'http:'
    } catch {
      safe = false
    }
    parts.push(safe ? { text: address, href: address } : { text: address })
    if (address.length < raw.length) parts.push({ text: raw.slice(address.length) })
    last = start + raw.length
  }
  if (last < text.length) parts.push({ text: text.slice(last) })
  return parts
}
