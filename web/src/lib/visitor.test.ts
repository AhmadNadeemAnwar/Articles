import { beforeEach, describe, expect, it } from 'vitest'
import { getVisitorId, newVisitorId, resetVisitorForTests } from './visitor'

const ID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

function fakeStorage(initial: Record<string, string> = {}) {
  const data = new Map(Object.entries(initial))
  return {
    data,
    getItem: (key: string) => data.get(key) ?? null,
    setItem: (key: string, value: string) => { data.set(key, value) },
  }
}

const broken = {
  getItem: () => { throw new Error('site data is blocked') },
  setItem: () => { throw new Error('site data is blocked') },
}

describe('visitor id', () => {
  beforeEach(() => resetVisitorForTests())

  it('makes a well-formed random id', () => {
    expect(newVisitorId()).toMatch(ID)
    expect(newVisitorId()).not.toBe(newVisitorId())
  })

  it('keeps the same id for a returning visitor', () => {
    const storage = fakeStorage()

    const first = getVisitorId(storage)
    resetVisitorForTests()
    const second = getVisitorId(storage)

    expect(first).toMatch(ID)
    expect(second).toBe(first)
  })

  it('stores the id so the next visit can find it', () => {
    const storage = fakeStorage()

    const id = getVisitorId(storage)

    expect(storage.data.get('articles.visitor')).toBe(id)
  })

  it.each(['', 'not-an-id', '<script>alert(1)</script>', '123'])('replaces a stored value that is not an id: %s', (bad) => {
    const storage = fakeStorage({ 'articles.visitor': bad })

    const id = getVisitorId(storage)

    expect(id).toMatch(ID)
    expect(storage.data.get('articles.visitor')).toBe(id)
  })

  it('still works when storage is blocked, and stays the same for the life of the page', () => {
    const first = getVisitorId(broken)
    const second = getVisitorId(broken)

    expect(first).toMatch(ID)
    expect(second).toBe(first)
  })

  it('still works when there is no storage at all', () => {
    expect(getVisitorId(null)).toMatch(ID)
  })

  it('gives two different browsers two different ids', () => {
    const one = getVisitorId(fakeStorage())
    resetVisitorForTests()
    const two = getVisitorId(fakeStorage())

    expect(one).not.toBe(two)
  })
})
