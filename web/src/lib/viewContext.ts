/**
 * The two coarse facts sent with a reader's view, for the owner's daily figures: which country they are in (as Cloudflare
 * sees it, two letters, never an address) and the bare name of the site they came from. Nothing else about the reader.
 */

/** The bare name of the site in a referrer address, or empty for this site, no referrer, or anything that is not a web address. */
export function referrerHost(referrer: string, ownHost: string): string {
  if (!referrer) return ''
  try {
    const url = new URL(referrer)
    if (url.protocol !== 'https:' && url.protocol !== 'http:') return ''
    const host = url.hostname.toLowerCase().replace(/^www\./, '')
    const own = ownHost.toLowerCase().replace(/^www\./, '')
    return host === own ? '' : host
  } catch {
    return ''
  }
}

let entryUsed = false

/**
 * Where the reader came from, for the first view of this visit only. Moving from post to post inside the site keeps the
 * same browser referrer, so later views say nothing rather than crediting the first site again.
 */
export function entrySource(referrer = typeof document === 'undefined' ? '' : document.referrer, ownHost = typeof location === 'undefined' ? '' : location.hostname): string {
  if (entryUsed) return ''
  entryUsed = true
  return referrerHost(referrer, ownHost)
}

/** For tests: start a new visit. */
export function resetEntrySource() {
  entryUsed = false
}

let country: Promise<string> | null = null

/** The reader's country code from the site's own Cloudflare front, asked once per visit. Empty when it is not known. */
export function viewerCountry(fetcher: typeof fetch = (...args) => fetch(...args)): Promise<string> {
  country ??= fetcher('/__country', { headers: { Accept: 'application/json' } })
    .then((response) => (response.ok ? response.json() : null))
    .then((body: unknown) => {
      const code = body && typeof body === 'object' ? (body as { country?: unknown }).country : ''
      return typeof code === 'string' && /^[A-Z]{2}$/.test(code) ? code : ''
    })
    .catch(() => '')
  return country
}

/** For tests: forget the remembered country. */
export function resetViewerCountry() {
  country = null
}
