// Pure helpers for the Cloudflare program in front of the public site (see index.js). Nothing here
// touches the network, so every rule can be unit tested.

const UUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'
const STORED_IMAGE = new RegExp(`^/api/v1/images/(${UUID})$`, 'i')
const MEDIA_PATH = new RegExp(`^/media/(${UUID})$`, 'i')
const IMAGE_ID = new RegExp(`^${UUID}$`, 'i')

/** The only kinds of file the picture address will ever serve, whatever the API answers with. */
export const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp']

/** How long a remembered preview is used before it is checked against the API again in the background. */
export const PREVIEW_FRESH_MS = 5 * 60 * 1000
const SLUG = /^[a-z0-9][a-z0-9-]{0,119}$/

export const DESCRIPTION_LENGTH = 200

/** A person is waiting for the page, so the preview gets only a moment before the page is sent without it. */
export const READER_TIMEOUT_MS = 2500

/** Nobody is waiting on a preview fetcher or crawler, and the preview is the whole reason it came. */
export const FETCHER_TIMEOUT_MS = 20000

const FETCHERS = /bot|crawl|spider|preview|facebookexternalhit|whatsapp|telegram|discord|slack|embedly|skype|pinterest|vkshare|redditbot|twitter|linkedin/i

/**
 * How long to wait for the API before sending a post page without its preview. The API sleeps when
 * idle and can take a while to wake, so services that fetch a link to build a preview card are
 * given much longer than a reader would tolerate.
 */
export function timeoutFor(userAgent) {
  return FETCHERS.test(String(userAgent ?? '')) ? FETCHER_TIMEOUT_MS : READER_TIMEOUT_MS
}

/** Text placed inside HTML or XML, whether between tags or inside a quoted attribute. */
export function escapeText(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
    // Characters XML does not allow at all would make a feed reader reject the whole file.
    .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g, '')
}

/** The short code that ends a post's address, e.g. the "k3x9p" of "garden-notes-k3x9p" (the API's alphabet). */
const CODE = /^[2-9a-hjkmnp-z]{5}$/
const NAME_AND_CODE = /^(.+)-([2-9a-hjkmnp-z]{5})$/

/** First parts of an address that already mean something else; the site's own pages, never a category or a post. */
export const RESERVED_SEGMENTS = ['read', 'p', 'media', 'assets', 'tag', 'search', 'privacy', 'subscribe', 'unsubscribe', 'api', 'rss', 'sitemap', 'latest', 'admin']

const decode = (part) => {
  try {
    return decodeURIComponent(part)
  } catch {
    return null
  }
}

/**
 * The post address in a URL path, or null if it is not shaped like one (so it is never sent to the
 * API). Posts are read at /<category>/<name>/<code>, /<name>/<code> (no category) or
 * /<category>/<address> (the first posts have no code). /read/<address> is the plain form, and
 * /p/<address> is where posts used to live; both still open them. The category is only for readers:
 * the post is found without it, so moving a post never breaks a shared link.
 */
export function slugFromPath(pathname) {
  const parts = String(pathname).replace(/\/$/, '').split('/')
  if (parts[0] !== '' || parts.length < 3 || parts.length > 4) return null
  const [, first, second, third] = parts.map((part) => (part === '' ? part : decode(part)))
  if (!first || !second || (parts.length === 4 && !third)) return null

  let slug
  if (parts.length === 3 && (first === 'read' || first === 'p')) slug = second
  else if (RESERVED_SEGMENTS.includes(first) || !SLUG.test(first)) return null
  else if (parts.length === 4) slug = CODE.test(third) ? `${second}-${third}` : null
  else slug = CODE.test(second) ? `${first}-${second}` : second

  return slug && SLUG.test(slug) ? slug : null
}

/**
 * The one true address of a post, as a path: the same rule the site uses to build its links. With
 * no category, or a first part that would be mistaken for one of the site's own pages, /read/<address>.
 */
export function postPathFor(post) {
  const slug = post.slug
  const category = typeof post.category?.slug === 'string' && SLUG.test(post.category.slug) ? post.category.slug : null
  const match = NAME_AND_CODE.exec(slug)
  if (match) {
    if (category) return `/${category}/${match[1]}/${match[2]}`
    if (!RESERVED_SEGMENTS.includes(match[1])) return `/${match[1]}/${match[2]}`
  } else if (category) {
    return `/${category}/${slug}`
  }
  return `/read/${slug}`
}

