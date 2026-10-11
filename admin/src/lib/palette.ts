/**
 * A custom theme is three colours: a header colour, an accent for links and buttons, and a page background.
 * Everything else the stylesheet needs (text, borders, hover shades, the colours used on top of
 * the header) is worked out here, and each one that carries text is pushed darker or lighter until
 * it can be read. So whatever three colours are chosen, the site stays readable.
 *
 * The admin portal keeps an identical copy of this file to preview a theme before saving it.
 */

export interface ThemeColors {
  /** The header, as #rrggbb. */
  main: string
  /** Links and buttons, as #rrggbb. */
  accent: string
  /** The page behind the text, as #rrggbb. */
  background: string
}

/** Body text must stand out from the page by at least this much (WCAG AAA, for long reading). */
export const MIN_TEXT_CONTRAST = 7
/** Everything else that is read: links, buttons, small print (WCAG AA). */
export const MIN_CONTRAST = 4.5
/** How far greys (borders, small print, quiet bands) lean toward the main colour, so they belong to the theme. */
export const TINT = 0.1

// The API measures a background against this same pair before it will store it.
const DARK_TEXT = '#1c1e21'
const LIGHT_TEXT = '#ececee'
const WHITE = '#ffffff'
const BLACK = '#000000'

const HEX = /^#[0-9a-f]{6}$/

/** A colour exactly as #rrggbb, lower-cased, or null. Nothing else is ever put into the page's styles. */
export function toHex(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const clean = value.trim().toLowerCase()
  return HEX.test(clean) ? clean : null
}

function channels(hex: string): [number, number, number] {
  return [1, 3, 5].map((at) => parseInt(hex.slice(at, at + 2), 16)) as [number, number, number]
}

function luminance(hex: string): number {
  const [r, g, b] = channels(hex).map((value) => {
    const unit = value / 255
    return unit <= 0.04045 ? unit / 12.92 : ((unit + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** The WCAG contrast ratio of two colours, from 1 (identical) to 21 (black on white). */
export function contrast(first: string, second: string): number {
  const [a, b] = [luminance(first), luminance(second)]
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

/** `from` moved `amount` (0 to 1) of the way towards `to`. */
export function mix(from: string, to: string, amount: number): string {
  const [a, b] = [channels(from), channels(to)]
  return `#${a.map((value, i) => Math.round(value + (b[i] - value) * amount).toString(16).padStart(2, '0')).join('')}`
}

/** Moves a colour towards `towards` in small steps until it reaches `minimum` contrast on every surface. */
function readable(colour: string, surfaces: string[], minimum: number, towards: string): string {
  for (let step = 0; step <= 20; step++) {
    const candidate = mix(colour, towards, step / 20)
    if (surfaces.every((surface) => contrast(candidate, surface) >= minimum)) return candidate
  }
  return towards
}

/** Why a background cannot be used, or null if it can. Mid-tones are refused: neither dark nor light text reads well on them. */
export function backgroundProblem(background: string): string | null {
  return Math.max(contrast(background, DARK_TEXT), contrast(background, LIGHT_TEXT)) >= MIN_TEXT_CONTRAST
    ? null
    : 'Text would be hard to read on that background. Choose a lighter or a darker one.'
}

/**
 * Usable colours, or null if one is not a plain colour or the background cannot carry text. The accent is
 * optional, because colours saved when there were two (here, and by the API) had links in the main colour.
 */
export function toColors(value: unknown): ThemeColors | null {
  if (typeof value !== 'object' || value === null) return null
  const main = toHex((value as Record<string, unknown>).main)
  const background = toHex((value as Record<string, unknown>).background)
  if (!main || !background || backgroundProblem(background)) return null
  const rawAccent = (value as Record<string, unknown>).accent
  const accent = rawAccent === undefined || rawAccent === null ? main : toHex(rawAccent)
  return accent ? { main, accent, background } : null
}

/** Every colour the stylesheet uses, by custom-property name, worked out from the three chosen colours. */
export function paletteFrom(colors: ThemeColors): Record<string, string> {
  const bg = colors.background
  const dark = contrast(bg, LIGHT_TEXT) > contrast(bg, DARK_TEXT)
  const text = dark ? LIGHT_TEXT : DARK_TEXT
  const extreme = dark ? WHITE : BLACK

  // Raised cards sit a step lighter than the page; a quiet band takes a hint of the main colour.
  const bgRaised = dark ? mix(mix(bg, WHITE, 0.07), colors.main, 0.04) : mix(bg, WHITE, 0.6)
  const bgSubtle = dark ? mix(mix(bg, WHITE, 0.035), colors.main, 0.06) : mix(mix(bg, BLACK, 0.025), colors.main, 0.05)
  const toward = (grey: string) => mix(grey, colors.main, TINT)
  const surfaces = [bg, bgRaised, bgSubtle]

  // The accent is also read on top of its own pale tint (an active button), so that tint is a surface too.
  let accent = colors.accent
  let accentSoft = mix(bg, accent, 0.1)
  for (let step = 0; step <= 20; step++) {
    accent = mix(colors.accent, extreme, step / 20)
    accentSoft = mix(bg, accent, 0.1)
    if ([...surfaces, accentSoft].every((surface) => contrast(accent, surface) >= MIN_CONTRAST)) break
  }

  // The header wears the main colour as it is; what sits on it flips between white and black to stay readable.
  const chrome = colors.main
  const chromeText = contrast(chrome, WHITE) >= MIN_CONTRAST ? WHITE : BLACK
  // The search box is set into the header: a step away from the text colour, so the text only gains contrast there.
  const chromeRaised = mix(chrome, chromeText === WHITE ? BLACK : WHITE, 0.14)
  const onChrome = [chrome, chromeRaised]

  return {
    'color-scheme': dark ? 'dark' : 'light',

    '--bg': bg,
    '--bg-subtle': bgSubtle,
    '--bg-raised': bgRaised,

    '--border': toward(mix(bg, text, 0.13)),
    '--border-strong': toward(mix(bg, text, 0.26)),

    '--text': text,
    '--text-muted': readable(toward(mix(text, bg, 0.35)), surfaces, MIN_CONTRAST, text),
    '--text-faint': readable(toward(mix(text, bg, 0.5)), surfaces, MIN_CONTRAST, text),

    '--chrome': chrome,
    '--chrome-raised': chromeRaised,
    '--chrome-text': chromeText,
    '--chrome-muted': readable(mix(chromeText, chrome, 0.25), onChrome, MIN_CONTRAST, chromeText),
    '--chrome-border': mix(chrome, chromeText, 0.2),

    '--accent': accent,
    '--accent-hover': mix(accent, extreme, 0.2),
    '--accent-soft': accentSoft,
    '--accent-chrome': readable(mix(chromeText, chrome, 0.12), onChrome, MIN_CONTRAST, chromeText),
    '--danger': readable(dark ? '#e58270' : '#a8331f', surfaces, MIN_CONTRAST, text),

    '--shadow': dark
      ? '0 1px 2px rgba(0, 0, 0, 0.4), 0 6px 18px rgba(0, 0, 0, 0.35)'
      : '0 1px 2px rgba(31, 36, 46, 0.05), 0 6px 16px rgba(31, 36, 46, 0.06)',
  }
}
