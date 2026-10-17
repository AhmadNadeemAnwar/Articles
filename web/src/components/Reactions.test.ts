import { describe, expect, it } from 'vitest'
import { afterPress, reactionLabel } from './Reactions'

describe('reactionLabel', () => {
  it('says what the icon means and how many readers gave it', () => {
    expect(reactionLabel('Clap', 0, false)).toBe('Clap: 0 readers')
    expect(reactionLabel('Clap', 1, false)).toBe('Clap: 1 reader')
    expect(reactionLabel('Insightful', 12, false)).toBe('Insightful: 12 readers')
  })

  it('tells a reader when they are one of them', () => {
    expect(reactionLabel('Clap', 1, true)).toBe('Clap: 1 reader, including you')
    expect(reactionLabel('Insightful', 4, true)).toBe('Insightful: 4 readers, including you')
  })
})

describe('afterPress', () => {
  const none = { clapCount: 4, insightfulCount: 2, clapped: false, markedInsightful: false }

  it('fills the clap and adds one straight away, and takes it back on a second press', () => {
    const once = afterPress(none, 'clap')
    expect(once).toEqual({ ...none, clapped: true, clapCount: 5 })
    expect(afterPress(once, 'clap')).toEqual(none)
  })

  it('does the same for insightful, leaving the clap alone', () => {
    expect(afterPress(none, 'insightful')).toEqual({ ...none, markedInsightful: true, insightfulCount: 3 })
  })

  it('never shows a count below zero', () => {
    expect(afterPress({ ...none, clapped: true, clapCount: 0 }, 'clap').clapCount).toBe(0)
  })
})
