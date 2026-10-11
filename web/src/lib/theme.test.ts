import { describe, expect, it } from 'vitest'
import {
  applyRememberedLook, applySiteLook, chooseReaderColors, currentLook, paint, pickerColors, readerColors, rememberedLook, toLook, toTheme,
} from './theme'

function fakeStorage(initial: Record<string, string> = {}) {
  const data = new Map(Object.entries(initial))
  return {
    data,
    getItem: (key: string) => data.get(key) ?? null,
    setItem: (key: string, value: string) => { data.set(key, value) },
    removeItem: (key: string) => { data.delete(key) },
  }
}

const broken = {
  getItem: () => { throw new Error('blocked') },
  setItem: () => { throw new Error('blocked') },
  removeItem: () => { throw new Error('blocked') },
}

function fakeRoot() {
  const styles = new Map<string, string>()
  return {
    dataset: {} as Record<string, string | undefined>,
    styles,
    style: {
      setProperty: (name: string, value: string) => { styles.set(name, value) },
      removeProperty: (name: string) => styles.delete(name),
    },
  }
}

const PLUM = { main: '#7a1f5c', background: '#fff8f0' }
const NIGHT = { main: '#58c79b', background: '#101418' }

describe('toTheme', () => {
  it('accepts the ready-made themes', () => {
    expect(toTheme('blue')).toBe('blue')
    expect(toTheme('seagreen')).toBe('seagreen')
  })

  it.each([undefined, null, '', 'neon', 'SeaGreen', 42, 'custom', 'seagreen"; background:url(x)'])('falls back to the default for %s', (value) => {
    expect(toTheme(value)).toBe('blue')
  })
})

describe('toLook', () => {
  it('keeps a custom theme with two usable colours', () => {
    expect(toLook('custom', { main: '#7A1F5C', background: '#fff8f0' })).toEqual({ theme: 'custom', colors: PLUM })
  })

  it.each([
    [undefined], [null], [{ main: '#7a1f5c' }], [{ main: 'red', background: '#ffffff' }], [{ main: '#7a1f5c', background: '#808080' }],
  ])('treats a custom theme with colours %j as the default theme', (colors) => {
    expect(toLook('custom', colors)).toEqual({ theme: 'blue' })
  })

  it('ignores colours sent with a ready-made theme', () => {
    expect(toLook('seagreen', PLUM)).toEqual({ theme: 'seagreen' })
  })
})

describe('paint', () => {
  it('marks the page with a ready-made theme and sets no colours of its own', () => {
    const page = fakeRoot()

    paint({ theme: 'seagreen' }, page)

    expect(page.dataset.theme).toBe('seagreen')
    expect(page.styles.size).toBe(0)
  })

  it('expresses the default theme as no mark at all', () => {
    const page = fakeRoot()
    paint({ theme: 'seagreen' }, page)

    paint({ theme: 'blue' }, page)

    expect('theme' in page.dataset).toBe(false)
  })

  it('sets every colour for a custom look', () => {
    const page = fakeRoot()

    paint({ theme: 'custom', colors: PLUM }, page)

    expect(page.dataset.theme).toBe('custom')
    expect(page.styles.get('--bg')).toBe('#fff8f0')
    expect(page.styles.get('--chrome')).toBe('#7a1f5c')
    expect(page.styles.get('color-scheme')).toBe('light')
    expect(page.styles.size).toBe(20)
  })

  it('leaves none of a custom look behind when a ready-made theme replaces it', () => {
    const page = fakeRoot()
    paint({ theme: 'custom', colors: NIGHT }, page)

    paint({ theme: 'seagreen' }, page)

    expect(page.styles.size).toBe(0)
    expect(page.dataset.theme).toBe('seagreen')
  })
})

describe('the site\'s look', () => {
  it('is worn and remembered when the API answers', () => {
    const page = fakeRoot()
    const storage = fakeStorage()

    expect(applySiteLook('seagreen', null, page, storage)).toEqual({ theme: 'seagreen' })

    expect(page.dataset.theme).toBe('seagreen')
    expect(rememberedLook(storage)).toEqual({ theme: 'seagreen' })
  })

  it('remembers the owner\'s own colours for the next visit', () => {
    const storage = fakeStorage()
    applySiteLook('custom', PLUM, fakeRoot(), storage)
    const nextVisit = fakeRoot()

    applyRememberedLook(nextVisit, storage)

    expect(nextVisit.styles.get('--chrome')).toBe('#7a1f5c')
    expect(rememberedLook(storage)).toEqual({ theme: 'custom', colors: PLUM })
  })

  it('still understands a theme remembered by an earlier version of the site', () => {
    expect(rememberedLook(fakeStorage({ 'articles.theme': 'seagreen' }))).toEqual({ theme: 'seagreen' })
  })

  it('is the default for a first visit, a tampered value, or blocked storage', () => {
    expect(rememberedLook(fakeStorage())).toEqual({ theme: 'blue' })
    expect(rememberedLook(fakeStorage({ 'articles.theme': '<script>' }))).toEqual({ theme: 'blue' })
    expect(rememberedLook(fakeStorage({ 'articles.theme': '{"theme":"custom","colors":{"main":"red;x","background":"#fff"}}' }))).toEqual({ theme: 'blue' })
    expect(rememberedLook(fakeStorage({ 'articles.theme': '{broken' }))).toEqual({ theme: 'blue' })
    expect(rememberedLook(broken)).toEqual({ theme: 'blue' })
    expect(rememberedLook(null)).toEqual({ theme: 'blue' })
  })

  it('never puts an unknown theme or unusable colours on the page', () => {
    const page = fakeRoot()

    applySiteLook('neon', null, page, fakeStorage())
    applySiteLook('custom', { main: '#7a1f5c', background: 'url(x)' }, page, fakeStorage())

    expect('theme' in page.dataset).toBe(false)
    expect(page.styles.size).toBe(0)
  })

  it('still themes the page when storage is blocked or missing', () => {
    const page = fakeRoot()

    applySiteLook('seagreen', null, page, broken)
    expect(page.dataset.theme).toBe('seagreen')

    applySiteLook('custom', PLUM, page, null)
    expect(page.styles.get('--bg')).toBe('#fff8f0')
  })
})

