import { describe, expect, it } from 'vitest'
import {
  DEFAULT_SHARE_IMAGE, FETCHER_TIMEOUT_MS, LATEST_COUNT, LATEST_READERS, buildLatest, corsHeadersFor, PREVIEW_FRESH_MS, READER_TIMEOUT_MS, absoluteImage, firstImageId, isStale, mediaIdFromPath, oldPostRedirect, buildRss, buildSitemap, describePost, escapeText, headTags, plainText,
  slugFromPath, summarise, timeoutFor,
} from './meta.js'

const SITE = { siteName: 'Articles', siteOrigin: 'https://articles.example', apiBase: 'https://api.example', description: 'Articles on Articles.' }
const IMAGE = '/api/v1/images/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11'

const paragraph = (words) => ({ type: 'paragraph', content: [{ type: 'text', text: words }] })
const post = (overrides = {}) => ({
  slug: 'a-post',
  title: 'A post',
  subtitle: null,
  excerpt: 'An excerpt.',
  coverImageUrl: null,
  publishedAt: '2026-10-01T09:30:00+00:00',
  author: { displayName: 'Ahmad Nadeem' },
  tags: [{ name: 'Engineering' }],
  contentJson: JSON.stringify({ type: 'sections', content: [{ type: 'textSection', content: [paragraph('First paragraph.'), paragraph('Second paragraph.')] }] }),
  ...overrides,
})

describe('timeoutFor', () => {
  it.each([
    'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36',
    'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1',
    '',
    null,
  ])('does not keep a reader waiting: %s', (userAgent) => {
    expect(timeoutFor(userAgent)).toBe(READER_TIMEOUT_MS)
  })

  it.each([
    'LinkedInBot/1.0 (compatible; Mozilla/5.0; Apache-HttpClient +http://www.linkedin.com)',
    'WhatsApp/2.23.20.0',
    'facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)',
    'Slackbot-LinkExpanding 1.0 (+https://api.slack.com/robots)',
    'Twitterbot/1.0',
    'TelegramBot (like TwitterBot)',
    'Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)',
  ])('gives a preview fetcher time for the API to wake: %s', (userAgent) => {
    expect(timeoutFor(userAgent)).toBe(FETCHER_TIMEOUT_MS)
  })
})

describe('escapeText', () => {
  it('neutralises everything that could end a tag or an attribute', () => {
    expect(escapeText(`<script>alert("x")</script> & 'y'`)).toBe('&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &#39;y&#39;')
  })

  it('drops characters XML forbids, and copes with nothing', () => {
    expect(escapeText('a\u0000b\u0008c')).toBe('abc')
    expect(escapeText(null)).toBe('')
  })
})

describe('slugFromPath', () => {
  it('reads the post address from a post page', () => {
    expect(slugFromPath('/read/today-for-tomorrow-k3x9p')).toBe('today-for-tomorrow-k3x9p')
    expect(slugFromPath('/read/what-i-learned/')).toBe('what-i-learned')
  })

  it('still reads it from the older /p/ address', () => {
    expect(slugFromPath('/p/what-i-learned')).toBe('what-i-learned')
  })

  it.each(['/reading/a-post', '/read', '/read/', '/read/a/b', '/x/a-post', '/read/..%2Fadmin'])('refuses %s', (path) => {
    expect(slugFromPath(path)).toBeNull()
  })

  it.each(['/', '/p/', '/p/a/b', '/search', '/p/Has%20Space', '/p/..%2Fadmin', '/p/%E0%A4%A', '/p/UPPER', '/p/a?b', `/p/${'x'.repeat(121)}`])(
    'refuses %s, so it is never passed to the API',
    (path) => {
      expect(slugFromPath(path)).toBeNull()
    },
  )
})

describe('plainText', () => {
  it('joins the words of every block, keeping blocks apart', () => {
    expect(plainText(post().contentJson)).toBe('First paragraph. Second paragraph.')
  })

  it('reads a post written before sections existed', () => {
    expect(plainText(JSON.stringify({ type: 'doc', content: [paragraph('Older post.')] }))).toBe('Older post.')
  })

  it('leaves picture captions out, so a description starts with the article and not a label', () => {
    const body = JSON.stringify({ type: 'sections', content: [
      { type: 'imageSection', attrs: { imageId: 'x', caption: 'AI generated photo' } },
      { type: 'textSection', content: [paragraph('We keep asking.')] },
    ] })

    expect(plainText(body)).toBe('We keep asking.')
  })

  it('returns nothing for a body it cannot read', () => {
    expect(plainText('not json')).toBe('')
    expect(plainText('null')).toBe('')
  })
})

