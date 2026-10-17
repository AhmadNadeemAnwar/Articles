import { describe, expect, it } from 'vitest'
import { likedLabel } from './FeedbackPage'

describe('likedLabel', () => {
  it('names each choice a reader can tick', () => {
    expect(['clear-writing', 'useful', 'made-me-think', 'personal'].map(likedLabel)).toEqual(['Clear writing', 'Useful', 'Made me think', 'Personal'])
  })

  it('shows a choice it does not know as it was stored', () => {
    expect(likedLabel('something-new')).toBe('something-new')
  })
})
