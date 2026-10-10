import { existsSync, readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

/** The site is called Articles and lives at articles.ahmadnadeem.dev; none of what a reader sees may still say Inkwell. */
const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8')

describe('the public site is branded Articles', () => {
  const html = read('./index.html')

  it('uses the new name and address in the page head', () => {
    expect(html).toContain('<title>Articles by Ahmad Nadeem</title>')
    expect(html).toContain('<meta property="og:site_name" content="Articles" />')
    expect(html).toContain('https://articles.ahmadnadeem.dev/articles-share.png')
    expect(html).not.toMatch(/inkwell/i)
  })

  it('names the site for link previews, sitemaps and feeds', () => {
    const config = read('./wrangler.jsonc')
    expect(config).toContain('"SITE_NAME": "Articles"')
    expect(config).toContain('"pattern": "articles.ahmadnadeem.dev"')
  })

  it('points search engines at the new sitemap', () => {
    expect(read('./public/robots.txt')).toContain('https://articles.ahmadnadeem.dev/sitemap.xml')
  })

  it('ships the icons and share card it advertises, and none of the old logo files', () => {
    for (const file of ['favicon.svg', 'favicon.ico', 'apple-touch-icon.png', 'articles-share.png']) expect(existsSync(new URL(`./public/${file}`, import.meta.url)), file).toBe(true)
    for (const file of ['inkwell-share.png', 'logo-ink.png', 'logo-leaf.png']) expect(existsSync(new URL(`./public/${file}`, import.meta.url)), file).toBe(false)
  })

  it('says Articles in the header and footer', () => {
    const layout = read('./src/components/Layout.tsx')
    expect(layout).toContain('className="brand">Articles</Link>')
    expect(layout).toContain('Articles by Ahmad Nadeem')
    expect(layout).not.toMatch(/inkwell/i)
  })
})

describe('the link back to the author', () => {
  it('is switched on, now that the portfolio is live at ahmadnadeem.dev', () => {
    expect(read('./.env.production')).toMatch(/^VITE_PORTFOLIO_URL=https:\/\/ahmadnadeem\.dev$/m)
  })
})

describe('the old address is gone', () => {
  it('is named nowhere in the public site settings, and no redirect Worker is left', () => {
    expect(read('./wrangler.jsonc')).not.toContain('inkwell.ahmadnadeem.dev')
    expect(read('./index.html')).not.toMatch(/inkwell/i)
    expect(existsSync(new URL('./legacy-redirect', import.meta.url))).toBe(false)
  })
})
