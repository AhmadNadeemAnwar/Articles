import { describe, expect, it } from 'vitest'
import { countryLabel, deviceLabel, percent, readersSummary, sourceLabel } from './ViewsPage'

describe('labels', () => {
  it('names countries, and says when the country was not known', () => {
    expect(countryLabel('MY')).toBe('Malaysia')
    expect(countryLabel('PK')).toBe('Pakistan')
    expect(countryLabel('')).toBe('Not known')
  })

  it('names devices and sources plainly', () => {
    expect(['phone', 'tablet', 'computer', ''].map(deviceLabel)).toEqual(['Phone', 'Tablet', 'Computer', 'Not known'])
    expect(sourceLabel('google.com')).toBe('google.com')
    expect(sourceLabel('')).toBe('Direct, or from within your sites')
  })
})

describe('percent', () => {
  it('is a whole-number share, and 0 when there is nothing', () => {
    expect(percent(1, 3)).toBe(33)
    expect(percent(2, 3)).toBe(67)
    expect(percent(5, 0)).toBe(0)
  })
})

describe('readersSummary', () => {
  it('says how many readers, how many came back, and the busiest day', () => {
    expect(readersSummary([{ count: 2 }, { count: 0 }, { count: 5 }], 3)).toBe('7 readers in the last 3 days, 3 of them returning; the busiest day had 5.')
  })

  it('says so when there were none', () => {
    expect(readersSummary([{ count: 0 }, { count: 0 }], 0)).toBe('No readers in the last 2 days yet.')
  })
})
