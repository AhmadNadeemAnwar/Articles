import { describe, expect, it } from 'vitest'
import { countLabel, withAny } from './DashboardPage'
import { commentsClause } from './PostsPage'

describe('countLabel', () => {
  it('says one view or clap, and more than one in the plural', () => {
    expect(countLabel(1, 'views')).toBe('1 view')
    expect(countLabel(1, 'claps')).toBe('1 clap')
    expect(countLabel(0, 'claps')).toBe('0 claps')
    expect(countLabel(14, 'views')).toBe('14 views')
  })
})

describe('withAny', () => {
  const post = (id: string, views: number, claps: number) => ({ id, title: id, slug: id, views, claps }) as never

  it('leaves out posts with none of what the list ranks', () => {
    const posts = [post('a', 5, 1), post('b', 0, 0), post('c', 2, 0)]

    expect(withAny(posts, 'claps').map((p: { id: string }) => p.id)).toEqual(['a'])
    expect(withAny(posts, 'views').map((p: { id: string }) => p.id)).toEqual(['a', 'c'])
  })
})

describe('commentsClause', () => {
  it('says nothing about comments when there are none, and counts them properly when there are', () => {
    expect(commentsClause(0)).toBe('')
    expect(commentsClause(1)).toBe(' and its comment')
    expect(commentsClause(3)).toBe(' and all 3 of its comments')
  })
})
