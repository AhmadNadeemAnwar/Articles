// A small Cloudflare Worker in front of the public site. The site itself is a single page that
// builds each post in the browser, which is invisible to anything that does not run scripts:
// link previews (LinkedIn, WhatsApp, Slack), feed readers and some search crawlers. This fills
// that gap, and nothing else:
//
//   /<category>/<name>/<code>, /<name>/<code>, /read/<post>
//                  a post: the normal page, with the post's title, description and picture added to its head.
//                  A post that does not exist, or has been taken down, answers "not found".
//   /p/<post>      where posts used to live: sent on to /read/<post>
//   /media/<id>    a picture from a post, fetched from the API once and then kept by Cloudflare,
//                  so readers are not left waiting on the API for pictures
//   /sitemap.xml   every published post, for search engines
//   /rss.xml       the newest posts, for feed readers
//   /latest.json   the three newest posts, for the portfolio's "Latest writing" (readable only from there)
//
// It runs only for those addresses (see run_worker_first in wrangler.jsonc); every other request is
// served straight from the static files and never reaches this code. If the API is slow or down,
// a post page is served unchanged, so the worst case is a missing preview, never a broken page.
//
// Free plan allowance: 100,000 runs a day. One run per post page opened, plus feed and sitemap fetches.

import {
  IMAGE_TYPES, buildLatest, buildRss, buildSitemap, corsHeadersFor, describePost, headTags, isStale, mediaIdFromPath, oldPostRedirect, slugFromPath, timeoutFor,
} from './meta.js'

const LIST_TIMEOUT_MS = 20000
const MEDIA_TIMEOUT_MS = 25000

// Long enough that a post someone has already opened keeps its preview while the API sleeps. The
// cost is that an edited title or subtitle can take this long to reach new previews.
const POST_CACHE_SECONDS = 6 * 60 * 60
const LIST_CACHE_SECONDS = 600
const MAX_LISTED_POSTS = 500
const RSS_ITEMS = 30
const LATEST_LIMIT = 3

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url)
    const site = {
      siteName: env.SITE_NAME || 'Articles',
      siteOrigin: url.origin,
      apiBase: String(env.API_BASE || '').replace(/\/+$/, ''),
      description: env.SITE_DESCRIPTION || 'Articles by Ahmad Nadeem.',
    }

    if (request.method === 'GET' || request.method === 'HEAD') {
      try {
        if (url.pathname === '/sitemap.xml') {
          return await cachedDocument(request, ctx, 'application/xml; charset=utf-8', async () => buildSitemap(await listPosts(site, MAX_LISTED_POSTS), site.siteOrigin))
        }
        if (url.pathname === '/rss.xml') {
          return await cachedDocument(request, ctx, 'application/rss+xml; charset=utf-8', async () => buildRss(await listPosts(site, RSS_ITEMS), site))
        }
        if (url.pathname === '/latest.json') {
          // The stored copy is shared by every reader, so the CORS answer is added after it, per request.
          const stored = await cachedDocument(request, ctx, 'application/json; charset=utf-8', async () => buildLatest(await listPosts(site, LATEST_LIMIT), site))
          const answer = new Response(stored.body, stored)
          for (const [name, value] of Object.entries(corsHeadersFor(request.headers.get('Origin')))) answer.headers.set(name, value)
          return answer
        }

        const mediaId = mediaIdFromPath(url.pathname)
        if (mediaId) return await media(request, ctx, site, mediaId)
        // Anything else under /media/ is not a picture address; it must not fall through to the page.
        if (url.pathname.startsWith('/media/')) {
          return new Response('Not found\n', { status: 404, headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store' } })
        }

        const moved = oldPostRedirect(url.pathname)
        if (moved) return Response.redirect(`${url.origin}${moved}`, 301)

        const slug = slugFromPath(url.pathname)
        if (slug) return await postPage(request, env, ctx, site, slug)
      } catch (error) {
        if (url.pathname.endsWith('.xml') || url.pathname.endsWith('.json')) {
          // An empty feed, list or sitemap would tell readers and search engines that every post is gone.
          return new Response('Temporarily unavailable. Please try again shortly.\n', {
            status: 503,
            headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Retry-After': '120', 'Cache-Control': 'no-store' },
          })
        }
        if (url.pathname.startsWith('/media/')) {
          return new Response('This picture is temporarily unavailable.\n', {
            status: 503,
            headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Retry-After': '30', 'Cache-Control': 'no-store' },
          })
        }
        console.log('preview unavailable', url.pathname, String(error))
      }
    }

    return env.ASSETS.fetch(request)
  },
}

async function getJson(url, timeoutMs) {
  const response = await fetch(url, { headers: { Accept: 'application/json' }, signal: AbortSignal.timeout(timeoutMs) })
  if (response.status === 404) return null
  if (!response.ok) throw new Error(`API answered ${response.status}`)
  return response.json()
}

async function listPosts(site, limit) {
  const posts = []
  for (let page = 1; posts.length < limit; page++) {
    const pageSize = Math.min(50, limit - posts.length)
    const data = await getJson(`${site.apiBase}/api/v1/posts?sort=Latest&pageSize=${pageSize}&pageNumber=${page}`, LIST_TIMEOUT_MS)
    if (!data || !Array.isArray(data.items)) throw new Error('unexpected list response')
    posts.push(...data.items)
    if (!data.hasNextPage || data.items.length === 0) break
  }
  return posts
}

