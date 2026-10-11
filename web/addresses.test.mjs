import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { RESERVED_SEGMENTS as WORKER } from './worker/meta.js'

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8')

/** A post's address starts with its category, so the words that are the site's own pages are kept in one list in three places. */
describe('the words a category may not take', () => {
  it('are the same in the site, the Worker and the API', () => {
    const site = /RESERVED_SEGMENTS: readonly string\[\] = \[([^\]]+)\]/.exec(read('./src/lib/site.ts'))
    const api = /ReservedAddresses =\s*\[([^\]]+)\]/.exec(read('../src/Articles.Application/Site/SiteService.cs'))
    const words = (match) => match[1].split(',').map((word) => word.trim().replace(/['"]/g, ''))

    expect(site).not.toBeNull()
    expect(api).not.toBeNull()
    expect(words(site)).toEqual(WORKER)
    expect(words(api)).toEqual(WORKER)
  })

  it('include every page the site has', () => {
    const app = read('./src/App.tsx')
    const firstParts = [...app.matchAll(/path="\/([a-z]+)[/"]/g)].map((match) => match[1])

    for (const part of firstParts) expect(WORKER, `/${part}`).toContain(part)
  })
})
