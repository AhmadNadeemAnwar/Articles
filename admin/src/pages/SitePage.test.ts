import { describe, expect, it } from 'vitest'
import { publicPostUrl } from '../components/ui'
import { aiProposalNote, categoryNameProblem, customColoursState, describeTheme } from './SitePage'

describe('categoryNameProblem', () => {
  const taken = ['Life and lessons', 'Technology and AI']

  it('accepts a new name', () => {
    expect(categoryNameProblem('Books', taken)).toBeNull()
  })

  it('requires a name that is more than spaces', () => {
    expect(categoryNameProblem('   ', taken)).toMatch(/name/)
  })

  it('refuses a name already in use, whatever its capitals or spacing', () => {
    expect(categoryNameProblem('  life AND lessons ', taken)).toMatch(/already/)
  })

  it('lets a category keep its own name when the others are passed in', () => {
    expect(categoryNameProblem('Life and Lessons', ['Technology and AI'])).toBeNull()
  })

  it('refuses a name with nothing an address could be made from', () => {
    expect(categoryNameProblem('!!! ???', taken)).toMatch(/letter or number/)
  })

  it('accepts names in other scripts', () => {
    expect(categoryNameProblem('زندگی', taken)).toBeNull()
  })

  it('limits the length', () => {
    expect(categoryNameProblem('x'.repeat(41), taken)).toMatch(/40/)
    expect(categoryNameProblem('x'.repeat(40), taken)).toBeNull()
  })
})

describe('describeTheme', () => {
  it('names the themes the site has', () => {
    expect(describeTheme('blue').name).toBe('Deep blue')
    expect(describeTheme('seagreen').name).toBe('Sea green')
  })

  it('still shows a theme this page has not been told about', () => {
    expect(describeTheme('sunset')).toEqual({ name: 'sunset', note: '', swatches: [] })
  })
})

describe('customColoursState', () => {
  const plum = { main: '#7a1f5c', accent: '#7a1f5c', background: '#fff8f0' }

  it('lets new colours be saved', () => {
    expect(customColoursState(plum, { theme: 'blue', colors: { main: '#17694a', accent: '#17694a', background: '#ffffff' } })).toEqual({ problem: null, inUse: false, canSave: true })
  })

  it('lets saved colours be switched on when a ready-made theme is in use', () => {
    expect(customColoursState(plum, { theme: 'seagreen', colors: plum })).toMatchObject({ inUse: false, canSave: true })
  })

  it('has nothing to save when these colours are already what the site wears', () => {
    expect(customColoursState(plum, { theme: 'custom', colors: plum })).toMatchObject({ inUse: true, canSave: false })
  })

  it('allows a change to either colour of the custom theme in use', () => {
    expect(customColoursState({ ...plum, main: '#224488' }, { theme: 'custom', colors: plum }).canSave).toBe(true)
    expect(customColoursState({ ...plum, accent: '#224488' }, { theme: 'custom', colors: plum }).canSave).toBe(true)
    expect(customColoursState({ ...plum, background: '#ffffff' }, { theme: 'custom', colors: plum }).canSave).toBe(true)
  })

  it('will not save a background text cannot be read on', () => {
    const state = customColoursState({ main: '#7a1f5c', accent: '#7a1f5c', background: '#808080' }, { theme: 'blue', colors: plum })

    expect(state.problem).toMatch(/lighter or a darker/)
    expect(state.canSave).toBe(false)
  })
})

describe('publicPostUrl', () => {
  it('points at the reading address', () => {
    expect(publicPostUrl('today-for-tomorrow-k3x9p')).toMatch(/\/read\/today-for-tomorrow-k3x9p$/)
  })

  it('has no address for a post that was never published', () => {
    expect(publicPostUrl(null)).toBeNull()
  })
})

describe('aiProposalNote', () => {
  it('names the theme, says the colours are not kept yet, and how many goes are left', () => {
    expect(aiProposalNote('Dusk Orchard', 3, 20)).toBe('The AI calls this “Dusk Orchard”. Press “Use these colours” to keep them, or try again. 17 goes left today.')
  })

  it('says "go" for the last one and never counts below none', () => {
    expect(aiProposalNote('Sea Glass', 19, 20)).toContain('1 go left today.')
    expect(aiProposalNote('Sea Glass', 25, 20)).toContain('0 goes left today.')
  })

  it('copes with a theme that has no name', () => {
    expect(aiProposalNote('  ', 1, 20)).toMatch(/^The AI has picked colours\. Press/)
  })
})
