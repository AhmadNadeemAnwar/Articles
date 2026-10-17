import { describe, expect, it } from 'vitest'
import { newSection, parseContent, serializeContent } from './sections'
import type { Section } from './sections'

const IMAGE_ID = '0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11'
const image = (size: 'small' | 'medium' | 'full'): Section => ({ key: 'i', type: 'image', imageId: IMAGE_ID, alt: 'A chart', caption: '', width: 1600, height: 900, size })
const storedAttrs = (sections: Section[]) => (JSON.parse(serializeContent(sections)) as { content: { attrs: Record<string, unknown> }[] }).content[0].attrs

describe('picture size in the article', () => {
  it('starts full width', () => {
    expect(newSection('image')).toMatchObject({ size: 'full' })
  })

  it('stores small and medium, and leaves full width out so older posts stay exactly as they were', () => {
    expect(storedAttrs([image('small')]).size).toBe('small')
    expect(storedAttrs([image('medium')]).size).toBe('medium')
    expect(storedAttrs([image('full')])).not.toHaveProperty('size')
  })

  it('keeps the picture itself and its real size whatever size it is shown at', () => {
    expect(storedAttrs([image('small')])).toMatchObject({ imageId: IMAGE_ID, width: 1600, height: 900 })
  })

  it('opens each size again, and anything else (or nothing) as full width', () => {
    const body = (size: unknown) => JSON.stringify({ type: 'sections', content: [{ type: 'imageSection', attrs: { imageId: IMAGE_ID, alt: '', caption: '', size } }] })

    expect(parseContent(body('small'))[0]).toMatchObject({ size: 'small' })
    expect(parseContent(body('medium'))[0]).toMatchObject({ size: 'medium' })
    for (const odd of [undefined, 'huge', 5, null]) expect(parseContent(body(odd))[0]).toMatchObject({ size: 'full' })
  })
})
