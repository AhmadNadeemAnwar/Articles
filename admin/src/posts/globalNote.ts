/** Small rules for the note shown on every post. No React, so they can be unit tested. */

export const MAX_NOTE_LENGTH = 1000

/** The note as the server will keep it: line endings made alike, outer space dropped. */
export function tidyNote(text: string): string {
  return text.replace(/\r\n?/g, '\n').trim()
}

/** What the Save button should do, given what is saved and what is typed. */
export function noteState(saved: string, draft: string) {
  const tidy = tidyNote(draft)
  const tooLong = tidy.length > MAX_NOTE_LENGTH
  const changed = tidy !== tidyNote(saved)
  return {
    tooLong,
    changed,
    canSave: changed && !tooLong,
    /** Clearing a note that exists removes it from every post, so it is worth saying so. */
    removing: changed && tidy === '' && tidyNote(saved) !== '',
    left: MAX_NOTE_LENGTH - tidy.length,
  }
}