/** The picture id in a /media/<id> address, or null if the address is not exactly that shape. */
export function mediaIdFromPath(pathname) {
  const match = MEDIA_PATH.exec(pathname)
  return match ? match[1].toLowerCase() : null
}

/** Where an old /p/<post> address should send people, or null if the path is not one. */
export function oldPostRedirect(pathname) {
  if (!/^\/p\/[^/]+\/?$/.test(pathname)) return null
  const slug = slugFromPath(pathname)
  return slug ? `/read/${slug}` : null
}

/** The first picture inside a post body, used for the preview when the post has no cover. */
export function firstImageId(contentJson) {
  let root
  try {
    root = JSON.parse(contentJson)
  } catch {
    return null
  }
  const sections = root && Array.isArray(root.content) ? root.content : []
  for (const section of sections) {
    const id = section && section.type === 'imageSection' ? section.attrs?.imageId : null
    if (typeof id === 'string' && IMAGE_ID.test(id)) return id.toLowerCase()
  }
  return null
}

/** True once a remembered preview is old enough to be checked again. Anything without a readable time counts as old. */
export function isStale(fetchedAt, now = Date.now()) {
  const at = typeof fetchedAt === 'number' ? fetchedAt : NaN
  return !(now - at < PREVIEW_FRESH_MS)
}

/** The words of a stored post body, in order, with formatting dropped. Picture captions are left out: they label a picture, they are not the article. */
export function plainText(contentJson) {
  let root
  try {
    root = JSON.parse(contentJson)
  } catch {
    return ''
  }

  const parts = []
  const walk = (node) => {
    if (!node || typeof node !== 'object') return
    if (typeof node.text === 'string') parts.push(node.text)
    if (Array.isArray(node.content)) {
      node.content.forEach(walk)
      // Blocks are separate sentences, not one run-on word.
      parts.push(' ')
    }
  }
  walk(root)
  return parts.join('').replace(/\s+/g, ' ').trim()
}

/** Shortens text at a word boundary. */
export function summarise(text, max = DESCRIPTION_LENGTH) {
  const clean = String(text ?? '').replace(/\s+/g, ' ').trim()
  if (clean.length <= max) return clean
  const cut = clean.slice(0, max)
  const space = cut.lastIndexOf(' ')
  return `${(space > max * 0.6 ? cut.slice(0, space) : cut).replace(/[\s.,;:!?-]+$/, '')}…`
}

/**
 * A picture address a preview may use: one uploaded to this site (served from the site's own
 * /media/ address, which Cloudflare keeps a copy of), or an https link. Anything else is dropped.
 */
export function absoluteImage(value, siteOrigin) {
  if (typeof value !== 'string' || value.trim() === '') return null
  const trimmed = value.trim()
  const stored = STORED_IMAGE.exec(trimmed)
  if (stored) return `${siteOrigin}/media/${stored[1].toLowerCase()}`
  try {
    const url = new URL(trimmed)
    return url.protocol === 'https:' ? url.toString() : null
  } catch {
    return null
  }
}

/** The logo card shown when a shared link has no picture. A file in web/public. */
export const DEFAULT_SHARE_IMAGE = '/articles-share.png'

/** What a shared link to a post should show. */
export function describePost(post, { siteName, siteOrigin }) {
  // The cover if there is one, otherwise the first picture in the article.
  const inArticle = firstImageId(post.contentJson)
  // With no picture of its own, a post is shared with the site's logo card instead of a bare text preview.
  const image = absoluteImage(post.coverImageUrl, siteOrigin) ?? (inArticle ? `${siteOrigin}/media/${inArticle}` : `${siteOrigin}${DEFAULT_SHARE_IMAGE}`)
  const description = summarise(post.subtitle || plainText(post.contentJson) || `An article on ${siteName}.`)
  return {
    title: `${post.title} · ${siteName}`,
    heading: post.title,
    description,
    // Always the same address, so a post shared through an old /read/ or /p/ link is still treated as one page.
    url: `${siteOrigin}${postPathFor(post)}`,
    image,
    author: post.author?.displayName ?? null,
    publishedAt: post.publishedAt ?? null,
  }
}

