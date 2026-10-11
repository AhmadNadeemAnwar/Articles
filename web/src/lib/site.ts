export const SITE_NAME = 'Articles'

/**
 * The owner's main site. Empty until that site is reachable: the link, and the contact line on the
 * privacy page, appear only when this is set (VITE_PORTFOLIO_URL in .env.production), so readers are
 * never sent to an address that does not load.
 */
export const PORTFOLIO_URL = normalise(import.meta.env.VITE_PORTFOLIO_URL as string | undefined)

export function normalise(value: string | undefined): string | null {
  const trimmed = (value ?? '').trim().replace(/\/+$/, '')
  if (trimmed === '') return null
  try {
    return new URL(trimmed).protocol === 'https:' ? trimmed : null
  } catch {
    return null
  }
}

/** What the browser tab and history show: the page first, the site second. */
export function pageTitle(page?: string | null): string {
  const clean = (page ?? '').replace(/\s+/g, ' ').trim()
  return clean === '' ? SITE_NAME : `${clean} · ${SITE_NAME}`
}

// A post's address ends in a short code (see the API's SlugGenerator): "garden-notes-k3x9p".
const CODE = /^[2-9a-hjkmnp-z]{5}$/
const NAME_AND_CODE = /^(.+)-([2-9a-hjkmnp-z]{5})$/

/** First parts of an address that already mean something else. A post never takes one of these as its first part. */
export const RESERVED_SEGMENTS: readonly string[] = ['read', 'p', 'media', 'assets', 'tag', 'search', 'privacy', 'subscribe', 'unsubscribe', 'api', 'rss', 'sitemap', 'latest', 'admin']

/**
 * Where a post is read: /<category>/<name>/<code>, or /<name>/<code> with no category. A post with
 * no code in its address (the first ones had none) is /<category>/<address>, or /read/<address>.
 * The category part is for readers; the post is found by its name and code, so moving a post to
 * another category never breaks a link already shared. /read/<address> always still opens it.
 */
export function postPath(slug: string | null, category?: string | null): string {
  const address = slug ?? ''
  const match = NAME_AND_CODE.exec(address)
  const shelf = category ? encodeURIComponent(category) : null
  if (match) {
    const [, name, code] = match
    if (shelf) return `/${shelf}/${encodeURIComponent(name)}/${code}`
    if (!RESERVED_SEGMENTS.includes(name)) return `/${encodeURIComponent(name)}/${code}`
  } else if (shelf) {
    return `/${shelf}/${encodeURIComponent(address)}`
  }
  return `/read/${encodeURIComponent(address)}`
}

/** The post address hidden in the pieces of a post's URL, whichever of its shapes it came in. */
export function slugFromParams(params: Record<string, string | undefined>): string {
  if (params.slug) return params.slug
  if (params.name && params.code) return `${params.name}-${params.code}`
  if (params.first && params.second) return CODE.test(params.second) ? `${params.first}-${params.second}` : params.second
  return ''
}