/** Builds an XML document at most once every few minutes; in between, Cloudflare serves the stored copy. */
async function cachedDocument(request, ctx, contentType, build) {
  const cache = caches.default
  const key = new Request(new URL(request.url).toString(), { method: 'GET' })

  const hit = await cache.match(key)
  if (hit) return hit

  const response = new Response(await build(), {
    headers: {
      'Content-Type': contentType,
      'Cache-Control': `public, max-age=${LIST_CACHE_SECONDS}`,
      'X-Content-Type-Options': 'nosniff',
    },
  })
  ctx.waitUntil(cache.put(key, response.clone()))
  return response
}

/** Marks the API's "there is no such post" answer, as opposed to "could not find out". */
const GONE = Symbol('gone')

// The version is bumped whenever what is stored changes, including the site's name (it is part of each
// saved title). v4: the site became Articles, so entries saved while it was still Articles are not reused.
const metaKey = (slug) => new Request(`https://post-meta.internal/v4/${encodeURIComponent(slug)}`)

/** Asks the API about a post and remembers the answer. Forgets it if the post is gone. */
async function refreshPost(ctx, site, slug, timeoutMs) {
  const cache = caches.default
  const post = await getJson(`${site.apiBase}/api/v1/posts/${encodeURIComponent(slug)}`, timeoutMs)
  if (!post) {
    ctx.waitUntil(cache.delete(metaKey(slug)))
    return GONE
  }

  // Only what the preview needs is kept; the body is reduced to its description first.
  const meta = { ...describePost(post, site), fetchedAt: Date.now() }
  ctx.waitUntil(cache.put(metaKey(slug), new Response(JSON.stringify(meta), {
    headers: { 'Content-Type': 'application/json', 'Cache-Control': `public, max-age=${POST_CACHE_SECONDS}` },
  })))
  return meta
}

/**
 * What a preview of this post should show. A remembered answer is used straight away, so a burst
 * of previews is one API call and a sleeping API does not blank them; once it is a few minutes old
 * it is also checked again in the background, which is how an edit or a take-down gets noticed.
 */
async function cachedPost(ctx, site, slug, timeoutMs) {
  const hit = await caches.default.match(metaKey(slug))
  if (hit) {
    const meta = await hit.json()
    if (isStale(meta.fetchedAt)) ctx.waitUntil(refreshPost(ctx, site, slug, LIST_TIMEOUT_MS).catch(() => {}))
    return meta
  }

  return refreshPost(ctx, site, slug, timeoutMs)
}

/** A picture from a post. Fetched from the API the first time, then served from Cloudflare's own copy. */
async function media(request, ctx, site, id) {
  const cache = caches.default
  const key = new Request(`${new URL(request.url).origin}/media/${id}`, { method: 'GET' })

  const hit = await cache.match(key)
  if (hit) return hit

  const upstream = await fetch(`${site.apiBase}/api/v1/images/${id}`, { signal: AbortSignal.timeout(MEDIA_TIMEOUT_MS) })
  if (upstream.status === 404) {
    return new Response('Not found\n', { status: 404, headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store' } })
  }
  if (!upstream.ok) throw new Error(`API answered ${upstream.status} for a picture`)

  // Whatever the API says, this address only ever hands out a picture.
  const type = (upstream.headers.get('Content-Type') || '').split(';')[0].trim().toLowerCase()
  if (!IMAGE_TYPES.includes(type)) throw new Error('the API returned something that is not a picture')

  const response = new Response(upstream.body, {
    headers: {
      'Content-Type': type,
      // A picture never changes once stored (a new upload gets a new id), so it can be kept for good.
      'Cache-Control': 'public, max-age=31536000, immutable',
      'X-Content-Type-Options': 'nosniff',
      'Content-Security-Policy': "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'none'",
    },
  })
  ctx.waitUntil(cache.put(key, response.clone()))
  return response
}

async function postPage(request, env, ctx, site, slug) {
  // Started together: the page is needed either way, and the preview must not delay it.
  const pagePromise = env.ASSETS.fetch(request)
  const meta = await cachedPost(ctx, site, slug, timeoutFor(request.headers.get('User-Agent'))).catch((error) => {
    console.log('post lookup failed', slug, String(error))
    return null
  })
  const page = await pagePromise

  const isHtml = (page.headers.get('Content-Type') || '').includes('text/html')

  if (meta === GONE && isHtml) {
    // The same page (it says the article is not available), but answered as "not found" so search
    // engines drop it and link previews do not show a card for something that is not there.
    const headers = new Headers(page.headers)
    headers.set('X-Robots-Tag', 'noindex')
    headers.set('Cache-Control', 'no-store')
    return new Response(page.body, { status: 404, headers })
  }

  if (!meta || meta === GONE || !isHtml || !page.ok) return page

  const tags = headTags(meta, site.siteName)
  return new HTMLRewriter()
    .on('title', { element(element) { element.setInnerContent(meta.title) } })
    // The page's general description and address are replaced by the post's own.
    .on('meta[name="description"], meta[property^="og:"], meta[name^="twitter:"], link[rel="canonical"]', { element(element) { element.remove() } })
    .on('head', { element(element) { element.append(tags, { html: true }) } })
    .transform(page)
}