/** The tags added to the page's head. Every value is escaped; nothing from a post reaches the page as markup. */
export function headTags(meta, siteName) {
  const tag = (attribute, name, content) => (content ? `<meta ${attribute}="${name}" content="${escapeText(content)}">` : '')
  return [
    `<link rel="canonical" href="${escapeText(meta.url)}">`,
    tag('name', 'description', meta.description),
    tag('property', 'og:type', 'article'),
    tag('property', 'og:site_name', siteName),
    tag('property', 'og:title', meta.heading),
    tag('property', 'og:description', meta.description),
    tag('property', 'og:url', meta.url),
    tag('property', 'og:image', meta.image),
    tag('property', 'article:published_time', meta.publishedAt),
    tag('name', 'author', meta.author),
    tag('name', 'twitter:card', meta.image ? 'summary_large_image' : 'summary'),
    tag('name', 'twitter:title', meta.heading),
    tag('name', 'twitter:description', meta.description),
    tag('name', 'twitter:image', meta.image),
  ].filter(Boolean).join('')
}

const published = (posts) => posts.filter((post) => post && typeof post.slug === 'string' && SLUG.test(post.slug))

const isoDate = (value) => {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}

export function buildSitemap(posts, siteOrigin) {
  const entry = (loc, lastmod) => `<url><loc>${escapeText(loc)}</loc>${lastmod ? `<lastmod>${lastmod}</lastmod>` : ''}</url>`
  const urls = [
    entry(`${siteOrigin}/`),
    ...published(posts).map((post) => entry(`${siteOrigin}${postPathFor(post)}`, isoDate(post.publishedAt))),
    entry(`${siteOrigin}/privacy`),
  ]
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">${urls.join('')}</urlset>\n`
}

/** The few newest posts as JSON, for another site (the portfolio) to list. Only what a link needs. */
export const LATEST_COUNT = 3

export function buildLatest(posts, { siteName, siteOrigin }, limit = LATEST_COUNT) {
  const items = published(posts)
    .slice(0, limit)
    .map((post) => ({
      title: String(post.title ?? ''),
      url: `${siteOrigin}${postPathFor(post)}`,
      date: isoDate(post.publishedAt),
      summary: summarise(post.subtitle || post.excerpt || ''),
    }))
  return JSON.stringify({ site: siteName, url: `${siteOrigin}/`, items })
}

/** Sites allowed to read /latest.json from the browser: the portfolio, with or without www. */
export const LATEST_READERS = ['https://ahmadnadeem.dev', 'https://www.ahmadnadeem.dev']

/** The CORS header for a request from `origin`, or none: only the listed sites may read the file. */
export function corsHeadersFor(origin, allowed = LATEST_READERS) {
  return allowed.includes(origin) ? { 'Access-Control-Allow-Origin': origin, Vary: 'Origin' } : { Vary: 'Origin' }
}

export function buildRss(posts, { siteName, siteOrigin, description }) {
  const items = published(posts).map((post) => {
    const link = `${siteOrigin}${postPathFor(post)}`
    const date = new Date(post.publishedAt)
    return [
      '<item>',
      `<title>${escapeText(post.title)}</title>`,
      `<link>${escapeText(link)}</link>`,
      `<guid isPermaLink="true">${escapeText(link)}</guid>`,
      Number.isNaN(date.getTime()) ? '' : `<pubDate>${date.toUTCString()}</pubDate>`,
      `<description>${escapeText(summarise(post.subtitle || post.excerpt || ''))}</description>`,
      ...(Array.isArray(post.tags) ? post.tags.map((tag) => `<category>${escapeText(tag.name)}</category>`) : []),
      '</item>',
    ].join('')
  })

  return [
    '<?xml version="1.0" encoding="UTF-8"?>\n',
    '<rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom"><channel>',
    `<title>${escapeText(siteName)}</title>`,
    `<link>${escapeText(`${siteOrigin}/`)}</link>`,
    `<description>${escapeText(description)}</description>`,
    '<language>en</language>',
    `<atom:link href="${escapeText(`${siteOrigin}/rss.xml`)}" rel="self" type="application/rss+xml"/>`,
    ...items,
    '</channel></rss>\n',
  ].join('')
}
