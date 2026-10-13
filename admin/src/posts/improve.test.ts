import { describe, expect, it } from 'vitest'
import { applyUnits, chunkUnits, collectUnits, formattedBlocks, inlineToMarkup, markupToInline, plainText, sameLinks } from './improve'
import type { DocNode } from './improve'

const text = (value: string, ...marks: (string | { type: string; attrs: Record<string, unknown> })[]): DocNode => ({
  type: 'text',
  text: value,
  ...(marks.length ? { marks: marks.map((m) => (typeof m === 'string' ? { type: m } : m)) } : {}),
})
const link = (href: string) => ({ type: 'link', attrs: { href, target: '_blank', rel: 'noopener' } })
const para = (...content: DocNode[]): DocNode => ({ type: 'paragraph', content })
const doc = (...content: DocNode[]): DocNode => ({ type: 'doc', content })

describe('inlineToMarkup', () => {
  it('writes plain text as it is', () => {
    expect(inlineToMarkup([text('Hello there.')])).toBe('Hello there.')
  })

  it('writes bold, italic, strikethrough and code as markup', () => {
    expect(inlineToMarkup([text('a '), text('b', 'bold'), text(' c '), text('d', 'italic'), text(' '), text('e', 'strike'), text(' '), text('f', 'code')]))
      .toBe('a **b** c _d_ ~~e~~ `f`')
  })

  it('writes a link with its address, and bold inside a link', () => {
    expect(inlineToMarkup([text('see '), text('my page', link('https://example.com/a'))])).toBe('see [my page](https://example.com/a)')
    expect(inlineToMarkup([text('x', link('https://e.example'), 'bold')])).toBe('[**x**](https://e.example)')
  })

  it('does not close and reopen emphasis between neighbouring pieces that share it', () => {
    expect(inlineToMarkup([text('one ', 'bold'), text('two', 'bold', 'italic'), text(' three', 'bold')])).toBe('**one _two_ three**')
  })

  it('writes a line break inside a paragraph as a real line break', () => {
    expect(inlineToMarkup([text('first'), { type: 'hardBreak' }, text('second')])).toBe('first\nsecond')
  })

  it('escapes characters that would be read as markup', () => {
    expect(inlineToMarkup([text('a*b_c [d] `e` ~f~ \\g')])).toBe('a\\*b\\_c \\[d\\] \\`e\\` \\~f\\~ \\\\g')
  })
})

describe('markupToInline', () => {
  it('reads plain text', () => {
    expect(markupToInline('Hello there.')).toEqual([text('Hello there.')])
  })

  it('reads bold, italic, strikethrough and code', () => {
    expect(markupToInline('a **b** c _d_ ~~e~~ `f`')).toEqual([text('a '), text('b', 'bold'), text(' c '), text('d', 'italic'), text(' '), text('e', 'strike'), text(' '), text('f', 'code')])
  })

  it('reads a link, and gives it the attributes it had', () => {
    const attrs = new Map([['https://example.com/a', { href: 'https://example.com/a', target: '_blank', rel: 'noopener' }]])

    expect(markupToInline('see [my page](https://example.com/a)', attrs)).toEqual([text('see '), text('my page', link('https://example.com/a'))])
  })

  it('reads emphasis that crosses a link', () => {
    const nodes = markupToInline('**a [b](https://e.example) c**')!

    expect(nodes.map((n) => n.text)).toEqual(['a ', 'b', ' c'])
    expect(nodes[1].marks?.map((m) => m.type).sort()).toEqual(['bold', 'link'])
  })

  it('reads a line break and escaped characters', () => {
    expect(markupToInline('first\nsecond')).toEqual([text('first'), { type: 'hardBreak' }, text('second')])
    expect(markupToInline('a\\*b\\_c')).toEqual([text('a*b_c')])
  })

  it('keeps markers that are inside code as plain characters', () => {
    expect(markupToInline('`a **b** _c_`')).toEqual([text('a **b** _c_', 'code')])
  })

  it.each(['an **unclosed bold', 'an _unclosed italic', 'a [link with no address]', 'a [link](  )', 'a [link](https://x.example', 'nested [a [b](https://x.example)](https://y.example)', 'stray ] bracket', 'half `code'])(
    'refuses badly formed markup: %s', (markup) => {
      expect(markupToInline(markup)).toBeNull()
    })
})