describe('summarise', () => {
  it('leaves short text alone and tidies its spacing', () => {
    expect(summarise('  Short   text. ')).toBe('Short text.')
  })

  it('cuts long text at a word, with an ellipsis, inside the limit', () => {
    const long = Array.from({ length: 80 }, (_, i) => `word${i}`).join(' ')

    const short = summarise(long, 50)

    expect(short.length).toBeLessThanOrEqual(51)
    expect(short.endsWith('…')).toBe(true)
    expect(long.startsWith(short.slice(0, -1))).toBe(true)
    expect(short).not.toMatch(/\s…$/)
  })
})

describe('absoluteImage', () => {
  it("points an uploaded picture at the site's own cached address, not at the API", () => {
    expect(absoluteImage(IMAGE, SITE.siteOrigin)).toBe('https://articles.example/media/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11')
  })

  it('keeps an https link as it is', () => {
    expect(absoluteImage('https://images.example/cover.jpg', SITE.siteOrigin)).toBe('https://images.example/cover.jpg')
  })

  it.each(['http://insecure.example/x.png', 'javascript:alert(1)', '/api/v1/admin/stats', '//evil.example/x.png', '', null, 42])('drops %s', (value) => {
    expect(absoluteImage(value, SITE.siteOrigin)).toBeNull()
  })
})

describe('mediaIdFromPath', () => {
  it('reads the picture id from a picture address', () => {
    expect(mediaIdFromPath('/media/0B6F8F0E-2f0b-4a53-9a3e-0e6a1f4f7c11')).toBe('0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11')
  })

  it.each(['/media/', '/media/not-an-id', '/media/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11/extra', '/media/../api/v1/admin/stats', '/api/v1/images/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11', '/media/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11.png'])(
    'refuses %s, so the address can only ever fetch a picture',
    (path) => {
      expect(mediaIdFromPath(path)).toBeNull()
    },
  )
})

describe('oldPostRedirect', () => {
  it('sends an old post address to the new one', () => {
    expect(oldPostRedirect('/p/today-for-tomorrow')).toBe('/read/today-for-tomorrow')
    expect(oldPostRedirect('/p/today-for-tomorrow/')).toBe('/read/today-for-tomorrow')
  })

  it.each(['/read/today-for-tomorrow', '/p/', '/p/a/b', '/p/..%2Fadmin', '/p/%2F%2Fevil.example', '/privacy'])('does not redirect %s', (path) => {
    expect(oldPostRedirect(path)).toBeNull()
  })
})

describe('firstImageId', () => {
  const body = (...content) => JSON.stringify({ type: 'sections', content })
  const image = (imageId) => ({ type: 'imageSection', attrs: { imageId } })

  it('finds the first picture in the article', () => {
    expect(firstImageId(body({ type: 'textSection', content: [] }, image('0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11'), image('11111111-2f0b-4a53-9a3e-0e6a1f4f7c11')))).toBe('0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11')
  })

  it('skips a picture section whose id is not a real id', () => {
    expect(firstImageId(body(image('https://evil.example/x.png'), image('11111111-2f0b-4a53-9a3e-0e6a1f4f7c11')))).toBe('11111111-2f0b-4a53-9a3e-0e6a1f4f7c11')
  })

  it('finds nothing in a post with no pictures, an older post, or a broken body', () => {
    expect(firstImageId(body({ type: 'textSection', content: [] }))).toBeNull()
    expect(firstImageId(JSON.stringify({ type: 'doc', content: [] }))).toBeNull()
    expect(firstImageId('not json')).toBeNull()
    expect(firstImageId('null')).toBeNull()
  })
})

describe('isStale', () => {
  const now = 1_800_000_000_000

  it('treats a recently remembered preview as fresh', () => {
    expect(isStale(now - 1000, now)).toBe(false)
    expect(isStale(now - PREVIEW_FRESH_MS + 1, now)).toBe(false)
  })

  it('treats an older one as due for a check', () => {
    expect(isStale(now - PREVIEW_FRESH_MS, now)).toBe(true)
    expect(isStale(now - 6 * 60 * 60 * 1000, now)).toBe(true)
  })

  it.each([undefined, null, 'yesterday', NaN])('treats one with no readable time (%s) as due for a check', (value) => {
    expect(isStale(value, now)).toBe(true)
  })
})

