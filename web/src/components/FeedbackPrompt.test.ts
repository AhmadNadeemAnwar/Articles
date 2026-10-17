import { describe, expect, it } from 'vitest'
import { LIKED_OPTIONS, MAX_FEEDBACK, canSend, feedbackHandled } from './FeedbackPrompt'

describe('canSend', () => {
  it('needs something liked or a few words', () => {
    expect(canSend([], '')).toBe(false)
    expect(canSend([], '   ')).toBe(false)
    expect(canSend(['useful'], '')).toBe(true)
    expect(canSend([], 'More examples please')).toBe(true)
  })

  it('refuses words over the limit', () => {
    expect(canSend([], 'x'.repeat(MAX_FEEDBACK))).toBe(true)
    expect(canSend([], 'x'.repeat(MAX_FEEDBACK + 1))).toBe(false)
  })
})

describe('feedbackHandled', () => {
  const store = (value: string | null) => ({ getItem: () => value })

  it('is true once the card was sent or closed for that post, and false before', () => {
    expect(feedbackHandled('p1', store('sent'))).toBe(true)
    expect(feedbackHandled('p1', store('closed'))).toBe(true)
    expect(feedbackHandled('p1', store(null))).toBe(false)
  })

  it('offers the card when storage cannot be read', () => {
    expect(feedbackHandled('p1', { getItem: () => { throw new Error('blocked') } })).toBe(false)
    expect(feedbackHandled('p1', null)).toBe(false)
  })
})

describe('LIKED_OPTIONS', () => {
  it('are the four choices the API accepts', () => {
    expect(LIKED_OPTIONS.map((o) => o.key)).toEqual(['clear-writing', 'useful', 'made-me-think', 'personal'])
  })
})
