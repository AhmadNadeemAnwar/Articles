import { describe, expect, it } from 'vitest'
import { noteParts } from './note'

describe('noteParts', () => {
  it('leaves plain text as it is', () => {
    expect(noteParts('Thank you for reading.')).toEqual([{ text: 'Thank you for reading.' }])
  })

  it('turns a web address into a link and keeps the words around it', () => {
    expect(noteParts('Write to me at https://example.com/contact today')).toEqual([
      { text: 'Write to me at ' },
      { text: 'https://example.com/contact', href: 'https://example.com/contact' },
      { text: ' today' },
    ])
  })

  it('does not take the full stop or bracket after an address into the link', () => {
    expect(noteParts('See (https://example.com/a).')).toEqual([
      { text: 'See (' },
      { text: 'https://example.com/a', href: 'https://example.com/a' },
      { text: ').' },
    ])
  })

  it('finds several addresses, http as well as https', () => {
    const parts = noteParts('http://a.example and https://b.example')

    expect(parts.filter((part) => part.href).map((part) => part.href)).toEqual(['http://a.example', 'https://b.example'])
  })

  it('never makes a link from anything else', () => {
    for (const text of ['javascript:alert(1)', 'data:text/html,hi', 'ftp://example.com', '<a href="https://x.example">x</a>']) {
      const links = noteParts(text).filter((part) => part.href)
      for (const link of links) expect(link.href).toMatch(/^https?:\/\//)
    }
    expect(noteParts('javascript:alert(1)').some((part) => part.href)).toBe(false)
  })

  it('keeps markup as text: what goes in comes out, unchanged, when the parts are joined', () => {
    const text = '<script>alert(1)</script> & <b>bold</b> https://example.com/?a=1&b=2.'

    expect(noteParts(text).map((part) => part.text).join('')).toBe(text)
  })

  it('is empty for no text', () => {
    expect(noteParts('')).toEqual([])
  })
})
