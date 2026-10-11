import { useState } from 'react'
import type { CSSProperties, FormEvent } from 'react'
import { api } from '../api/client'
import type { Category } from '../api/types'
import { useAsync } from '../hooks/useAsync'
import { useConfirm, useToast } from '../components/feedback'
import { Empty, ErrorNote, PageHeader, Spinner, formatNumber } from '../components/ui'
import { backgroundProblem, paletteFrom, toHex } from '../lib/palette'
import type { ThemeColors } from '../lib/palette'

export const MAX_CATEGORIES = 12
export const MAX_CATEGORY_NAME = 40

/** How each theme is described to the owner. A theme the API offers but this page does not know still gets a plain entry. */
export const themeLabels: Record<string, { name: string; note: string; swatches: string[] }> = {
  blue: { name: 'Deep blue', note: 'Charcoal header, deep blue links and buttons.', swatches: ['#202327', '#185fa5', '#f2f2f1'] },
  seagreen: { name: 'Sea green', note: 'White pages with a sea-green header, links and buttons.', swatches: ['#17694a', '#1e7a56', '#ffffff'] },
}

export function describeTheme(theme: string) {
  return themeLabels[theme] ?? { name: theme, note: '', swatches: [] }
}

/** The owner's own three colours are a theme too, but are set with pickers rather than chosen from the list. */
export const CUSTOM_THEME = 'custom'

/** What the Save button for custom colours should do, given what is picked and what the site wears now. */
export function customColoursState(picked: ThemeColors, current: { theme: string; colors: ThemeColors }) {
  const problem = backgroundProblem(picked.background)
  const inUse = current.theme === CUSTOM_THEME && current.colors.main === picked.main && current.colors.accent === picked.accent && current.colors.background === picked.background
  return { problem, inUse, canSave: problem === null && !inUse }
}

/** What is said after the AI has proposed colours. They are only in the pickers; the owner still has to keep them. */
export function aiProposalNote(name: string, usedToday: number, dailyLimit: number): string {
  const left = Math.max(0, dailyLimit - usedToday)
  const title = name.trim() === '' ? 'The AI has picked colours.' : `The AI calls this “${name.trim()}”.`
  return `${title} Press “Use these colours” to keep them, or try again. ${left} ${left === 1 ? 'go' : 'goes'} left today.`
}

/** Why a category name cannot be used, or null if it can. `others` are the names already taken by other categories. */
export function categoryNameProblem(name: string, others: string[]): string | null {
  const clean = name.trim()
  if (clean === '') return 'Give the category a name.'
  if (clean.length > MAX_CATEGORY_NAME) return `A category name can be at most ${MAX_CATEGORY_NAME} characters.`
  if (!/[\p{L}\p{N}]/u.test(clean)) return 'A category name needs at least one letter or number.'
  if (others.some((other) => other.trim().toLowerCase() === clean.toLowerCase())) return `There is already a category called “${clean}”.`
  return null
}

export function SitePage() {
  return (
    <>
      <PageHeader title="Site" />
      <ThemePanel />
      <CategoriesPanel />
    </>
  )
}

function ThemePanel() {
  const notify = useToast()
  const settings = useAsync(() => api.settings(), [])
  const [saving, setSaving] = useState<string | null>(null)

  async function choose(theme: string, colors?: ThemeColors) {
    setSaving(theme)
    try {
      await api.updateSettings(theme, colors)
      notify(`Theme changed to ${colors ? 'your own colours' : describeTheme(theme).name}. Readers see it the next time they open the site.`)
      settings.reload()
    } catch (err) {
      notify(err instanceof Error ? err.message : 'The theme could not be changed.', 'error')
    } finally {
      setSaving(null)
    }
  }

  return (
    <section className="panel">
      <h2>Theme</h2>
      <p className="muted">How the public site looks to every reader. This portal keeps its own look.</p>

      {settings.error && <ErrorNote message={settings.error} onRetry={settings.reload} />}
      {settings.loading && !settings.data && <Spinner />}

      {settings.data && (
        <div className="themes" role="radiogroup" aria-label="Theme">
          {settings.data.availableThemes.filter((theme) => theme !== CUSTOM_THEME).map((theme) => {
            const info = describeTheme(theme)
            const active = settings.data!.theme === theme
            return (
              <button key={theme} type="button" role="radio" aria-checked={active} disabled={saving !== null}
                className={`theme${active ? ' theme--active' : ''}`} onClick={() => { if (!active) void choose(theme) }}>
                <span className="theme__swatches" aria-hidden="true">
                  {info.swatches.map((colour) => <span key={colour} style={{ background: colour }} />)}
                </span>
                <span className="theme__name">{info.name}{active && <span className="badge badge--good">In use</span>}</span>
                <span className="theme__note">{saving === theme ? 'Switching…' : info.note}</span>
              </button>
            )
          })}
        </div>
      )}

      {settings.data?.availableThemes.includes(CUSTOM_THEME) && (
        <CustomColours key={`${settings.data.theme}-${settings.data.colors.main}-${settings.data.colors.accent}-${settings.data.colors.background}`}
          current={settings.data} saving={saving !== null} onSave={(colors) => void choose(CUSTOM_THEME, colors)} />
      )}
    </section>
  )
}

