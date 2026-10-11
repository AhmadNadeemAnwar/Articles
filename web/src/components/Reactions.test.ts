import { describe, expect, it } from 'vitest'
import { reactionLabel } from './Reactions'

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
