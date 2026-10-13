/**
 * A post body as the editor sees it: an ordered list of sections the writer can add, remove and
 * move. This file is the only place that knows how that list is stored, and it has no React in it
 * so every rule here can be unit tested.
 *
 * Stored shape (checked again by the API before it is saved):
 *   { type: "sections", content: [
 *       { type: "textSection", content: [...rich text blocks] },
 *       { type: "imageSection", attrs: { imageId, alt, caption, width?, height? } },
 *       { type: "referencesSection", attrs: { items: [{ title, url }] } } ] }
 *
 * Posts written before sections existed are a single { type: "doc" } and open as one text section.
 *
 * When the writer applies "Improve text with AI", the wording it replaced is kept in the body, as text sections placed
 * before the one readers see, each with a "textState":
 *       { type: "textSection", textState: "Original", content: [...] }                         what the writer wrote
 *       { type: "textSection", textState: "Improved", content: [...] }                         the first improvement
 *       { type: "textSection", textState: "Improved_2026-10-14T10:30:00Z", content: [...] }    each later one
 * The editor shows such a group as ONE section (the newest text, with the earlier versions folded away below it), and
 * readers see only the newest. Moving or deleting the section moves or deletes the whole group.
 */

export interface Reference {
  title: string
  url: string
}

export type Section =
  /**
   * `doc` is the text readers see. `history` holds earlier versions kept beside it, oldest first (the first is the
   * writer's original); `liveState` is the state `doc` is stored under, which is null for a section never improved.
   */
  | { key: string; type: 'text'; doc: string; history: KeptVersion[]; liveState: string | null }
  /** `width` and `height` are the stored picture's size in pixels, or null for pictures added before sizes were kept. */
  | { key: string; type: 'image'; imageId: string | null; alt: string; caption: string; width: number | null; height: number | null }
  | { key: string; type: 'references'; items: Reference[] }

export type SectionType = Section['type']

/** An earlier version of a text section: the editor's own JSON for it, and the state it is stored under. */
export interface KeptVersion {
  state: string
  doc: string
}

/** The most earlier versions kept for one section (the first, the writer's original, is never the one dropped). */
export const MAX_KEPT_VERSIONS = 10

export const ORIGINAL = 'Original'
export const IMPROVED = 'Improved'
const IMPROVED_AT = /^Improved_\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$/

/** "Improved", or "Improved_" and a time: one of the AI's versions. */
export const isImprovedState = (state: unknown): state is string => state === IMPROVED || (typeof state === 'string' && IMPROVED_AT.test(state))

/** The state for an improvement made at `at`, to the second: "Improved_2026-10-14T10:30:00Z". */
export const improvedStateAt = (at: Date) => `${IMPROVED}_${at.toISOString().replace(/\.\d+Z$/, 'Z')}`

export const MAX_SECTIONS = 100
export const MAX_REFERENCES = 50
export const MAX_CAPTION = 300
export const MAX_REFERENCE_TITLE = 200

export const EMPTY_DOC = '{"type":"doc","content":[{"type":"paragraph"}]}'

export const sectionLabels: Record<SectionType, string> = { text: 'Text', image: 'Image', references: 'References' }

let counter = 0
/** Unique within this page for as long as it is open; never stored. */
const newKey = () => `s${Date.now().toString(36)}${(counter++).toString(36)}`

export function newSection(type: SectionType): Section {
  switch (type) {
    case 'text': return { key: newKey(), type, doc: EMPTY_DOC, history: [], liveState: null }
    case 'image': return { key: newKey(), type, imageId: null, alt: '', caption: '', width: null, height: null }
    case 'references': return { key: newKey(), type, items: [{ title: '', url: '' }] }
  }
}

const text = (value: unknown): string => (typeof value === 'string' ? value : '')
const isObject = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export const MAX_IMAGE_DIMENSION = 20_000

/** A picture dimension the API will accept, or null. */
export function toDimension(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= MAX_IMAGE_DIMENSION ? value : null
}

