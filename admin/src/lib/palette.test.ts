import { describe, expect, it } from 'vitest'
import { backgroundProblem, paletteFrom } from './palette'
import here from './palette.ts?raw'
import site from '../../../web/src/lib/palette.ts?raw'

describe('the palette code', () => {
  it('is the same file the public site uses, so the preview here shows what readers will get', () => {
    const tidy = (text: string) => text.replace(/\r\n/g, '\n')

    expect(tidy(here)).toBe(tidy(site))
  })

  it('works out a full set of colours from three', () => {
    const palette = paletteFrom({ main: '#7a1f5c', accent: '#7a1f5c', background: '#fff8f0' })

    expect(palette['--bg']).toBe('#fff8f0')
    expect(palette['--chrome']).toBe('#7a1f5c')
    expect(palette['--text']).toBe('#1c1e21')
  })

  it('refuses a background text cannot be read on', () => {
    expect(backgroundProblem('#808080')).toMatch(/lighter or a darker/)
    expect(backgroundProblem('#ffffff')).toBeNull()
  })
})
