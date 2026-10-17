import { beforeEach, describe, expect, it } from 'vitest'
import { entrySource, referrerHost, resetEntrySource, resetViewerCountry, viewerCountry } from './viewContext'

describe('referrerHost', () => {
  it('keeps only the bare name of another site', () => {
    expect(referrerHost('https://www.google.com/search?q=something+private', 'articles.ahmadnadeem.dev')).toBe('google.com')
    expect(referrerHost('https://linkedin.com/feed/', 'articles.ahmadnadeem.dev')).toBe('linkedin.com')
  })

  it('says nothing for this site, no referrer, or something that is not a web address', () => {
    expect(referrerHost('https://articles.ahmadnadeem.dev/life/a-post/k3x9p', 'articles.ahmadnadeem.dev')).toBe('')
    expect(referrerHost('', 'articles.ahmadnadeem.dev')).toBe('')
    expect(referrerHost('android-app://com.google.android.gm/', 'articles.ahmadnadeem.dev')).toBe('')
    expect(referrerHost('not a url', 'articles.ahmadnadeem.dev')).toBe('')
  })
})

describe('entrySource', () => {
  beforeEach(() => resetEntrySource())

  it('credits the site a reader came from on the first view of a visit only', () => {
    expect(entrySource('https://www.google.com/', 'articles.ahmadnadeem.dev')).toBe('google.com')
    expect(entrySource('https://www.google.com/', 'articles.ahmadnadeem.dev')).toBe('')
  })
})

describe('viewerCountry', () => {
  beforeEach(() => resetViewerCountry())
  const answer = (body: unknown, ok = true) => (async () => ({ ok, json: async () => body })) as unknown as typeof fetch

  it('reads the two-letter code the site front gives', async () => {
    expect(await viewerCountry(answer({ country: 'MY' }))).toBe('MY')
  })

  it('is empty when the answer is missing, odd or failed', async () => {
    expect(await viewerCountry(answer({ country: 'Malaysia' }))).toBe('')
    resetViewerCountry()
    expect(await viewerCountry(answer(null, false))).toBe('')
    resetViewerCountry()
    expect(await viewerCountry((async () => { throw new Error('offline') }) as unknown as typeof fetch)).toBe('')
  })

  it('asks only once per visit', async () => {
    let calls = 0
    const counting = (async () => { calls++; return { ok: true, json: async () => ({ country: 'PK' }) } }) as unknown as typeof fetch
    await viewerCountry(counting)
    await viewerCountry(counting)
    expect(calls).toBe(1)
  })
})
