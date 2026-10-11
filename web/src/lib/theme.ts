import { paletteFrom, toColors } from './palette'
import type { ThemeColors } from './palette'

/**
 * How the site looks. Two things decide it:
 *
 *   the site's look    chosen by the owner in the admin portal: a ready-made theme, or two colours
 *                      of their own. It arrives from the API a moment after the page starts.
 *   the reader's look  two colours a reader picked for themselves with the palette button. It is
 *                      kept in this browser only and, while it exists, wins over the site's.
 *
 * Both are remembered in this browser and applied before anything is drawn, so a returning reader
 * never sees the page change colour, and a first-time reader sees the default until the API answers.
 */
export const THEMES = ['blue', 'seagreen'] as const
export type Theme = (typeof THEMES)[number]

export const DEFAULT_THEME: Theme = 'blue'

export type Look = { theme: Theme } | { theme: 'custom'; colors: ThemeColors }

const DEFAULT_LOOK: Look = { theme: DEFAULT_THEME }
const SITE_KEY = 'articles.theme'
const READER_KEY = 'articles.reader-colors'

/** Roughly what each ready-made theme looks like, so the reader's pickers start from what is on screen. */
const NEAREST: Record<Theme, ThemeColors> = {
  blue: { main: '#185fa5', background: '#f2f2f1' },
  seagreen: { main: '#17694a', background: '#ffffff' },
}

/** Only a theme this build has styles for is ever applied; anything else is the default. */
export function toTheme(value: unknown): Theme {
  return typeof value === 'string' && (THEMES as readonly string[]).includes(value) ? (value as Theme) : DEFAULT_THEME
}

/** A look that can safely be put on the page. A custom theme without two usable colours is the default. */
export function toLook(theme: unknown, colors?: unknown): Look {
  if (theme !== 'custom') return { theme: toTheme(theme) }
  const clean = toColors(colors)
  return clean ? { theme: 'custom', colors: clean } : DEFAULT_LOOK
}

type Store = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>
interface Root {
  dataset: Record<string, string | undefined>
  style: { setProperty(name: string, value: string): void; removeProperty(name: string): unknown }
}

function safeStorage(): Store | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

function read(storage: Store | null, key: string): unknown {
  try {
    const raw = storage?.getItem(key)
    if (raw === null || raw === undefined) return null
    // The site's look used to be remembered as a bare theme name; that is still understood.
    return raw.startsWith('{') ? JSON.parse(raw) : raw
  } catch {
    return null
  }
}

/** The site's look as this browser last saw it. */
export function rememberedLook(storage: Store | null = safeStorage()): Look {
  const stored = read(storage, SITE_KEY)
  if (typeof stored === 'string') return toLook(stored)
  if (typeof stored === 'object' && stored !== null) return toLook((stored as Record<string, unknown>).theme, (stored as Record<string, unknown>).colors)
  return DEFAULT_LOOK
}

/** The colours this reader chose for themselves, or null if they follow the site. */
export function readerColors(storage: Store | null = safeStorage()): ThemeColors | null {
  return toColors(read(storage, READER_KEY))
}

const CUSTOM_PROPERTIES = Object.keys(paletteFrom(NEAREST.blue))

/** Puts a look on the page. The default is no attribute at all; a custom look sets each colour directly. */
export function paint(look: Look, root: Root = document.documentElement): void {
  if (look.theme === DEFAULT_THEME) delete root.dataset.theme
  else root.dataset.theme = look.theme

  if (look.theme === 'custom') {
    for (const [name, value] of Object.entries(paletteFrom(look.colors))) root.style.setProperty(name, value)
  } else {
    for (const name of CUSTOM_PROPERTIES) root.style.removeProperty(name)
  }
}

/** What this browser should wear right now: the reader's own colours if they chose any, otherwise the site's look. */
export function currentLook(storage: Store | null = safeStorage()): Look {
  const own = readerColors(storage)
  return own ? { theme: 'custom', colors: own } : rememberedLook(storage)
}

/** Before the first paint: wear what this browser wore last time. */
export function applyRememberedLook(root: Root = document.documentElement, storage: Store | null = safeStorage()): Look {
  const look = currentLook(storage)
  paint(look, root)
  return look
}

/** The API has answered with the owner's choice: remember it, and wear it unless the reader chose their own. */
export function applySiteLook(theme: unknown, colors: unknown, root: Root = document.documentElement, storage: Store | null = safeStorage()): Look {
  const site = toLook(theme, colors)
  try {
    storage?.setItem(SITE_KEY, site.theme === 'custom' ? JSON.stringify(site) : site.theme)
  } catch {
    // Not remembered: the next visit starts from the default until the API answers.
  }
  return applyRememberedLookOr(site, root, storage)
}

function applyRememberedLookOr(site: Look, root: Root, storage: Store | null): Look {
  const own = readerColors(storage)
  const look: Look = own ? { theme: 'custom', colors: own } : site
  paint(look, root)
  return look
}

/**
 * The reader picked their own colours, or (with null) went back to the site's. Returns false, and
 * changes nothing, when the colours cannot be used. `site` is the look to return to; it defaults to
 * the remembered one.
 */
export function chooseReaderColors(
  colors: unknown,
  root: Root = document.documentElement,
  storage: Store | null = safeStorage(),
  site: Look = rememberedLook(storage),
): boolean {
  if (colors === null) {
    try {
      storage?.removeItem(READER_KEY)
    } catch {
      // Nothing was stored, or storage is blocked: either way the page goes back to the site's look.
    }
    paint(site, root)
    return true
  }

  const clean = toColors(colors)
  if (!clean) return false
  try {
    storage?.setItem(READER_KEY, JSON.stringify(clean))
  } catch {
    // Not remembered: the colours last until the page is closed.
  }
  paint({ theme: 'custom', colors: clean }, root)
  return true
}

/** What the reader's two pickers show when opened: their own colours, else the nearest thing to what is on screen. */
export function pickerColors(storage: Store | null = safeStorage()): ThemeColors {
  const look = currentLook(storage)
  return look.theme === 'custom' ? look.colors : NEAREST[look.theme]
}