describe('the reader\'s own colours', () => {
  it('are worn at once and remembered in this browser', () => {
    const page = fakeRoot()
    const storage = fakeStorage()

    expect(chooseReaderColors(NIGHT, page, storage)).toBe(true)

    expect(page.styles.get('--bg')).toBe('#101418')
    expect(readerColors(storage)).toEqual(NIGHT)
    expect(currentLook(storage)).toEqual({ theme: 'custom', colors: NIGHT })
  })

  it('win over the site\'s look, on this visit and the next', () => {
    const storage = fakeStorage()
    chooseReaderColors(NIGHT, fakeRoot(), storage)
    const page = fakeRoot()

    applyRememberedLook(page, storage)
    expect(page.styles.get('--bg')).toBe('#101418')

    applySiteLook('seagreen', null, page, storage)
    expect(page.styles.get('--bg')).toBe('#101418')
    expect(page.dataset.theme).toBe('custom')
    expect(rememberedLook(storage)).toEqual({ theme: 'seagreen' })
  })

  it('give way to the site\'s look again when the reader resets', () => {
    const page = fakeRoot()
    const storage = fakeStorage()
    applySiteLook('seagreen', null, page, storage)
    chooseReaderColors(NIGHT, page, storage)

    expect(chooseReaderColors(null, page, storage)).toBe(true)

    expect(page.dataset.theme).toBe('seagreen')
    expect(page.styles.size).toBe(0)
    expect(readerColors(storage)).toBeNull()
  })

  it('return to the owner\'s custom colours, not the default, when that is the site\'s look', () => {
    const page = fakeRoot()
    const storage = fakeStorage()
    applySiteLook('custom', PLUM, page, storage)
    chooseReaderColors(NIGHT, page, storage)

    chooseReaderColors(null, page, storage)

    expect(page.styles.get('--bg')).toBe('#fff8f0')
  })

  it.each([
    [{ main: '#58c79b', background: '#808080' }], [{ main: 'green', background: '#ffffff' }], [{ main: '#58c79b' }], ['#58c79b'], [undefined],
  ])('are refused, changing nothing, when they are %j', (colors) => {
    const page = fakeRoot()
    const storage = fakeStorage()
    applySiteLook('seagreen', null, page, storage)

    expect(chooseReaderColors(colors, page, storage)).toBe(false)

    expect(page.dataset.theme).toBe('seagreen')
    expect(page.styles.size).toBe(0)
    expect(readerColors(storage)).toBeNull()
  })

  it('are ignored if what is stored has been tampered with', () => {
    expect(readerColors(fakeStorage({ 'articles.reader-colors': '{"main":"#58c79b","background":"#101418;}*{display:none"}' }))).toBeNull()
    expect(readerColors(fakeStorage({ 'articles.reader-colors': 'seagreen' }))).toBeNull()
    expect(readerColors(broken)).toBeNull()
  })

  it('still apply for this visit when storage is blocked', () => {
    const page = fakeRoot()

    expect(chooseReaderColors(NIGHT, page, broken)).toBe(true)
    expect(page.styles.get('--bg')).toBe('#101418')

    expect(chooseReaderColors(null, page, broken, { theme: 'seagreen' })).toBe(true)
    expect(page.dataset.theme).toBe('seagreen')
  })
})

describe('pickerColors', () => {
  it('starts from the reader\'s own colours if they have any', () => {
    const storage = fakeStorage()
    chooseReaderColors(NIGHT, fakeRoot(), storage)

    expect(pickerColors(storage)).toEqual(NIGHT)
  })

  it('otherwise starts from the site\'s colours, or the nearest thing to its ready-made theme', () => {
    const custom = fakeStorage()
    applySiteLook('custom', PLUM, fakeRoot(), custom)
    const green = fakeStorage()
    applySiteLook('seagreen', null, fakeRoot(), green)

    expect(pickerColors(custom)).toEqual(PLUM)
    expect(pickerColors(green)).toEqual({ main: '#17694a', background: '#ffffff' })
    expect(pickerColors(fakeStorage())).toEqual({ main: '#185fa5', background: '#f2f2f1' })
  })
})