describe('describePost', () => {
  it('uses the first picture in the article when there is no cover', () => {
    const contentJson = JSON.stringify({ type: 'sections', content: [
      { type: 'imageSection', attrs: { imageId: '0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11', caption: 'AI generated photo' } },
      { type: 'textSection', content: [paragraph('We keep asking.')] },
    ] })

    const meta = describePost(post({ contentJson }), SITE)

    expect(meta.image).toBe('https://articles.example/media/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11')
    expect(meta.description).toBe('We keep asking.')
  })

  it('prefers the cover over a picture in the article', () => {
    const contentJson = JSON.stringify({ type: 'sections', content: [{ type: 'imageSection', attrs: { imageId: '11111111-2f0b-4a53-9a3e-0e6a1f4f7c11' } }] })

    expect(describePost(post({ contentJson, coverImageUrl: 'https://images.example/cover.jpg' }), SITE).image).toBe('https://images.example/cover.jpg')
  })

  it('uses the subtitle as the description when there is one', () => {
    expect(describePost(post({ subtitle: 'The subtitle.' }), SITE).description).toBe('The subtitle.')
  })

  it('falls back to the opening words of the post, then to a general line', () => {
    expect(describePost(post(), SITE).description).toBe('First paragraph. Second paragraph.')
    expect(describePost(post({ contentJson: '{}' }), SITE).description).toBe('An article on Articles.')
  })

  it('gives the /read/ address as the one true address of a post', () => {
    expect(describePost(post(), SITE).url).toBe('https://articles.example/read/a-post')
    expect(headTags(describePost(post(), SITE), 'Articles')).toContain('<link rel="canonical" href="https://articles.example/read/a-post">')
  })

  it('builds the address, title and picture', () => {
    const meta = describePost(post({ coverImageUrl: IMAGE }), SITE)

    expect(meta).toMatchObject({
      title: 'A post · Articles',
      heading: 'A post',
      url: 'https://articles.example/read/a-post',
      image: 'https://articles.example/media/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11',
      author: 'Ahmad Nadeem',
    })
  })
})

describe('headTags', () => {
  it('includes what LinkedIn, WhatsApp and X look for', () => {
    const tags = headTags(describePost(post({ coverImageUrl: IMAGE }), SITE), 'Articles')

    expect(tags).toContain('<link rel="canonical" href="https://articles.example/read/a-post">')
    expect(tags).toContain('<meta property="og:title" content="A post">')
    expect(tags).toContain('<meta property="og:type" content="article">')
    expect(tags).toContain('<meta property="og:image" content="https://articles.example/media/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11">')
    expect(tags).toContain('<meta name="twitter:card" content="summary_large_image">')
    expect(tags).toContain('<meta name="description" content="First paragraph. Second paragraph.">')
  })

  it('shares a post with no picture of its own using the Articles logo card', () => {
    const meta = describePost(post(), SITE)
    const tags = headTags(meta, 'Articles')

    expect(meta.image).toBe('https://articles.example/articles-share.png')
    expect(DEFAULT_SHARE_IMAGE).toBe('/articles-share.png')
    expect(tags).toContain('<meta name="twitter:card" content="summary_large_image">')
    expect(tags).toContain('<meta property="og:image" content="https://articles.example/articles-share.png">')
  })

  it('prefers the posts own picture over the logo card', () => {
    expect(describePost(post({ coverImageUrl: 'https://images.example/cover.jpg' }), SITE).image).toBe('https://images.example/cover.jpg')
  })

  it('uses the small card and leaves picture tags out when the meta has no picture at all', () => {
    const tags = headTags({ ...describePost(post(), SITE), image: null }, 'Articles')

    expect(tags).toContain('<meta name="twitter:card" content="summary">')
    expect(tags).not.toContain('og:image')
    expect(tags).not.toContain('twitter:image')
  })

  it('cannot be broken out of by a hostile title or subtitle', () => {
    const tags = headTags(describePost(post({ title: '"><script>alert(1)</script>', subtitle: `'"><img src=x onerror=alert(2)>` }), SITE), 'Articles')

    expect(tags).not.toContain('<script>')
    expect(tags).not.toContain('<img')
    expect(tags).toContain('&quot;&gt;&lt;script&gt;')
  })
})