describe('writing and reading come back to the same content', () => {
  const cases: [string, DocNode[]][] = [
    ['plain', [text('Just words.')]],
    ['all the marks', [text('a '), text('b', 'bold'), text(' '), text('c', 'italic'), text(' '), text('d', 'strike'), text(' '), text('e', 'code')]],
    ['nested marks', [text('one ', 'bold'), text('two', 'bold', 'italic'), text(' three', 'bold')]],
    ['a link', [text('go to '), text('the page', link('https://example.com/p?a=1&b=2')), text('.')]],
    ['a link with bold', [text('go '), text('now', link('https://e.example'), 'bold')]],
    ['special characters', [text('1 * 2 = 2, snake_case, [x], `y`, ~z~, \\')]],
    ['a line break', [text('one'), { type: 'hardBreak' }, text('two', 'italic')]],
    ['urdu', [text('یہ ایک '), text('اہم', 'bold'), text(' جملہ ہے۔')]],
  ]

  it.each(cases)('%s', (_name, content) => {
    const attrs = new Map<string, Record<string, unknown>>()
    for (const node of content) for (const mark of node.marks ?? []) if (mark.type === 'link') attrs.set(String(mark.attrs?.href), { ...mark.attrs })

    const back = markupToInline(inlineToMarkup(content), attrs)

    // Marks are written in a fixed order, so compare them as sets.
    const norm = (nodes: DocNode[] | null) => (nodes ?? []).map((n) => ({ ...n, marks: n.marks ? [...n.marks].map((m) => JSON.stringify(m)).sort() : undefined }))
    expect(norm(back)).toEqual(norm(content))
  })
})

describe('collectUnits', () => {
  const sample = doc(
    { type: 'heading', attrs: { level: 2 }, content: [text('A heading')] },
    para(text('First '), text('paragraph', 'bold'), text('.')),
    para(),
    { type: 'bulletList', content: [{ type: 'listItem', content: [para(text('item one'))] }, { type: 'listItem', content: [para(text('item two', 'italic'))] }] },
    { type: 'blockquote', content: [para(text('quoted words'))] },
    { type: 'codeBlock', content: [text('const x = 1')] },
    { type: 'horizontalRule' },
  )

  it('takes the text of each heading, paragraph, list item and quote, in order, and nothing else', () => {
    expect(collectUnits(sample).units).toEqual(['A heading', 'First **paragraph**.', 'item one', '_item two_', 'quoted words'])
  })

  it('skips empty paragraphs and code', () => {
    expect(collectUnits(sample).units.join('|')).not.toContain('const')
    expect(collectUnits(doc(para(), para(text('   ')))).units).toEqual([])
  })

  it('records where each came from', () => {
    expect(collectUnits(sample).slots.map((s) => s.path)).toEqual([[0], [1], [3, 0, 0], [3, 1, 0], [4, 0]])
  })
})

