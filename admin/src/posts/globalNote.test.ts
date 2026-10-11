import { describe, expect, it } from 'vitest'
import { MAX_NOTE_LENGTH, noteState, tidyNote } from './globalNote'

describe('tidyNote', () => {
  it('drops the space around the note and makes the line endings alike', () => {
    expect(tidyNote('  Hello\r\nthere\rfriend \n')).toBe('Hello\nthere\nfriend')
  })
})

describe('noteState', () => {
  it('cannot save what is already saved, or only differs by space', () => {
    expect(noteState('A note', 'A note').canSave).toBe(false)
    expect(noteState('A note', '  A note \r\n').canSave).toBe(false)
    expect(noteState('', '   ').canSave).toBe(false)
  })

  it('can save a first note, a change, or a removal', () => {
    expect(noteState('', 'Hello').canSave).toBe(true)
    expect(noteState('Hello', 'Hello again').canSave).toBe(true)
    expect(noteState('Hello', '').canSave).toBe(true)
  })

  it('says when saving would remove the note from every post', () => {
    expect(noteState('Hello', '  ').removing).toBe(true)
    expect(noteState('', '').removing).toBe(false)
    expect(noteState('Hello', 'Hi').removing).toBe(false)
  })

  it('refuses a note over the limit and counts what is left', () => {
    const over = noteState('', 'x'.repeat(MAX_NOTE_LENGTH + 1))
    expect(over.tooLong).toBe(true)
    expect(over.canSave).toBe(false)
    expect(over.left).toBe(-1)
    expect(noteState('', 'x'.repeat(MAX_NOTE_LENGTH)).canSave).toBe(true)
    expect(noteState('', 'abc').left).toBe(MAX_NOTE_LENGTH - 3)
  })
})