/** The palette as React wants it: the colour variables as they are, and the one real CSS property under its React name. */
function previewStyle(colors: ThemeColors): CSSProperties {
  const { 'color-scheme': colorScheme, ...variables } = paletteFrom(colors)
  return { ...variables, colorScheme } as CSSProperties
}

function CustomColours({ current, saving, onSave }: {
  current: { theme: string; colors: ThemeColors }
  saving: boolean
  onSave: (colors: ThemeColors) => void
}) {
  const notify = useToast()
  const [picked, setPicked] = useState<ThemeColors>(current.colors)
  const ai = useAsync(() => api.themeAiStatus().catch(() => null), [])
  const [asking, setAsking] = useState(false)
  const [aiNote, setAiNote] = useState<string | null>(null)
  const { problem, inUse, canSave } = customColoursState(picked, current)

  function pick(part: keyof ThemeColors, value: string) {
    const hex = toHex(value)
    if (hex) setPicked({ ...picked, [part]: hex })
  }

  async function amaze() {
    setAsking(true)
    try {
      const result = await api.askAiForColours()
      setPicked(result.colors)
      setAiNote(aiProposalNote(result.name, result.usedToday, result.dailyLimit))
      ai.reload()
    } catch (err) {
      notify(err instanceof Error ? err.message : 'The AI could not choose colours.', 'error')
    } finally {
      setAsking(false)
    }
  }

  return (
    <div className={`custom-theme${inUse ? ' custom-theme--active' : ''}`}>
      <h3>Your own colours {inUse && <span className="badge badge--good">In use</span>}</h3>
      <p className="muted">
        Pick three colours. The text, borders and button shades are worked out from them so the site always stays readable.
        Readers can also choose colours for themselves with the palette button on the site; that only changes their own device.
      </p>

      <div className="custom-theme__body">
        <div className="custom-theme__pickers">
          <label>
            <span>Header<small>The bar across the top</small></span>
            <input type="color" value={picked.main} onChange={(e) => pick('main', e.target.value)} />
          </label>
          <label>
            <span>Links and buttons<small>Whatever readers can click</small></span>
            <input type="color" value={picked.accent} onChange={(e) => pick('accent', e.target.value)} />
          </label>
          <label>
            <span>Background<small>The page behind the text</small></span>
            <input type="color" value={picked.background} onChange={(e) => pick('background', e.target.value)} />
          </label>
          {ai.data?.enabled && (
            <button type="button" className="btn btn--ai" disabled={asking || saving || ai.data.usedToday >= ai.data.dailyLimit} onClick={() => void amaze()}>
              {asking ? 'The AI is choosing…' : 'Amaze with AI'}
            </button>
          )}
          {aiNote && <p className="custom-theme__note" role="status">{aiNote}</p>}
          {problem && <p className="custom-theme__problem" role="alert">{problem}</p>}
          <button type="button" className="btn btn--primary" disabled={!canSave || saving} onClick={() => onSave(picked)}>
            {saving ? 'Saving…' : inUse ? 'These colours are in use' : 'Use these colours'}
          </button>
        </div>

        {!problem && (
          <div className="theme-preview" style={previewStyle(picked)} aria-label="Preview of the public site">
            <div className="theme-preview__header"><strong>Articles</strong><span>Search articles</span></div>
            <div className="theme-preview__page">
              <p className="theme-preview__title">A post title</p>
              <p className="theme-preview__meta">7 October · 4 min read</p>
              <p>Body text looks like this, with <a>a link</a> in it.</p>
              <div className="theme-preview__card">
                <span className="theme-preview__button">Button</span>
                <span className="theme-preview__chip">Selected</span>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

function CategoriesPanel() {
  const confirm = useConfirm()
  const notify = useToast()
  const categories = useAsync(() => api.categories(), [])

  const [name, setName] = useState('')
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<{ id: string; name: string } | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const list = categories.data ?? []
  const full = list.length >= MAX_CATEGORIES

  async function add(event: FormEvent) {
    event.preventDefault()
    const problem = categoryNameProblem(name, list.map((c) => c.name))
    if (problem) return notify(problem, 'error')

    setAdding(true)
    try {
      await api.createCategory(name.trim())
      setName('')
      notify('Category added. Choose it for a post in the editor.')
      categories.reload()
    } catch (err) {
      notify(err instanceof Error ? err.message : 'The category could not be added.', 'error')
    } finally {
      setAdding(false)
    }
  }

  async function rename(event: FormEvent) {
    event.preventDefault()
    if (!editing) return
    const problem = categoryNameProblem(editing.name, list.filter((c) => c.id !== editing.id).map((c) => c.name))
    if (problem) return notify(problem, 'error')

    setBusyId(editing.id)
    try {
      await api.renameCategory(editing.id, editing.name.trim())
      setEditing(null)
      notify('Category renamed.')
      categories.reload()
    } catch (err) {
      notify(err instanceof Error ? err.message : 'The category could not be renamed.', 'error')
    } finally {
      setBusyId(null)
    }
  }

  async function remove(category: Category) {
    const ok = await confirm({
      title: `Delete “${category.name}”?`,
      message: category.postCount > 0
        ? <>Its {formatNumber(category.postCount)} published {category.postCount === 1 ? 'post is' : 'posts are'} kept, and any drafts too. They will simply have no category.</>
        : <>No published posts use it. Any drafts in it will simply have no category.</>,
      confirmLabel: 'Delete category',
      danger: true,
    })
    if (!ok) return

    setBusyId(category.id)
    try {
      await api.deleteCategory(category.id)
      notify('Category deleted.')
      categories.reload()
    } catch (err) {
      notify(err instanceof Error ? err.message : 'The category could not be deleted.', 'error')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <section className="panel">
      <h2>Categories</h2>
      <p className="muted">
        Broad shelves such as “Life and lessons”. Each post goes in one category at most, and the public home page shows a tab for every
        category that has a published post. Topics are separate: a post can have several, and you make those up as you write.
      </p>

      {categories.error && <ErrorNote message={categories.error} onRetry={categories.reload} />}
      {categories.loading && !categories.data && <Spinner />}

      {categories.data && (list.length === 0 ? (
        <Empty title="No categories yet">Add the first one below.</Empty>
      ) : (
        <div className="table-wrap">
          <table className="table table--tight">
            <thead>
              <tr><th>Name</th><th className="num">Published posts</th><th></th></tr>
            </thead>
            <tbody>
              {list.map((category) => (
                <tr key={category.id} className={busyId === category.id ? 'is-busy' : undefined}>
                  <td>
                    {editing?.id === category.id ? (
                      <form className="inline-form" onSubmit={rename}>
                        <input aria-label={`New name for ${category.name}`} value={editing.name} maxLength={MAX_CATEGORY_NAME} autoFocus
                          onChange={(e) => setEditing({ id: category.id, name: e.target.value })} />
                        <button className="btn btn--small btn--primary" disabled={busyId !== null}>Save</button>
                        <button type="button" className="btn btn--small" onClick={() => setEditing(null)}>Cancel</button>
                      </form>
                    ) : (
                      <div className="cell-title">{category.name}</div>
                    )}
                  </td>
                  <td className="num">{formatNumber(category.postCount)}</td>
                  <td className="actions">
                    {editing?.id !== category.id && (
                      <>
                        <button className="btn btn--small" onClick={() => setEditing({ id: category.id, name: category.name })} disabled={busyId !== null}>Rename</button>
                        <button className="btn btn--small btn--danger" onClick={() => remove(category)} disabled={busyId !== null}>Delete</button>
                      </>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ))}

      <form className="inline-form category-add" onSubmit={add}>
        <input aria-label="New category name" placeholder={full ? `The limit is ${MAX_CATEGORIES} categories` : 'New category, for example Technology and AI'}
          value={name} maxLength={MAX_CATEGORY_NAME} disabled={full || adding} onChange={(e) => setName(e.target.value)} />
        <button className="btn btn--primary" disabled={full || adding || name.trim() === ''}>{adding ? 'Adding…' : 'Add category'}</button>
      </form>
    </section>
  )
}
