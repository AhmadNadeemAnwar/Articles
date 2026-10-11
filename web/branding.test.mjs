import { existsSync, readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

/** The site is called Articles and lives at articles.ahmadnadeem.dev; none of what a reader sees may still say Articles. */
// The old name, in two halves so that no search-and-replace of the name ever turns these guards into their opposite.
const OLD = ['ink', 'well'].join('')
const OLD_RE = new RegExp(OLD, 'i')
const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8')

describe('the public site is branded Articles', () => {
  const html = read('./index.html')

  it('uses the new name and address in the page head', () => {
    expect(html).toContain('<title>Articles | لکھت از احمد</title>')
    expect(html).toContain('<meta property="og:site_name" content="Articles | لکھت از احمد" />')
    expect(html).toContain('https://articles.ahmadnadeem.dev/articles-share.png')
    expect(html).not.toMatch(OLD_RE)
  })

  it('names the site for link previews, sitemaps and feeds', () => {
    const config = read('./wrangler.jsonc')
    expect(config).toContain('"SITE_NAME": "Articles | لکھت از احمد"')
    expect(config).toContain('"pattern": "articles.ahmadnadeem.dev"')
  })

  it('points search engines at the new sitemap', () => {
    expect(read('./public/robots.txt')).toContain('https://articles.ahmadnadeem.dev/sitemap.xml')
  })

  it('ships the icons and share card it advertises, and none of the old logo files', () => {
    for (const file of ['favicon.svg', 'favicon.ico', 'apple-touch-icon.png', 'articles-share.png']) expect(existsSync(new URL(`./public/${file}`, import.meta.url)), file).toBe(true)
    for (const file of [`${OLD}-share.png`, 'logo-ink.png', 'logo-leaf.png']) expect(existsSync(new URL(`./public/${file}`, import.meta.url)), file).toBe(false)
  })

  it('says Articles in the header and footer', () => {
    const layout = read('./src/components/Layout.tsx')
    expect(layout).toContain('className="brand">Articles<span')
    expect(layout).toContain('lang="ur" dir="rtl">لکھت از احمد</span>')
    expect(layout).toContain('Articles by Ahmad Nadeem')
    expect(layout).not.toMatch(OLD_RE)
  })
})

describe('the link back to the author', () => {
  it('is switched on, now that the portfolio is live at ahmadnadeem.dev', () => {
    expect(read('./.env.production')).toMatch(/^VITE_PORTFOLIO_URL=https:\/\/ahmadnadeem\.dev$/m)
  })
})

describe('the old address is gone', () => {
  it('is named nowhere in the public site settings, and no redirect Worker is left', () => {
    expect(read('./wrangler.jsonc')).not.toContain(`${OLD}.ahmadnadeem.dev`)
    expect(read('./index.html')).not.toMatch(OLD_RE)
    expect(existsSync(new URL('./legacy-redirect', import.meta.url))).toBe(false)
  })
})

describe('the API address', () => {
  const find = (text, pattern) => text.match(pattern)?.[1]
  const adminRead = (path) => readFileSync(new URL(`../admin/${path}`, import.meta.url), 'utf8')

  it('is one address everywhere, so the sites and their security headers agree', () => {
    const addresses = {
      'web worker API_BASE': find(read('./wrangler.jsonc'), /"API_BASE":\s*"([^"]+)"/),
      'web VITE_API_BASE': find(read('./.env.production'), /^VITE_API_BASE=(\S+)$/m),
      'admin VITE_API_BASE': find(adminRead('.env.production'), /^VITE_API_BASE=(\S+)$/m),
      'web headers connect-src': find(read('./public/_headers'), /connect-src 'self' (https:\/\/\S+)/),
      'admin headers connect-src': find(adminRead('public/_headers'), /connect-src 'self' (https:\/\/\S+)/),
      'admin headers img-src': find(adminRead('public/_headers'), /img-src [^;]*?(https:\/\/[a-z0-9.-]+onrender\.com)/),
    }

    expect(new Set(Object.values(addresses)).size, JSON.stringify(addresses)).toBe(1)
    expect(Object.values(addresses)[0]).toMatch(/^https:\/\/articles-api-[a-z0-9]+\.onrender\.com$/)
  })

  it('does not carry the old service name anywhere in the settings', () => {
    for (const text of [read('./wrangler.jsonc'), read('./.env.production'), read('./public/_headers'), adminRead('.env.production'), adminRead('public/_headers')]) {
      expect(text).not.toMatch(OLD_RE)
    }
  })

  it('allows only the Articles site and the admin portal to call the API from a browser', () => {
    const config = readFileSync(new URL('../src/Articles.Api/appsettings.Production.json', import.meta.url), 'utf8')

    expect(JSON.parse(config).Cors.AllowedOrigins).toEqual(['https://articles.ahmadnadeem.dev', 'https://admin.ahmadnadeem.dev'])
  })
})
