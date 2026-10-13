/**
 * "Improve text with AI", without losing the writer's formatting.
 *
 * A text section is the editor's own JSON: paragraphs, headings, lists, quotes, with bold, italic, strikethrough,
 * code and links inside them. The AI is only ever asked to reword the TEXT of each paragraph, heading or list item:
 * every such block is turned into one numbered line with the formatting written as simple markup
 * (**bold**, _italic_, ~~strike~~, `code`, [text](address)), and the reply is turned back into editor JSON.
 * Which block is a heading, a list item or a quote, and where each one sits, is never sent and so never changes.
 *
 * No React in here, so every rule can be unit tested.
 */

export interface Mark {
  type: string
  attrs?: Record<string, unknown>
}

export interface DocNode {
  type: string
  attrs?: Record<string, unknown>
  content?: DocNode[]
  text?: string
  marks?: Mark[]
}

/** The blocks whose text the AI rewrites. Code blocks and rules are left exactly as they are. */
const TEXT_BLOCKS = new Set(['paragraph', 'heading'])

/** Marks in the order they are written around the words, outermost first. */
const MARK_ORDER = ['link', 'bold', 'italic', 'strike', 'code']

const OPEN: Record<string, string> = { bold: '**', italic: '_', strike: '~~', code: '`' }

const SPECIAL = /[\\*_~`[\]]/g
const escapeText = (text: string) => text.replace(SPECIAL, (c) => `\\${c}`)

/** The line break inside a line, which the line-based request cannot hold as a real line break. */
export const LINE_BREAK = '⏎'

export const CHUNK_CHARACTERS = 5000
export const CHUNK_UNITS = 40
export const MAX_IMPROVE_CHARACTERS = 15_000

// ---- JSON to markup ---------------------------------------------------------------------------

function markNames(node: DocNode): string[] {
  const names = (node.marks ?? []).map((m) => m.type).filter((t) => MARK_ORDER.includes(t))
  return MARK_ORDER.filter((t) => names.includes(t))
}

function linkOf(node: DocNode): string | null {
  const link = (node.marks ?? []).find((m) => m.type === 'link')
  const href = link?.attrs?.href
  return typeof href === 'string' ? href : null
}

/** The words of a paragraph or heading as one line of markup. */
export function inlineToMarkup(content: DocNode[] | undefined): string {
  let out = ''
  let open: { name: string; href: string | null }[] = []

  const closeTo = (keep: number) => {
    for (let i = open.length - 1; i >= keep; i--) out += open[i].name === 'link' ? `](${open[i].href})` : OPEN[open[i].name]
    open = open.slice(0, keep)
  }

  for (const node of content ?? []) {
    if (node.type === 'hardBreak') {
      closeTo(0)
      out += '\n'
      continue
    }
    if (node.type !== 'text' || typeof node.text !== 'string') continue

    const wanted = markNames(node).map((name) => ({ name, href: name === 'link' ? linkOf(node) : null }))
    let common = 0
    while (common < open.length && common < wanted.length && open[common].name === wanted[common].name && open[common].href === wanted[common].href) common++
    closeTo(common)
    for (const mark of wanted.slice(common)) {
      out += mark.name === 'link' ? '[' : OPEN[mark.name]
      open.push(mark)
    }
    out += escapeText(node.text)
  }
  closeTo(0)
  return out
}

export interface Slot {
  /** Indexes down from the document's content to the block. */
  path: number[]
}

/** The lines to send, and where each came from. Blocks with no text are skipped. */
export function collectUnits(doc: DocNode): { units: string[]; slots: Slot[] } {
  const units: string[] = []
  const slots: Slot[] = []

  const walk = (node: DocNode, path: number[]) => {
    if (TEXT_BLOCKS.has(node.type)) {
      const line = inlineToMarkup(node.content)
      if (line.trim() !== '') {
        units.push(line)
        slots.push({ path })
      }
      return
    }
    if (node.type === 'codeBlock') return
    node.content?.forEach((child, index) => walk(child, [...path, index]))
  }
  doc.content?.forEach((child, index) => walk(child, [index]))
  return { units, slots }
}

/** Groups line positions so that no group is more than one go's worth of text. Same rule as the server's. */
export function chunkUnits(units: string[]): number[][] {
  const chunks: number[][] = []
  let current: number[] = []
  let size = 0
  units.forEach((unit, i) => {
    if (current.length > 0 && (size + unit.length > CHUNK_CHARACTERS || current.length >= CHUNK_UNITS)) {
      chunks.push(current)
      current = []
      size = 0
    }
    current.push(i)
    size += unit.length
  })
  if (current.length > 0) chunks.push(current)
  return chunks
}

// ---- Markup to JSON ---------------------------------------------------------------------------

const hrefsOf = (markup: string) => [...markup.matchAll(/\]\(([^)\s]+)\)/g)].map((m) => m[1])

/**
 * One line of markup as editor content, or null if the markup is not well formed (an emphasis left open, a link
 * with no address) or carries a link address the original line did not have. `linkAttrs` gives each address
 * its original attributes, so a link keeps its target and rel.
 */
export function markupToInline(markup: string, linkAttrs: Map<string, Record<string, unknown>> = new Map()): DocNode[] | null {
  const nodes: DocNode[] = []
  const active: string[] = []
  let text = ''
  let linkStart: number | null = null
  let i = 0

  const flush = () => {
    if (text === '') return
    const node: DocNode = { type: 'text', text }
    if (active.length > 0) node.marks = active.map((type) => ({ type }))
    nodes.push(node)
    text = ''
  }
  const toggle = (name: string) => {
    flush()
    const at = active.indexOf(name)
    if (at >= 0) active.splice(at, 1)
    else active.push(name)
  }

  while (i < markup.length) {
    const c = markup[i]
    if (c === '\\' && i + 1 < markup.length) {
      text += markup[i + 1]
      i += 2
    } else if (c === '\n') {
      flush()
      nodes.push({ type: 'hardBreak' })
      i++
    } else if (active.includes('code') && c !== '`') {
      text += c
      i++
    } else if (markup.startsWith('**', i)) {
      toggle('bold')
      i += 2
    } else if (markup.startsWith('~~', i)) {
      toggle('strike')
      i += 2
    } else if (c === '_') {
      toggle('italic')
      i++
    } else if (c === '`') {
      toggle('code')
      i++
    } else if (c === '[') {
      if (linkStart !== null) return null
      flush()
      linkStart = nodes.length
      i++
    } else if (c === ']') {
      if (linkStart === null || markup[i + 1] !== '(') return null
      const end = markup.indexOf(')', i + 2)
      if (end < 0) return null
      const href = markup.slice(i + 2, end)
      if (href === '' || /\s/.test(href)) return null
      flush()
      const attrs = { ...(linkAttrs.get(href) ?? {}), href }
      for (let k = linkStart; k < nodes.length; k++) {
        if (nodes[k].type !== 'text') continue
        nodes[k] = { ...nodes[k], marks: [{ type: 'link', attrs }, ...(nodes[k].marks ?? [])] }
      }
      linkStart = null
      i = end + 1
    } else {
      text += c
      i++
    }
  }
  flush()

  if (active.length > 0 || linkStart !== null) return null
  return nodes
}

