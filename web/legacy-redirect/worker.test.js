import { describe, expect, it } from 'vitest'
import worker, { redirectTarget } from './worker.js'

const NEW = 'https://articles.ahmadnadeem.dev'
const ask = (url, method = 'GET') => worker.fetch(new Request(url, { method }))

describe('redirectTarget', () => {
  it('keeps the path and the query', () => {
    expect(redirectTarget('https://inkwell.ahmadnadeem.dev/read/today-for-tomorrow')).toBe(`${NEW}/read/today-for-tomorrow`)
    expect(redirectTarget('https://inkwell.ahmadnadeem.dev/search?q=faith&page=2')).toBe(`${NEW}/search?q=faith&page=2`)
    expect(redirectTarget('https://inkwell.ahmadnadeem.dev/')).toBe(`${NEW}/`)
    expect(redirectTarget('https://inkwell.ahmadnadeem.dev')).toBe(`${NEW}/`)
  })

  it.each([
    ['/p/an-old-post', '/p/an-old-post'],
    ['/media/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11', '/media/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11'],
    ['/rss.xml', '/rss.xml'],
    ['/sitemap.xml', '/sitemap.xml'],
    ['/subscribe/confirm?token=abc.def', '/subscribe/confirm?token=abc.def'],
    ['/r%C3%A9sum%C3%A9', '/r%C3%A9sum%C3%A9'],
  ])('sends %s to the same address on the new site', (path, expected) => {
    expect(redirectTarget(`https://inkwell.ahmadnadeem.dev${path}`)).toBe(`${NEW}${expected}`)
  })

  it.each(['//evil.example/x', '//evil.example', '/\evil.example', '/%2F%2Fevil.example', '///evil.example/x', '/@evil.example', '/..//evil.example'])(
    'never turns the path %s into a link to another site', (path) => {
      const to = redirectTarget(`https://inkwell.ahmadnadeem.dev${path}`)
      expect(new URL(to).origin).toBe(NEW)
    })
})

describe('the Worker', () => {
  it('answers a read with a permanent redirect', async () => {
    const response = await ask('https://inkwell.ahmadnadeem.dev/read/a-post?x=1')

    expect(response.status).toBe(301)
    expect(response.headers.get('Location')).toBe(`${NEW}/read/a-post?x=1`)
    expect(response.headers.get('Cache-Control')).toBe('public, max-age=3600')
    expect(await response.text()).toBe('')
  })

  it('does the same for a HEAD request', async () => {
    expect((await ask('https://inkwell.ahmadnadeem.dev/', 'HEAD')).status).toBe(301)
  })

  it.each(['POST', 'PUT', 'DELETE'])('keeps the method on a %s with a 308', async (method) => {
    const response = await ask('https://inkwell.ahmadnadeem.dev/api/x', method)

    expect(response.status).toBe(308)
    expect(response.headers.get('Location')).toBe(`${NEW}/api/x`)
  })

  it('has no setting that could send visitors anywhere else', async () => {
    const response = await worker.fetch(new Request('https://inkwell.ahmadnadeem.dev/a'), { TARGET: 'https://evil.example' })

    expect(response.headers.get('Location')).toBe(`${NEW}/a`)
  })
})