/** Turns a stored body into sections. Anything unreadable opens as one empty text section rather than failing. */
export function parseContent(json: string | null | undefined): Section[] {
  let root: unknown
  try {
    root = JSON.parse(json ?? '')
  } catch {
    return [newSection('text')]
  }
  if (!isObject(root)) return [newSection('text')]

  if (root.type === 'doc') {
    const content = Array.isArray(root.content) && root.content.length > 0 ? root.content : [{ type: 'paragraph' }]
    return [{ key: newKey(), type: 'text', doc: JSON.stringify({ type: 'doc', content }), history: [], liveState: null }]
  }

  if (root.type !== 'sections' || !Array.isArray(root.content)) return [newSection('text')]

  const sections: Section[] = []
  // The versions of one improved section are stored side by side, oldest first; they are gathered here and become one section.
  let group: KeptVersion[] | null = null

  const closeGroup = () => {
    if (!group) return
    const live = group[group.length - 1]
    // A lone original, with no improved text after it, is just text: it opens as an ordinary section.
    if (group.length === 1) sections.push({ key: newKey(), type: 'text', doc: live.doc, history: [], liveState: null })
    else sections.push({ key: newKey(), type: 'text', doc: live.doc, history: group.slice(0, -1), liveState: live.state })
    group = null
  }

  for (const raw of root.content) {
    if (!isObject(raw)) continue
    const attrs = isObject(raw.attrs) ? raw.attrs : {}

    if (raw.type === 'textSection') {
      const content = Array.isArray(raw.content) && raw.content.length > 0 ? raw.content : [{ type: 'paragraph' }]
      const doc = JSON.stringify({ type: 'doc', content })
      const state = raw.textState

      if (state === ORIGINAL) {
        closeGroup()
        group = [{ state: ORIGINAL, doc }]
        continue
      }
      if (group && isImprovedState(state)) {
        group.push({ state, doc })
        continue
      }
      closeGroup()
      sections.push({ key: newKey(), type: 'text', doc, history: [], liveState: null })
      continue
    }
    closeGroup()

    if (raw.type === 'imageSection') {
      const imageId = text(attrs.imageId)
      const width = toDimension(attrs.width)
      const height = toDimension(attrs.height)
      // A size is only useful, and only accepted by the API, as a pair.
      const sized = width !== null && height !== null
      sections.push({
        key: newKey(), type: 'image', imageId: GUID.test(imageId) ? imageId : null, alt: text(attrs.alt), caption: text(attrs.caption),
        width: sized ? width : null, height: sized ? height : null,
      })
    } else if (raw.type === 'referencesSection') {
      const items = Array.isArray(attrs.items) ? attrs.items.filter(isObject).map((item) => ({ title: text(item.title), url: text(item.url) })) : []
      sections.push({ key: newKey(), type: 'references', items })
    }
  }

  closeGroup()
  return sections.length > 0 ? sections : [newSection('text')]
}

function docContent(doc: string): unknown[] {
  try {
    const parsed: unknown = JSON.parse(doc)
    if (isObject(parsed) && Array.isArray(parsed.content) && parsed.content.length > 0) return parsed.content
  } catch {
    // Falls through to an empty paragraph.
  }
  return [{ type: 'paragraph' }]
}

/**
 * The body to store. Unfinished pieces are left out rather than sent, because the API would refuse
 * the whole save over them: an image section with no picture yet, and sources with no title.
 */
export function serializeContent(sections: Section[]): string {
  const content: unknown[] = []

  for (const section of sections) {
    if (section.type === 'text') {
      // Earlier versions go first, oldest first, then the text readers see: that is the order the API checks.
      const kept = section.history.length > 0
      for (const version of kept ? section.history : []) content.push({ type: 'textSection', textState: version.state, content: docContent(version.doc) })
      const state = kept ? (isImprovedState(section.liveState) ? section.liveState : section.history.length === 1 ? IMPROVED : improvedStateAt(new Date())) : null
      content.push({ type: 'textSection', ...(state ? { textState: state } : {}), content: docContent(section.doc) })
    } else if (section.type === 'image') {
      if (!section.imageId) continue
      const width = toDimension(section.width)
      const height = toDimension(section.height)
      content.push({
        type: 'imageSection',
        attrs: {
          imageId: section.imageId, alt: section.alt.trim(), caption: section.caption.trim(),
          ...(width !== null && height !== null ? { width, height } : {}),
        },
      })
    } else {
      const items = section.items
        .map((item) => ({ title: item.title.trim(), url: item.url.trim() }))
        .filter((item) => item.title !== '')
      if (items.length === 0) continue
      content.push({ type: 'referencesSection', attrs: { items } })
    }
  }

  return JSON.stringify({ type: 'sections', content })
}