describe('buildSitemap', () => {
  it('lists the home page, every post with its date, and the privacy page', () => {
    const xml = buildSitemap([post(), post({ slug: 'another-post', publishedAt: '2026-09-01T00:00:00Z' })], SITE.siteOrigin)

    expect(xml.startsWith('<?xml version="1.0" encoding="UTF-8"?>')).toBe(true)
    expect(xml).toContain('<loc>https://articles.example/</loc>')
    expect(xml).toContain('<url><loc>https://articles.example/read/a-post</loc><lastmod>2026-10-01T09:30:00.000Z</lastmod></url>')
    expect(xml).toContain('<loc>https://articles.example/read/another-post</loc>')
    expect(xml).toContain('<loc>https://articles.example/privacy</loc>')
  })

  it('leaves out anything without a usable address, and a date it cannot read', () => {
    const xml = buildSitemap([post({ slug: null }), post({ slug: '../admin' }), null, post({ publishedAt: 'not a date' })], SITE.siteOrigin)

    expect(xml.match(/<url>/g)).toHaveLength(3)
    expect(xml).not.toContain('admin')
    expect(xml).not.toContain('lastmod')
  })

  it('is still a valid sitemap with no posts', () => {
    expect(buildSitemap([], SITE.siteOrigin).match(/<url>/g)).toHaveLength(2)
  })
})

describe('buildRss', () => {
  it('describes the site and each post', () => {
    const xml = buildRss([post({ subtitle: 'The subtitle.' })], SITE)

    expect(xml).toContain('<title>Articles</title>')
    expect(xml).toContain('<atom:link href="https://articles.example/rss.xml" rel="self" type="application/rss+xml"/>')
    expect(xml).toContain('<item><title>A post</title><link>https://articles.example/read/a-post</link>')
    expect(xml).toContain('<guid isPermaLink="true">https://articles.example/read/a-post</guid>')
    expect(xml).toContain('<pubDate>Thu, 01 Oct 2026 09:30:00 GMT</pubDate>')
    expect(xml).toContain('<description>The subtitle.</description>')
    expect(xml).toContain('<category>Engineering</category>')
  })

  it('falls back to the excerpt when there is no subtitle', () => {
    expect(buildRss([post()], SITE)).toContain('<description>An excerpt.</description>')
  })

  it('escapes titles so one post cannot corrupt the feed', () => {
    const xml = buildRss([post({ title: 'Tom & Jerry <3 "quotes"', tags: [{ name: 'R&D' }] })], SITE)

    expect(xml).toContain('<title>Tom &amp; Jerry &lt;3 &quot;quotes&quot;</title>')
    expect(xml).toContain('<category>R&amp;D</category>')
  })

  it('is a valid empty feed when nothing is published', () => {
    const xml = buildRss([], SITE)

    expect(xml).not.toContain('<item>')
    expect(xml.trimEnd().endsWith('</channel></rss>')).toBe(true)
  })
})

describe('buildLatest', () => {
  const many = ['one', 'two', 'three', 'four', 'five'].map((slug) => post({ slug, title: `Post ${slug}` }))

  it('lists only the newest few, with an absolute link, a date and a summary', () => {
    const data = JSON.parse(buildLatest([...many, post({ slug: 'six', subtitle: 'A subtitle.' })], SITE))

    expect(LATEST_COUNT).toBe(3)
    expect(data.items.map((i) => i.title)).toEqual(['Post one', 'Post two', 'Post three'])
    expect(data.items[0]).toEqual({ title: 'Post one', url: 'https://articles.example/read/one', date: expect.stringMatching(/^2026-10-01/), summary: expect.any(String) })
    expect(data.url).toBe('https://articles.example/')
  })

  it('uses the subtitle as the summary when there is one', () => {
    expect(JSON.parse(buildLatest([post({ subtitle: 'The subtitle.' })], SITE)).items[0].summary).toBe('The subtitle.')
  })

  it('skips anything without a usable address', () => {
    const data = JSON.parse(buildLatest([post({ slug: 'Bad Slug!' }), post({ slug: 'good-one' }), null], SITE))

    expect(data.items.map((i) => i.url)).toEqual(['https://articles.example/read/good-one'])
  })

  it('is an empty list, not an error, when there are no posts', () => {
    expect(JSON.parse(buildLatest([], SITE)).items).toEqual([])
  })

  it('keeps markup in a title as plain text for the reader to escape', () => {
    expect(JSON.parse(buildLatest([post({ title: '<b>Hi</b> & "bye"' })], SITE)).items[0].title).toBe('<b>Hi</b> & "bye"')
  })
})

describe('corsHeadersFor', () => {
  it('lets the portfolio, with or without www, read the list', () => {
    for (const origin of LATEST_READERS) expect(corsHeadersFor(origin)).toEqual({ 'Access-Control-Allow-Origin': origin, Vary: 'Origin' })
  })

  it.each([null, undefined, '', 'https://evil.example', 'http://ahmadnadeem.dev', 'https://ahmadnadeem.dev.evil.example', 'https://sub.ahmadnadeem.dev'])('gives no permission to %s', (origin) => {
    expect(corsHeadersFor(origin)).toEqual({ Vary: 'Origin' })
  })
})
