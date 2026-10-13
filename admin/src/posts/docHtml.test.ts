import { describe, expect, it } from 'vitest'
import { docToHtml, parseDoc } from './docHtml'
import type { DocNode } from './improve'

const text = (value: string, ...marks: { type: string; attrs?: Record<string, unknown> }[]): DocNode => ({ type: 'text', text: value, ...(marks.length ? { marks } : {}) })
const doc = (...content: DocNode[]): DocNode => ({ type: 'doc', content })

describe('docToHtml', () => {
  it('writes paragraphs, headings, quotes and lists', () => {
    const html = docToHtml(doc(
      { type: 'heading', attrs: { level: 2 }, content: [text('Title')] },
      { type: 'paragraph', content: [text('Body')] },
      { type: 'blockquote', content: [{ type: 'paragraph', content: [text('Quoted')] }] },
      { type: 'bulletList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [text('one')] }] }] },
      { type: 'orderedList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [text('first')] }] }] },
    ))

    expect(html).toBe('<h2>Title</h2><p>Body</p><blockquote><p>Quoted</p></blockquote><ul><li><p>one</p></li></ul><ol><li><p>first</p></li></ol>')
  })

  it('keeps a numbered list counting from where it started', () => {
    const list = (start: number) => ({ type: 'orderedList', attrs: { start }, content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [text('x')] }] }] })

    expect(docToHtml(doc(list(3)))).toBe('<ol start="3"><li><p>x</p></li></ol>')
    expect(docToHtml(doc(list(1)))).toBe('<ol><li><p>x</p></li></ol>')
  })

  it('writes bold, italic, strikethrough, code and a line break', () => {
    expect(docToHtml(doc({ type: 'paragraph', content: [text('a', { type: 'bold' }), text('b', { type: 'italic' }), text('c', { type: 'strike' }), text('d', { type: 'code' }), { type: 'hardBreak' }, text('e')] })))
      .toBe('<p><strong>a</strong><em>b</em><s>c</s><code>d</code><br>e</p>')
  })

  it('writes a web link that opens safely', () => {
    expect(docToHtml(doc({ type: 'paragraph', content: [text('here', { type: 'link', attrs: { href: 'https://example.com/a?x=1&y=2' } })] })))
      .toBe('<p><a href="https://example.com/a?x=1&amp;y=2" target="_blank" rel="noopener noreferrer nofollow">here</a></p>')
  })

  it('never lets text become markup, and only makes links of web and mail addresses', () => {
    const html = docToHtml(doc({ type: 'paragraph', content: [
      text('<img src=x onerror=alert(1)> & "quotes"'),
      text('click', { type: 'link', attrs: { href: 'javascript:alert(1)' } }),
      text('data', { type: 'link', attrs: { href: 'data:text/html,<script>x</script>' } }),
    ] }))

    expect(html).not.toContain('<img')
    expect(html).not.toContain('<script')
    expect(html).not.toContain('href=')
    expect(html).toContain('&lt;img src=x onerror=alert(1)&gt; &amp; &quot;quotes&quot;')
  })

  it('shows code blocks as text and leaves out what it does not know', () => {
    expect(docToHtml(doc({ type: 'codeBlock', content: [text('<b>x</b>')] }, { type: 'mystery' }))).toBe('<pre><code>&lt;b&gt;x&lt;/b&gt;</code></pre>')
  })

  it('is empty for an empty document', () => {
    expect(docToHtml(doc())).toBe('')
  })
})

describe('parseDoc', () => {
  it('reads a saved document', () => {
    expect(parseDoc('{"type":"doc","content":[]}')).toEqual({ type: 'doc', content: [] })
  })

  it.each(['', 'not json', '[]', '{"type":"paragraph"}', 'null', '42'])('refuses %j', (value) => {
    expect(parseDoc(value)).toBeNull()
  })
})