/** True when every link address in `improved` was already in `original`, and none was lost. */
export function sameLinks(original: string, improved: string): boolean {
  const a = new Set(hrefsOf(original))
  const b = new Set(hrefsOf(improved))
  return a.size === b.size && [...a].every((h) => b.has(h))
}

function linkAttributes(doc: DocNode): Map<string, Record<string, unknown>> {
  const found = new Map<string, Record<string, unknown>>()
  const walk = (node: DocNode) => {
    for (const mark of node.marks ?? []) {
      const href = mark.attrs?.href
      if (mark.type === 'link' && typeof href === 'string' && !found.has(href)) found.set(href, { ...mark.attrs })
    }
    node.content?.forEach(walk)
  }
  walk(doc)
  return found
}

const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T

/**
 * The document with each collected block's words replaced by the improved line. Everything else is untouched.
 * A line that cannot be turned back into well-formed content, or that changed a link, keeps the writer's
 * original block; `kept` counts those.
 */
export function applyUnits(doc: DocNode, slots: Slot[], originals: string[], improved: string[]): { doc: DocNode; replaced: number; kept: number } {
  const next = clone(doc)
  const attrs = linkAttributes(doc)
  let replaced = 0
  let kept = 0

  slots.forEach((slot, index) => {
    const line = improved[index]
    if (line === undefined || line === originals[index]) return

    const content = sameLinks(originals[index], line) ? markupToInline(line, attrs) : null
    if (content === null || content.length === 0) {
      kept++
      return
    }

    let node: DocNode | undefined = next
    for (const step of slot.path) node = node?.content?.[step]
    if (!node) {
      kept++
      return
    }
    node.content = content
    replaced++
  })

  return { doc: next, replaced, kept }
}

/** The words of a document with no formatting, for copying as plain text. */
export function plainText(doc: DocNode): string {
  const parts: string[] = []
  const walk = (node: DocNode) => {
    if (node.type === 'text') parts.push(node.text ?? '')
    else if (node.type === 'hardBreak') parts.push('\n')
    node.content?.forEach(walk)
    if (['paragraph', 'heading', 'listItem', 'blockquote', 'codeBlock'].includes(node.type)) parts.push('\n')
  }
  walk(doc)
  return parts.join('').replace(/\n{3,}/g, '\n\n').trim()
}

/** How many blocks of the document have any bold, italic, strikethrough, code or links in them. */
export function formattedBlocks(doc: DocNode): number {
  let count = 0
  const walk = (node: DocNode) => {
    if (TEXT_BLOCKS.has(node.type) && (node.content ?? []).some((child) => (child.marks ?? []).length > 0)) count++
    else node.content?.forEach(walk)
  }
  walk(doc)
  return count
}
