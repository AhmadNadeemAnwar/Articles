import { existsSync, readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { collections } from './src/portfolio/schema.ts'

/** The portal is called Articles Admin; none of what its one user reads may still say Articles. */
// The old name, in two halves so that no search-and-replace of the name ever turns these guards into their opposite.
const OLD = ['ink', 'well'].join('')
const OLD_RE = new RegExp(OLD, 'i')
const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8')

describe('the admin portal is branded Articles', () => {
  it('uses the new name for the page and the sign-in', () => {
    expect(read('./index.html')).toContain('<title>Articles Admin</title>')
    expect(read('./src/pages/LoginPage.tsx')).toContain('<h1>Articles Admin</h1>')
    expect(read('./src/components/Layout.tsx')).toContain('Articles <span>Admin</span>')
  })

  it('links to the public site at its new address', () => {
    expect(read('./.env.production')).toContain('VITE_PUBLIC_SITE=https://articles.ahmadnadeem.dev')
  })

  it('has no message that still names Articles', () => {
    for (const file of ['./src/pages/PostEditPage.tsx', './src/pages/PostsPage.tsx', './src/pages/SitePage.tsx', './src/pages/LoginPage.tsx']) {
      expect(read(file), file).not.toMatch(OLD_RE)
    }
  })

  it('names the exported posts file after the new site', () => {
    const posts = read('./src/pages/PostsPage.tsx')
    expect(posts).toContain('articles-posts-')
    expect(posts).not.toContain(`${OLD}-posts-`)
  })

  it('carries no old logo files', () => {
    for (const file of ['logo-ink.png', 'logo-leaf.png']) expect(existsSync(new URL(`./public/${file}`, import.meta.url)), file).toBe(false)
    expect(existsSync(new URL('./public/favicon.ico', import.meta.url))).toBe(true)
  })
})

describe('the portfolio editor in the admin portal', () => {
  it('offers projects and updates only, because the portfolio no longer has a blog', () => {
    expect(collections).toEqual(['projects', 'updates'])
  })
})
