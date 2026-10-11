import { describe, expect, it } from 'vitest'
import type { PostSummary } from '../api/types'
import { mergePosts } from '../hooks/usePagedPosts'
import { normalise, pageTitle, postDisplayPath, postPath, slugFromParams } from './site'

describe('pageTitle', () => {
  it('puts the page first and the site second', () => {
    expect(pageTitle('What I learned')).toBe('What I learned · Articles')
  })

  it('is just the site name when the page has no name yet', () => {
    expect(pageTitle()).toBe('Articles')
    expect(pageTitle(null)).toBe('Articles')
    expect(pageTitle('   ')).toBe('Articles')
  })

  it('tidies stray spacing and line breaks in a title', () => {
    expect(pageTitle('  Two\n  lines ')).toBe('Two lines · Articles')
  })
})

describe('portfolio address', () => {
  it('is off when nothing is set, so no dead link is shown', () => {
    expect(normalise(undefined)).toBeNull()
    expect(normalise('')).toBeNull()
    expect(normalise('   ')).toBeNull()
  })

  it('accepts an https address and drops a trailing slash', () => {
    expect(normalise('https://ahmadnadeem.dev/')).toBe('https://ahmadnadeem.dev')
  })

  it.each(['http://ahmadnadeem.dev', 'javascript:alert(1)', 'ahmadnadeem.dev', '//ahmadnadeem.dev'])('stays off for %s', (value) => {
    expect(normalise(value)).toBeNull()
  })
})

describe('mergePosts', () => {
  const post = (id: string) => ({ id, title: id }) as PostSummary
  const ids = (posts: PostSummary[]) => posts.map((p) => p.id)

  it('adds the next page after what is already shown', () => {
    expect(ids(mergePosts([post('a'), post('b')], [post('c'), post('d')]))).toEqual(['a', 'b', 'c', 'd'])
  })

  it('drops a post that is already on screen', () => {
    // A new post was published between page 1 and page 2, so "b" slid onto page 2.
    expect(ids(mergePosts([post('a'), post('b')], [post('b'), post('c')]))).toEqual(['a', 'b', 'c'])
  })

  it('copes with an empty first page or an empty next page', () => {
    expect(ids(mergePosts([], [post('a')]))).toEqual(['a'])
    expect(ids(mergePosts([post('a')], []))).toEqual(['a'])
  })

  it('does not change the list it was given', () => {
    const shown = [post('a')]

    mergePosts(shown, [post('b')])

    expect(ids(shown)).toEqual(['a'])
  })
})

describe('postPath', () => {
  it('is /<category>/<name>/<code> for a post in a category', () => {
    expect(postPath('today-for-tomorrow-k3x9p', 'life-and-lessons')).toBe('/life-and-lessons/today-for-tomorrow/k3x9p')
  })

  it('is /<name>/<code> for a post in no category', () => {
    expect(postPath('today-for-tomorrow-k3x9p')).toBe('/today-for-tomorrow/k3x9p')
    expect(postPath('today-for-tomorrow-k3x9p', null)).toBe('/today-for-tomorrow/k3x9p')
  })

  it('keeps an older post, whose address has no code, readable', () => {
    expect(postPath('today-for-tomorrow')).toBe('/read/today-for-tomorrow')
    expect(postPath('today-for-tomorrow', 'life-and-lessons')).toBe('/life-and-lessons/today-for-tomorrow')
  })

  it('does not start with a word that is one of the site’s own pages', () => {
    expect(postPath('tag-k3x9p')).toBe('/read/tag-k3x9p')
    expect(postPath('privacy-k3x9p')).toBe('/read/privacy-k3x9p')
  })

  it('never lets a strange address break out of the path', () => {
    expect(postPath('a/b?c')).toBe('/read/a%2Fb%3Fc')
  })
})

describe('slugFromParams', () => {
  it('finds the post address in every shape of address', () => {
    expect(slugFromParams({ slug: 'today-for-tomorrow-k3x9p' })).toBe('today-for-tomorrow-k3x9p')
    expect(slugFromParams({ category: 'life', name: 'today-for-tomorrow', code: 'k3x9p' })).toBe('today-for-tomorrow-k3x9p')
    expect(slugFromParams({ first: 'today-for-tomorrow', second: 'k3x9p' })).toBe('today-for-tomorrow-k3x9p')
    expect(slugFromParams({ first: 'life', second: 'today-for-tomorrow' })).toBe('today-for-tomorrow')
  })

  it('leads back to the same post from whatever postPath makes', () => {
    const cases: [string, string | null][] = [['today-for-tomorrow-k3x9p', 'life'], ['today-for-tomorrow-k3x9p', null], ['today-for-tomorrow', 'life'], ['tag-k3x9p', null]]
    for (const [slug, category] of cases) {
      const parts = postPath(slug, category).split('/').slice(1)
      const params = parts[0] === 'read' ? { slug: parts[1] } : parts.length === 3 ? { category: parts[0], name: parts[1], code: parts[2] } : { first: parts[0], second: parts[1] }
      expect(slugFromParams(params)).toBe(slug)
    }
  })

  it('is empty when there is nothing to go on', () => {
    expect(slugFromParams({})).toBe('')
  })
})

describe('postDisplayPath', () => {
  it('reads Articles/<category>/<post name>', () => {
    expect(postDisplayPath('Today for Tomorrow', 'Life and Lessons')).toBe('Articles/Life and Lessons/Today for Tomorrow')
  })

  it('is Articles/<post name> when the post has no category', () => {
    expect(postDisplayPath('Today for Tomorrow')).toBe('Articles/Today for Tomorrow')
    expect(postDisplayPath('Today for Tomorrow', null)).toBe('Articles/Today for Tomorrow')
  })
})
