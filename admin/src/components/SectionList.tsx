import { useRef, useState } from 'react'
import type { ChangeEvent } from 'react'
import { imageUrl } from '../api/client'
import { uploadImage } from '../posts/imageUpload'
import {
  MAX_CAPTION, MAX_REFERENCES, MAX_REFERENCE_TITLE, MAX_SECTIONS, hasContent, moveSection, newSection,
  removeSection, sectionLabels, updateSection,
} from '../posts/sections'
import type { Reference, Section, SectionType } from '../posts/sections'
import { useConfirm } from './feedback'
import { GenerateImagePanel } from './GenerateImagePanel'
import { SparkleIcon } from './SparkleIcon'
import { RichEditor } from './RichEditor'
import { ErrorNote } from './ui'

/** Takes an updater, not a value, so a change made from inside an editor never works from a stale list. */
type Update = (change: (sections: Section[]) => Section[]) => void

const ADDABLE: SectionType[] = ['text', 'image', 'references']

export function SectionList({ sections, onChange, postId = null }: { sections: Section[]; onChange: Update; postId?: string | null }) {
  const confirm = useConfirm()

  const edit = (key: string, change: (section: Section) => Section) => onChange((current) => updateSection(current, key, change))

  async function remove(section: Section) {
    if (hasContent(section)) {
      const ok = await confirm({
        title: `Delete this ${sectionLabels[section.type].toLowerCase()} section?`,
        message: 'What is in it will be removed from the post.',
        confirmLabel: 'Delete section',
        danger: true,
      })
      if (!ok) return
    }
    onChange((current) => removeSection(current, section.key))
  }

  return (
    <>
      {sections.length === 0 && <p className="muted">This post has no sections yet. Add one below.</p>}

      <ol className="sections">
        {sections.map((section, index) => (
          <li key={section.key} className="section">
            <header className="section__head">
              <span className="section__label">{sectionLabels[section.type]}</span>
              <span className="spacer" />
              <button type="button" className="section__tool" aria-label={`Move ${sectionLabels[section.type].toLowerCase()} section up`} title="Move up"
                disabled={index === 0} onClick={() => onChange((current) => moveSection(current, section.key, -1))}>↑</button>
              <button type="button" className="section__tool" aria-label={`Move ${sectionLabels[section.type].toLowerCase()} section down`} title="Move down"
                disabled={index === sections.length - 1} onClick={() => onChange((current) => moveSection(current, section.key, 1))}>↓</button>
              <button type="button" className="section__tool section__tool--danger" aria-label={`Delete ${sectionLabels[section.type].toLowerCase()} section`} title="Delete"
                onClick={() => remove(section)}>✕</button>
            </header>

            {section.type === 'text' && (
              <RichEditor postId={postId} initialContent={section.doc} onChange={(doc) => edit(section.key, (s) => (s.type === 'text' ? { ...s, doc } : s))} />
            )}
            {section.type === 'image' && (
              <ImageEditor section={section} onChange={(change) => edit(section.key, (s) => (s.type === 'image' ? { ...s, ...change } : s))} />
            )}
            {section.type === 'references' && (
              <ReferencesEditor items={section.items} onChange={(change) => edit(section.key, (s) => (s.type === 'references' ? { ...s, items: change(s.items) } : s))} />
            )}
          </li>
        ))}
      </ol>

      <div className="section-add" role="group" aria-label="Add a section">
        <span className="muted">Add a section:</span>
        {ADDABLE.map((type) => (
          <button key={type} type="button" className="btn" disabled={sections.length >= MAX_SECTIONS}
            onClick={() => onChange((current) => [...current, newSection(type)])}>
            + {sectionLabels[type]}
          </button>
        ))}
      </div>
    </>
  )
}

type ImageSection = Extract<Section, { type: 'image' }>

