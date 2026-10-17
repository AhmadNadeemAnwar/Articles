import { describe, expect, it } from 'vitest'
import {
  MAX_KEPT_VERSIONS, hasContent, improvedStateAt, isImprovedState, moveSection, parseContent, serializeContent, withImprovement, withoutHistory,
} from './sections'
import type { Section } from './sections'

const IMAGE_ID = '0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11'
const paragraph = (words: string) => ({ type: 'paragraph', content: [{ type: 'text', text: words }] })
const docOf = (words: string) => JSON.stringify({ type: 'doc', content: [paragraph(words)] })
const textSection = (words: string): Section => ({ key: `t-${words}`, type: 'text', doc: docOf(words), history: [], liveState: null })
const imageSection = (): Section => ({ key: 'img', type: 'image', imageId: IMAGE_ID, alt: 'A chart', caption: '', width: null, height: null, size: 'full' })
const stored = (sections: Section[]) => JSON.parse(serializeContent(sections)) as { content: { type: string; textState?: string }[] }
const texts = (section: Section) => (section.type === 'text' ? [...section.history.map((h) => [h.state, h.doc]), [section.liveState, section.doc]] : [])

const AT = new Date('2026-10-14T10:30:00.123Z')

describe('kept earlier versions of a text section', () => {
  it('names an improvement by the second it was made', () => {
    expect(improvedStateAt(AT)).toBe('Improved_2026-10-14T10:30:00Z')
    expect(isImprovedState('Improved')).toBe(true)
    expect(isImprovedState('Improved_2026-10-14T10:30:00Z')).toBe(true)
    for (const bad of ['Original', 'improved', 'Improved_', 'Improved_soon', 'Improved_2026-10-14', null, 5]) expect(isImprovedState(bad)).toBe(false)
  })

  it('the first improvement keeps what the writer wrote as Original and the new text as Improved', () => {
    const next = withImprovement(textSection('mine'), docOf('mine'), docOf('better'), AT)

    expect(texts(next)).toEqual([['Original', docOf('mine')], ['Improved', docOf('better')]])
  })

  it('each later improvement keeps the text it replaced and stores the new one with its time', () => {
    const once = withImprovement(textSection('mine'), docOf('mine'), docOf('better'), AT)
    const twice = withImprovement(once, docOf('better'), docOf('best'), new Date('2026-10-15T08:00:00Z'))
    const thrice = withImprovement(twice, docOf('best'), docOf('finest'), new Date('2026-10-16T09:00:00Z'))

    expect(texts(twice)).toEqual([['Original', docOf('mine')], ['Improved', docOf('better')], ['Improved_2026-10-15T08:00:00Z', docOf('best')]])
    expect(texts(thrice).map((t) => t[0])).toEqual(['Original', 'Improved', 'Improved_2026-10-15T08:00:00Z', 'Improved_2026-10-16T09:00:00Z'])
  })

  it('keeps the original and the newest versions when there are too many', () => {
    let section: Section = textSection('mine')
    let previous = docOf('mine')
    for (let i = 1; i <= MAX_KEPT_VERSIONS + 5; i++) {
      const next = docOf(`version ${i}`)
      section = withImprovement(section, previous, next, new Date(Date.UTC(2026, 9, 14, 10, 0, i)))
      previous = next
    }

    if (section.type !== 'text') throw new Error('expected text')
    expect(section.history).toHaveLength(MAX_KEPT_VERSIONS)
    expect(section.history[0]).toEqual({ state: 'Original', doc: docOf('mine') })
    expect(section.doc).toBe(docOf(`version ${MAX_KEPT_VERSIONS + 5}`))
  })

  it('stores earlier versions first, oldest first, and the text readers see last', () => {
    const once = withImprovement(textSection('mine'), docOf('mine'), docOf('better'), AT)
    const section = withImprovement(once, docOf('better'), docOf('best'), new Date('2026-10-15T08:00:00Z'))

    const body = stored([textSection('Intro'), section, imageSection()])

    expect(body.content.map((s) => s.type)).toEqual(['textSection', 'textSection', 'textSection', 'textSection', 'imageSection'])
    expect(body.content.map((s) => s.textState)).toEqual([undefined, 'Original', 'Improved', 'Improved_2026-10-15T08:00:00Z', undefined])
  })

  it('opens what it stored as one section again, with every version in place', () => {
    const once = withImprovement(textSection('mine'), docOf('mine'), docOf('better'), AT)
    const original = withImprovement(once, docOf('better'), docOf('best'), new Date('2026-10-15T08:00:00Z'))

    const opened = parseContent(serializeContent([textSection('Intro'), original, imageSection()]))

    expect(opened).toHaveLength(3)
    expect(texts(opened[1])).toEqual([
      ['Original', docOf('mine')],
      ['Improved', docOf('better')],
      ['Improved_2026-10-15T08:00:00Z', docOf('best')],
    ])
  })

  it('opens a post with no kept versions as plain sections, and saves it unchanged', () => {
    const plain = serializeContent([textSection('One'), textSection('Two')])

    expect(serializeContent(parseContent(plain))).toBe(plain)
    expect(parseContent(plain).every((s) => s.type === 'text' && s.history.length === 0 && s.liveState === null)).toBe(true)
  })

  it('treats an original with nothing after it as ordinary text', () => {
    const body = JSON.stringify({ type: 'sections', content: [{ type: 'textSection', textState: 'Original', content: [paragraph('alone')] }] })

    const [only] = parseContent(body)

    expect(only).toMatchObject({ type: 'text', history: [], liveState: null })
    expect(hasContent(only)).toBe(true)
  })

  it('does not take an improved text with no original before it for a group', () => {
    const body = JSON.stringify({ type: 'sections', content: [{ type: 'textSection', textState: 'Improved', content: [paragraph('stray')] }] })

    expect(parseContent(body)[0]).toMatchObject({ type: 'text', history: [], liveState: null })
  })

  it('lets the earlier versions go and leaves what readers see', () => {
    const improved = withImprovement(textSection('mine'), docOf('mine'), docOf('better'), AT)

    expect(withoutHistory(improved)).toMatchObject({ doc: docOf('better'), history: [], liveState: null })
    expect(stored([withoutHistory(improved)]).content).toHaveLength(1)
  })

  it('does nothing to a section that is not text', () => {
    expect(withImprovement(imageSection(), docOf('a'), docOf('b'))).toEqual(imageSection())
    expect(withoutHistory(imageSection())).toEqual(imageSection())
  })

  it('moving a section moves its kept versions with it', () => {
    const improved = withImprovement(textSection('mine'), docOf('mine'), docOf('better'), AT)
    const moved = moveSection([textSection('first'), improved], improved.key, -1)

    expect(parseContent(serializeContent(moved))[0]).toMatchObject({ doc: docOf('better') })
    expect(stored(moved).content.map((s) => s.textState)).toEqual(['Original', 'Improved', undefined])
  })
})
