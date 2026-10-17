import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8')

/** The game page must run under the site's strict content policy: no script inside the page, no inline event handlers, no eval. */
describe('the game page', () => {
  const html = read('./public/play/index.html')
  const js = read('./public/play/game.js')

  it('loads its script and styles from files on this site', () => {
    expect(html).toContain('<script src="/play/game.js" defer></script>')
    expect(html).toContain('<link rel="stylesheet" href="/play/game.css" />')
  })

  it('has no script written inside the page and no inline event handlers', () => {
    expect(html.match(/<script(?![^>]*\ssrc=)[^>]*>/g)).toBeNull()
    expect(html).not.toMatch(/\son[a-z]+\s*=/i)
    expect(js).not.toMatch(/\son[a-z]+=\?["']/)
  })

  it('never evaluates text as code', () => {
    expect(js).not.toMatch(/\beval\s*\(|new Function\s*\(|setTimeout\s*\(\s*['"`]/)
  })

  it('has a way back to the articles and the site name in its title', () => {
    expect(html).toContain('href="/"')
    expect(html).toContain('<title>Catch the Ball · Articles')
  })
})

describe('the game in a window on the site', () => {
  const headers = read('./public/_headers').split(String.fromCharCode(13)).join('')
  const js = read('./public/play/game.js')

  it('lets pages show only this site in a frame, and lets only this site frame the game', () => {
    expect(headers).toMatch(/\/\*\n\s+Content-Security-Policy:[^\n]*frame-src 'self';[^\n]*frame-ancestors 'none'/)
    expect(headers).toMatch(/\/play\/\*\n\s+! Content-Security-Policy\n\s+! X-Frame-Options\n\s+Content-Security-Policy:[^\n]*frame-ancestors 'self'[^\n]*\n\s+X-Frame-Options: SAMEORIGIN/)
  })

  it('still runs: the line it adds before the game ends its statement, so the two are not read as one', async () => {
    const vm = await import('node:vm')
    const calls = []
    const sandbox = {
      location: { search: '?embed=1' }, URLSearchParams,
      document: { documentElement: { classList: { add: (c) => calls.push(c) } }, getElementById: () => { throw new Error('stop') } },
    }
    expect(() => new vm.Script(js).runInNewContext(sandbox)).toThrow('stop')
    expect(calls).toEqual(['embedded'])
  })
})