function ImageEditor({ section, onChange }: { section: ImageSection; onChange: (change: Partial<ImageSection>) => void }) {
  const input = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [generating, setGenerating] = useState(false)

  async function choose(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    // Clearing the input lets the same file be chosen again after a failure.
    event.target.value = ''
    if (!file) return

    setBusy(true)
    setError(null)
    try {
      const image = await uploadImage(file)
      onChange({ imageId: image.id, width: image.width, height: image.height })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'That picture could not be uploaded.')
    } finally {
      setBusy(false)
    }
  }

  // A generated picture takes the description as its text for screen readers only if none was written yet.
  const panel = (
    <GenerateImagePanel
      onClose={() => setGenerating(false)}
      onKeep={({ imageId, width, height, alt }) => {
        onChange({ imageId, width, height, ...(section.alt.trim() === '' ? { alt } : {}) })
        setGenerating(false)
      }}
    />
  )

  return (
    <div className="section__body">
      {error && <ErrorNote message={error} />}
      <input ref={input} type="file" accept="image/jpeg,image/png,image/webp" hidden onChange={choose} />

      {section.imageId ? (
        <>
          <img
            className="section__image"
            src={imageUrl(section.imageId)}
            alt={section.alt}
            // Pictures added before sizes were kept get theirs filled in here, the next time the post is saved.
            onLoad={(event) => {
              const { naturalWidth, naturalHeight } = event.currentTarget
              if (section.width === null && naturalWidth > 0 && naturalHeight > 0) onChange({ width: naturalWidth, height: naturalHeight })
            }}
          />
          <div className="field">
            <label htmlFor={`alt-${section.key}`}>Describe the picture</label>
            <input id={`alt-${section.key}`} value={section.alt} maxLength={MAX_CAPTION} onChange={(e) => onChange({ alt: e.target.value })} />
            {section.alt.trim() === ''
              ? <p className="field__hint field__hint--warn">No description yet. Someone using a screen reader hears nothing for this picture, and search engines cannot tell what it shows.</p>
              : <p className="field__hint">Read aloud to people who cannot see it, and helps the post be found. Not shown on the page.</p>}
          </div>
          <div className="field">
            <label htmlFor={`caption-${section.key}`}>Caption (optional)</label>
            <input id={`caption-${section.key}`} value={section.caption} maxLength={MAX_CAPTION} onChange={(e) => onChange({ caption: e.target.value })} />
          </div>
          <div className="generate__actions">
            <button type="button" className="btn btn--small" disabled={busy} onClick={() => input.current?.click()}>
              {busy ? 'Uploading…' : 'Replace picture'}
            </button>
            {!generating && <button type="button" className="btn btn--small" onClick={() => setGenerating(true)}><SparkleIcon />Generate a different one</button>}
          </div>
          {generating && panel}
        </>
      ) : (
        <div className="section__drop">
          <button type="button" className="btn btn--primary" disabled={busy} onClick={() => input.current?.click()}>
            {busy ? 'Uploading…' : 'Choose a picture'}
          </button>
          {!generating && <button type="button" className="btn" onClick={() => setGenerating(true)}><SparkleIcon />Generate with AI</button>}
          <p className="field__hint">JPEG, PNG or WebP. Large pictures are shrunk automatically before they are uploaded.</p>
          {generating && panel}
        </div>
      )}
    </div>
  )
}

function ReferencesEditor({ items, onChange }: { items: Reference[]; onChange: (change: (items: Reference[]) => Reference[]) => void }) {
  const set = (index: number, change: Partial<Reference>) =>
    onChange((current) => current.map((item, i) => (i === index ? { ...item, ...change } : item)))

  return (
    <div className="section__body">
      <ol className="references">
        {items.map((item, index) => (
          // Rows have no identity of their own, and they are only ever appended or removed, so position is a stable key.
          <li key={index} className="reference">
            <input aria-label={`Source ${index + 1} title`} placeholder="Title of the source" value={item.title} maxLength={MAX_REFERENCE_TITLE}
              onChange={(e) => set(index, { title: e.target.value })} />
            <input aria-label={`Source ${index + 1} link`} placeholder="https://… (optional)" inputMode="url" value={item.url}
              onChange={(e) => set(index, { url: e.target.value })} />
            <button type="button" className="section__tool section__tool--danger" aria-label={`Remove source ${index + 1}`} title="Remove"
              onClick={() => onChange((current) => current.filter((_, i) => i !== index))}>✕</button>
          </li>
        ))}
      </ol>
      <button type="button" className="btn btn--small" disabled={items.length >= MAX_REFERENCES}
        onClick={() => onChange((current) => [...current, { title: '', url: '' }])}>
        + Add a source
      </button>
    </div>
  )
}
