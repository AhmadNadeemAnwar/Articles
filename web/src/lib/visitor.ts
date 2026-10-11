/**
 * Readers have no accounts, so each browser makes up a random id for itself the first time it
 * reacts to a post or is counted as a reader. It is what lets a second click undo a clap, and it
 * keeps a refresh from counting as another view. It is not tied to a person: clearing site data
 * simply produces a new one. The API stores only a hash of it.
 */
const KEY = 'articles.visitor'
const ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

// Used when storage is unavailable (private mode, blocked site data): the id then lasts for this page only.
let memory: string | null = null

export function newVisitorId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID()

  // Older browsers: build a version-4 id from random bytes.
  const bytes = new Uint8Array(16)
  crypto.getRandomValues(bytes)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

export function getVisitorId(storage: Pick<Storage, 'getItem' | 'setItem'> | null = safeStorage()): string {
  try {
    const stored = storage?.getItem(KEY)
    // Anything that is not a well-formed id (edited by hand, left by an older version) is replaced.
    if (stored && ID.test(stored)) return stored
  } catch {
    // Reading can throw when site data is blocked; fall through to a fresh id.
  }

  const id = memory ?? newVisitorId()
  memory = id
  try {
    storage?.setItem(KEY, id)
  } catch {
    // Not saved: this visitor keeps the same id until the page is reloaded.
  }
  return id
}

function safeStorage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

/** Test hook: forget the in-memory id so each test starts as a new visitor. */
export function resetVisitorForTests() {
  memory = null
}
