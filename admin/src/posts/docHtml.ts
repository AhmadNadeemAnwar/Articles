/**
 * A text section's JSON as plain HTML, for showing it in the improve dialog and the list of originals, and for
 * copying it with its formatting. Every piece of text is escaped and only web or mail addresses become links,
 * so nothing the document contains can run.
 */
import type { DocNode } from './improve'

const escape = (value: string) =>
  value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;')

const SAFE_LINK = /^(https?:\/\/|mailto:)/i

const WRAP: Record<string, [string, string]> = {
  bold: ['<strong>', '</strong>'],
  italic: ['<em>', '</em>'],
  strike: ['<s>', '</s>'],
  code: ['<code>', '</code>'],
}

function inline(node: DocNode): string {
  if (node.type === 'hardBreak') return '<br>'
  if (node.type !== 'text') return ''

  let html = escape(node.text ?? '')
  for (const mark of node.marks ?? []) {
    if (WRAP[mark.type]) html = `${WRAP[mark.type][0]}${html}${WRAP[mark.type][1]}`
    else if (mark.type === 'link') {
      const href = String(mark.attrs?.href ?? '')
      if (SAFE_LINK.test(href)) html = `<a href="${escape(href)}" target="_blank" rel="noopener noreferrer nofollow">${html}</a>`
    }
  }
  return html
}

function block(node: DocNode): string {
  const inner = () => (node.content ?? []).map(block).join('')
  const text = () => (node.content ?? []).map(inline).join('')

  switch (node.type) {
    case 'paragraph': return `<p>${text()}</p>`
    case 'heading': return `<h${node.attrs?.level === 3 ? 3 : 2}>${text()}</h${node.attrs?.level === 3 ? 3 : 2}>`
    case 'blockquote': return `<blockquote>${inner()}</blockquote>`
    case 'bulletList': return `<ul>${inner()}</ul>`
    case 'orderedList': {
      const start = Number(node.attrs?.start)
      return `<ol${Number.isInteger(start) && start > 1 && start < 10000 ? ` start="${start}"` : ''}>${inner()}</ol>`
    }
    case 'listItem': return `<li>${inner()}</li>`
    case 'codeBlock': return `<pre><code>${(node.content ?? []).map((n) => escape(n.text ?? '')).join('')}</code></pre>`
    case 'horizontalRule': return '<hr>'
    default: return node.content ? inner() : ''
  }
}

export function docToHtml(doc: DocNode): string {
  return (doc.content ?? []).map(block).join('')
}

/** Reads a saved document, or null if it cannot be read. */
export function parseDoc(json: string): DocNode | null {
  try {
    const value = JSON.parse(json) as DocNode
    return value && typeof value === 'object' && value.type === 'doc' ? value : null
  } catch {
    return null
  }
}
