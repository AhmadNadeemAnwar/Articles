import { describe, expect, it } from 'vitest'
import { NO_CATEGORY, categoryChoice, categoryParam, isFiltering, movedMessage } from './categoryMove'

describe('categoryParam', () => {
  it('sends nothing when the filter is on "All categories"', () => {
    expect(categoryParam('')).toBeUndefined()
  })

  it('sends "none" for posts in no category, and a category id as it is', () => {
    expect(categoryParam(NO_CATEGORY)).toBe('none')
    expect(categoryParam('7c2d5e0c-9f1e-4c6e-8f33-0a1b2c3d4e5f')).toBe('7c2d5e0c-9f1e-4c6e-8f33-0a1b2c3d4e5f')
  })
})

describe('categoryChoice', () => {
  it('turns the dropdown\'s empty value into "no category"', () => {
    expect(categoryChoice('')).toBeNull()
  })

  it('keeps a chosen category id', () => {
    expect(categoryChoice('abc')).toBe('abc')
  })
})

describe('movedMessage', () => {
  it('says where the post went', () => {
    expect(movedMessage('Today for Tomorrow', 'Life and Lessons')).toBe('“Today for Tomorrow” moved to Life and Lessons.')
  })

  it('says so when the post was taken out of its category', () => {
    expect(movedMessage('Today for Tomorrow', null)).toBe('“Today for Tomorrow” is now in no category.')
  })
})

describe('isFiltering', () => {
  it('is false with no filter at all', () => {
    expect(isFiltering({ status: '', term: '', category: '' })).toBe(false)
  })

  it.each([{ status: 'Draft' }, { term: 'faith' }, { category: 'none' }])('is true when %j is set', (one) => {
    expect(isFiltering({ status: '', term: '', category: '', ...one })).toBe(true)
  })
})