/**
 * The section after the writer has applied an improvement: what was there before is kept as an earlier version, and `doc` is the
 * new text. The first time, the writer's own wording is kept as "Original" and the new text is "Improved"; every later time, the
 * text it replaces is kept under the state it had and the new text is "Improved_" and the time it was made.
 */
export function withImprovement(section: Section, previousDoc: string, newDoc: string, at: Date = new Date()): Section {
  if (section.type !== 'text') return section

  let history: KeptVersion[]
  let liveState: string
  if (section.history.length === 0) {
    history = [{ state: ORIGINAL, doc: previousDoc }]
    liveState = IMPROVED
  } else {
    history = [...section.history, { state: isImprovedState(section.liveState) ? section.liveState : IMPROVED, doc: previousDoc }]
    liveState = improvedStateAt(at)
  }

  // Keep the original and the newest few; the oldest improvements are the ones let go.
  while (history.length > MAX_KEPT_VERSIONS) history.splice(1, 1)
  return { ...section, doc: newDoc, history, liveState }
}

/** The section with its earlier versions let go: what readers see stays as it is. */
export function withoutHistory(section: Section): Section {
  return section.type === 'text' ? { ...section, history: [], liveState: null } : section
}

/** Moves a section one place up (-1) or down (+1). Returns the same array when it cannot move. */
export function moveSection(sections: Section[], key: string, direction: -1 | 1): Section[] {
  const from = sections.findIndex((section) => section.key === key)
  const to = from + direction
  if (from < 0 || to < 0 || to >= sections.length) return sections

  const next = [...sections]
  ;[next[from], next[to]] = [next[to], next[from]]
  return next
}

export function removeSection(sections: Section[], key: string): Section[] {
  return sections.filter((section) => section.key !== key)
}

export function updateSection(sections: Section[], key: string, change: (section: Section) => Section): Section[] {
  return sections.map((section) => (section.key === key ? change(section) : section))
}

/** True when deleting the section would throw away something the writer made. */
export function hasContent(section: Section): boolean {
  switch (section.type) {
    case 'text': return hasText(docContent(section.doc))
    case 'image': return section.imageId !== null
    case 'references': return section.items.some((item) => item.title.trim() !== '' || item.url.trim() !== '')
  }
}

function hasText(nodes: unknown[]): boolean {
  return nodes.some((node) => {
    if (!isObject(node)) return false
    if (node.type === 'horizontalRule') return true
    if (typeof node.text === 'string' && node.text.trim() !== '') return true
    return Array.isArray(node.content) && hasText(node.content)
  })
}

export function isWebAddress(value: string): boolean {
  try {
    const url = new URL(value.trim())
    return (url.protocol === 'https:' || url.protocol === 'http:') && url.hostname !== ''
  } catch {
    return false
  }
}

/** How many pictures in the post have no description for people who cannot see them. */
export function countUndescribedImages(sections: Section[]): number {
  return sections.filter((section) => section.type === 'image' && section.imageId !== null && section.alt.trim() === '').length
}

/** The first thing that would stop a save, in words the writer can act on, or null if nothing would. */
export function findProblem(sections: Section[]): string | null {
  if (sections.length > MAX_SECTIONS) return `A post can have at most ${MAX_SECTIONS} sections.`

  for (const section of sections) {
    if (section.type === 'image') {
      if (section.alt.length > MAX_CAPTION || section.caption.length > MAX_CAPTION) return `Image descriptions and captions can be at most ${MAX_CAPTION} characters.`
    } else if (section.type === 'references') {
      if (section.items.length > MAX_REFERENCES) return `A reference section can list at most ${MAX_REFERENCES} sources.`
      for (const item of section.items) {
        const title = item.title.trim()
        const url = item.url.trim()
        if (title === '' && url !== '') return 'A source has a link but no title. Give it a title or clear the link.'
        if (title.length > MAX_REFERENCE_TITLE) return `A source title can be at most ${MAX_REFERENCE_TITLE} characters.`
        if (url !== '' && !isWebAddress(url)) return `The link for “${title}” must start with http:// or https://.`
      }
    }
  }

  return null
}
