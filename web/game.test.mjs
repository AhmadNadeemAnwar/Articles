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