describe('applyUnits', () => {
  const original = doc(
    { type: 'heading', attrs: { level: 2 }, content: [text('A heading')] },
    para(text('First '), text('paragraph', 'bold'), text(' see '), text('here', link('https://example.com/a'))),
    { type: 'bulletList', content: [{ type: 'listItem', content: [para(text('item one'))] }] },
    { type: 'codeBlock', content: [text('keep me')] },
  )

  it('puts the improved words in, and leaves headings, lists, code and everything else where they were', () => {
    const { units, slots } = collectUnits(original)
    const improved = ['A better heading', 'First **paragraph**, and see [here](https://example.com/a)', 'The first item']

    const result = applyUnits(original, slots, units, improved)

    expect(result.replaced).toBe(3)
    expect(result.kept).toBe(0)
    const d = result.doc.content!
    expect(d[0]).toEqual({ type: 'heading', attrs: { level: 2 }, content: [text('A better heading')] })
    expect(d[1].content![1]).toEqual(text('paragraph', 'bold'))
    expect(d[1].content![3]).toEqual(text('here', link('https://example.com/a')))
    expect(d[2].type).toBe('bulletList')
    expect(d[2].content![0].content![0].content).toEqual([text('The first item')])
    expect(d[3]).toEqual({ type: 'codeBlock', content: [text('keep me')] })
  })

  it('does not change the document it was given', () => {
    const before = JSON.stringify(original)
    const { units, slots } = collectUnits(original)

    applyUnits(original, slots, units, ['x', 'y', 'z'])

    expect(JSON.stringify(original)).toBe(before)
  })

  it('keeps the writer’s block when the improved line is badly formed or changed a link', () => {
    const { units, slots } = collectUnits(original)

    const result = applyUnits(original, slots, units, ['Fine heading', 'a **broken bold', 'The first item'])
    expect(result.kept).toBe(1)
    expect(result.doc.content![1]).toEqual(original.content![1])

    const swapped = applyUnits(original, slots, units, [units[0], 'First paragraph, see [here](https://evil.example)', units[2]])
    expect(swapped.kept).toBe(1)
    expect(swapped.replaced).toBe(0)

    const dropped = applyUnits(original, slots, units, [units[0], 'First paragraph, see here', units[2]])
    expect(dropped.kept).toBe(1)
  })

  it('counts a line left unchanged as neither replaced nor kept', () => {
    const { units, slots } = collectUnits(original)

    expect(applyUnits(original, slots, units, units)).toMatchObject({ replaced: 0, kept: 0 })
  })
})

describe('sameLinks', () => {
  it('is true when the same addresses are there, in any order, and false when one is lost, added or changed', () => {
    expect(sameLinks('[a](https://x.example) [b](https://y.example)', '[b](https://y.example) and [a](https://x.example)')).toBe(true)
    expect(sameLinks('no links', 'still none')).toBe(true)
    expect(sameLinks('[a](https://x.example)', 'a')).toBe(false)
    expect(sameLinks('a', '[a](https://x.example)')).toBe(false)
    expect(sameLinks('[a](https://x.example)', '[a](https://y.example)')).toBe(false)
  })
})

describe('chunkUnits', () => {
  it('keeps short text in one piece', () => {
    expect(chunkUnits(['a', 'b', 'c'])).toEqual([[0, 1, 2]])
  })

  it('cuts long text between lines so no piece is more than one go', () => {
    const lines = Array.from({ length: 12 }, () => 'x'.repeat(1500))
    const chunks = chunkUnits(lines)

    expect(chunks.flat()).toEqual(lines.map((_, i) => i))
    for (const chunk of chunks) expect(chunk.reduce((n, i) => n + lines[i].length, 0)).toBeLessThanOrEqual(5000)
    expect(chunks.length).toBeGreaterThan(2)
  })

  it('also cuts after 40 lines', () => {
    expect(chunkUnits(Array.from({ length: 45 }, () => 'x')).map((c) => c.length)).toEqual([40, 5])
  })

  it('is empty for no lines', () => {
    expect(chunkUnits([])).toEqual([])
  })
})

describe('plainText and formattedBlocks', () => {
  const sample = doc({ type: 'heading', attrs: { level: 2 }, content: [text('Title')] }, para(text('one '), text('two', 'bold')), para(text('plain')))

  it('gives the words with no formatting, a line per block', () => {
    expect(plainText(sample)).toBe('Title\none two\nplain')
  })

  it('counts the blocks that carry any formatting', () => {
    expect(formattedBlocks(sample)).toBe(1)
    expect(formattedBlocks(doc(para(text('x'))))).toBe(0)
  })
})
