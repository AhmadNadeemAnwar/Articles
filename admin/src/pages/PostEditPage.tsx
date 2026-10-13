import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ChangeEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api, assetUrl } from '../api/client'
import type { PostStatus } from '../api/types'
import type { Category, PostRevisionDetail } from '../api/types'
import { OriginalTexts } from '../components/OriginalTexts'
import { RevisionHistory } from '../components/RevisionHistory'
import { SectionList } from '../components/SectionList'
import { TagInput } from '../components/TagInput'
import { useConfirm, useToast } from '../components/feedback'
import { ErrorNote, PageHeader, Spinner, formatDateTime, publicPostUrl, statusLabels } from '../components/ui'
import { uploadImage } from '../posts/imageUpload'
import { findProblem, newSection, parseContent, serializeContent } from '../posts/sections'
import type { Section } from '../posts/sections'

// Slow enough that steady typing stays well under the API's limit of 30 writes a minute.
const AUTOSAVE_DELAY_MS = 4000

interface Draft {
  title: string
  subtitle: string
  contentJson: string
  coverImageUrl: string
  tags: string[]
  /** Empty for "no category". */
  categoryId: string
}

const EMPTY_DRAFT: Draft = { title: '', subtitle: '', contentJson: serializeContent([newSection('text')]), coverImageUrl: '', tags: [], categoryId: '' }

const STATUSES: PostStatus[] = ['Draft', 'Published', 'Inactive']

/**
 * Unsaved work is mirrored to this browser, because an admin session ends after two hours and a
 * save that arrives too late would otherwise take the writing with it.
 */
const backupKey = (id: string | null) => `articles.admin.unsaved:${id ?? 'new'}`

function readBackup(id: string | null): { draft: Draft; at: string } | null {
  try {
    const raw = localStorage.getItem(backupKey(id))
    if (!raw) return null
    const parsed = JSON.parse(raw) as { draft?: Draft; at?: string }
    return parsed.draft && typeof parsed.draft.title === 'string' && parsed.at ? { draft: parsed.draft, at: parsed.at } : null
  } catch {
    return null
  }
}

function writeBackup(id: string | null, draft: Draft) {
  try {
    localStorage.setItem(backupKey(id), JSON.stringify({ draft, at: new Date().toISOString() }))
  } catch {
    // Storage full or unavailable: the copy is a safety net, so writing carries on without it.
  }
}

function clearBackup(id: string | null) {
  try {
    localStorage.removeItem(backupKey(id))
  } catch {
    // Nothing to clear.
  }
}

export function PostEditPage() {
  const { id: routeId } = useParams()
  const navigate = useNavigate()
  const confirm = useConfirm()
  const notify = useToast()

  const [postId, setPostId] = useState<string | null>(routeId ?? null)
  const [title, setTitle] = useState('')
  const [subtitle, setSubtitle] = useState('')
  const [sections, setSections] = useState<Section[]>(() => [newSection('text')])
  const [coverImageUrl, setCoverImageUrl] = useState('')
  const [tags, setTags] = useState<string[]>([])
  const [categoryId, setCategoryId] = useState('')
  const [categories, setCategories] = useState<Category[]>([])
  const [status, setStatus] = useState<PostStatus>('Draft')
  const [slug, setSlug] = useState<string | null>(null)

  const [ready, setReady] = useState(!routeId)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [coverBusy, setCoverBusy] = useState(false)
  const [savedAt, setSavedAt] = useState<Date | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [recovered, setRecovered] = useState<{ draft: Draft; at: string } | null>(null)

  const coverInput = useRef<HTMLInputElement>(null)

  const contentJson = useMemo(() => serializeContent(sections), [sections])
  const problem = useMemo(() => findProblem(sections), [sections])
  const snapshot = JSON.stringify({ title, subtitle, contentJson, coverImageUrl, tags, categoryId } satisfies Draft)
  // What the server holds. Anything different on screen is unsaved.
  const [baseline, setBaseline] = useState(() => JSON.stringify(EMPTY_DRAFT))
  const dirty = snapshot !== baseline

  // The post this page has already loaded, so putting a new post's id in the URL does not reload it.
  const loadedId = useRef<string | null>(null)
  const savingNow = useRef(false)

  function apply(draft: Draft) {
    setTitle(draft.title)
    setSubtitle(draft.subtitle)
    setSections(parseContent(draft.contentJson))
    setCoverImageUrl(draft.coverImageUrl)
    setTags(draft.tags)
    // A copy saved by an earlier version of this page has no category in it.
    setCategoryId(draft.categoryId ?? '')
  }

  // The list is small and rarely changes; if it cannot be loaded the post can still be written and saved.
  useEffect(() => {
    api.categories().then(setCategories).catch(() => {})
  }, [])

  useEffect(() => {
    if (!routeId) {
      setRecovered(readBackup(null))
      return
    }
    if (loadedId.current === routeId) return

    let cancelled = false
    setReady(false)
    setLoadError(null)

    api.postForEdit(routeId)
      .then((post) => {
        if (cancelled) return
        const opened = parseContent(post.contentJson)
        const loaded: Draft = {
          title: post.title,
          subtitle: post.subtitle ?? '',
          // The body as this editor would store it. An older single-document post therefore opens
          // "unchanged" rather than looking edited the moment it is opened.
          contentJson: serializeContent(opened),
          coverImageUrl: post.coverImageUrl ?? '',
          tags: post.tags.map((t) => t.name),
          categoryId: post.category?.id ?? '',
        }
        setTitle(loaded.title)
        setSubtitle(loaded.subtitle)
        setSections(opened)
        setCoverImageUrl(loaded.coverImageUrl)
        setTags(loaded.tags)
        setCategoryId(loaded.categoryId)
        setBaseline(JSON.stringify(loaded))
        setPostId(post.id)
        setStatus(post.status)
        setSlug(post.slug)
        loadedId.current = post.id

        const backup = readBackup(post.id)
        setRecovered(backup && JSON.stringify(backup.draft) !== JSON.stringify(loaded) ? backup : null)
        setReady(true)
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoadError(err instanceof Error ? err.message : 'Could not open this post.')
      })

    return () => { cancelled = true }
  }, [routeId])

  /** Saves the post and returns its id, so a change of status can act on one that was only just created. */
  const save = useCallback(async (): Promise<string | null> => {
    if (!title.trim() || savingNow.current) return postId
    if (problem) {
      setError(problem)
      return null
    }

    savingNow.current = true
    setSaving(true)
    setError(null)
    const sent = snapshot

    try {
      const payload = {
        title: title.trim(),
        subtitle: subtitle.trim() || null,
        contentJson,
        coverImageUrl: coverImageUrl.trim() || null,
        tags,
        categoryId: categoryId || null,
      }
      const post = postId ? await api.updatePost(postId, payload) : await api.createPost(payload)

      setBaseline(sent)
      clearBackup(postId)
      clearBackup(post.id)
      setPostId(post.id)
      setStatus(post.status)
      setSlug(post.slug)
      setSavedAt(new Date())

      if (!postId) {
        // Put the new id in the address so a refresh reopens this post instead of a blank one.
        loadedId.current = post.id
        navigate(`/posts/${post.id}/edit`, { replace: true })
      }
      return post.id
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save.')
      return null
    } finally {
      savingNow.current = false
      setSaving(false)
    }
  }, [title, subtitle, contentJson, coverImageUrl, tags, categoryId, postId, snapshot, problem, navigate])

  // A post nobody can see saves itself. A published post is live, so its changes wait for the Save button.
  useEffect(() => {
    if (!ready || !dirty || status === 'Published' || !title.trim() || recovered || problem) return
    const timer = setTimeout(save, AUTOSAVE_DELAY_MS)
    return () => clearTimeout(timer)
  }, [ready, dirty, status, title, recovered, problem, snapshot, save])

  useEffect(() => {
    if (!ready || !dirty || recovered) return
    writeBackup(postId, JSON.parse(snapshot) as Draft)
  }, [ready, dirty, recovered, postId, snapshot])

  useEffect(() => {
    if (!dirty) return
    const guard = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', guard)
    return () => window.removeEventListener('beforeunload', guard)
  }, [dirty])

  async function changeStatus(target: PostStatus, ask: boolean) {
    if (target === status) return

    if (ask) {
      const ok = await confirm(
        target === 'Published'
          ? { title: 'Publish this post?', message: 'It goes live on the site straight away.', confirmLabel: 'Publish' }
          : status === 'Published'
            ? {
                title: target === 'Inactive' ? 'Make this post not active?' : 'Move this post back to draft?',
                message: 'It comes off the site at once. Nothing is deleted, and it keeps its address for when you publish it again.',
                confirmLabel: target === 'Inactive' ? 'Make not active' : 'Move to draft',
              }
            : { title: `Change this post to ${statusLabels[target].toLowerCase()}?`, message: 'It stays hidden from readers either way.', confirmLabel: 'Change' },
      )
      if (!ok) return
    }

    const id = dirty || !postId ? await save() : postId
    if (!id) return

    setSaving(true)
    try {
      // The admin route records the change in the Activity history; the post is then re-read for its address.
      await api.adminSetPostStatus(id, target)
      const post = await api.postForEdit(id)
      setStatus(post.status)
      setSlug(post.slug)
      notify(target === 'Published' ? 'Published. It is live now.' : `Post is now ${statusLabels[target].toLowerCase()}.`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not change the status of this post.')
    } finally {
      setSaving(false)
    }
  }

  async function chooseCover(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return

    setCoverBusy(true)
    setError(null)
    try {
      const image = await uploadImage(file)
      setCoverImageUrl(image.path)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'That picture could not be uploaded.')
    } finally {
      setCoverBusy(false)
    }
  }

  /** Puts an earlier version on screen as unsaved changes; the writer decides whether to keep it. */
  function restoreRevision(revision: PostRevisionDetail) {
    setTitle(revision.title)
    setSections(parseContent(revision.contentJson))
    notify('Earlier version restored. Press Save to keep it.')
  }

  function restore() {
    if (!recovered) return
    apply(recovered.draft)
    setRecovered(null)
  }

  function discardRecovered() {
    clearBackup(postId)
    setRecovered(null)
  }

  if (loadError) {
    return (
      <>
        <PageHeader title="Post" />
        <ErrorNote message={loadError} />
        <p><Link to="/posts">Back to posts</Link></p>
      </>
    )
  }
  if (!ready) return <Spinner label="Opening post…" />

  const live = status === 'Published'
  const url = live ? publicPostUrl(slug) : null
  const state = saving ? 'Working…'
    : !title.trim() ? 'Add a title to start saving'
    : problem ? 'Not saved: see the note above'
    : dirty ? 'Unsaved changes'
    : savedAt ? `Saved ${savedAt.toLocaleTimeString()}`
    : postId ? 'All changes saved' : 'Not saved yet'

  return (
    <div className="writer">
      <PageHeader title={postId ? 'Edit post' : 'New post'}>
        <label className="status-pick">
          <span className="muted">Status</span>
          <select value={status} disabled={saving || !title.trim()} onChange={(e) => changeStatus(e.target.value as PostStatus, true)}>
            {STATUSES.map((value) => <option key={value} value={value}>{statusLabels[value]}</option>)}
          </select>
        </label>
        <Link className="btn" to="/posts">Back to posts</Link>
      </PageHeader>

      {recovered && (
        <div className="note" role="status">
          <span>Unsaved writing from {formatDateTime(recovered.at)} was found on this device.</span>
          <span className="spacer" />
          <button className="btn btn--small" onClick={discardRecovered}>Discard it</button>
          <button className="btn btn--small btn--primary" onClick={restore}>Restore it</button>
        </div>
      )}
      {error && <ErrorNote message={error} />}
      {problem && problem !== error && <ErrorNote message={problem} />}

      <input className="writer__title" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Title" aria-label="Title" maxLength={160} />
      <input className="writer__subtitle" value={subtitle} onChange={(e) => setSubtitle(e.target.value)} placeholder="Add a subtitle…" aria-label="Subtitle" maxLength={300} />

      <SectionList sections={sections} onChange={setSections} postId={postId} />

      <section className="panel writer__details">
        <div className="field">
          <label>Cover picture (optional)</label>
          <input ref={coverInput} type="file" accept="image/jpeg,image/png,image/webp" hidden onChange={chooseCover} />
          {coverImageUrl && <img className="writer__cover" src={assetUrl(coverImageUrl)} alt="" />}
          <div className="writer__cover-actions">
            <button type="button" className="btn btn--small" disabled={coverBusy} onClick={() => coverInput.current?.click()}>
              {coverBusy ? 'Uploading…' : coverImageUrl ? 'Replace picture' : 'Choose a picture'}
            </button>
            {coverImageUrl && <button type="button" className="btn btn--small btn--danger" onClick={() => setCoverImageUrl('')}>Remove</button>}
          </div>
          <p className="field__hint">Shown at the top of the post, above the first section.</p>
        </div>
        <div className="field">
          <label htmlFor="category">Category</label>
          <select id="category" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
            <option value="">No category</option>
            {categories.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}
            {/* A post can sit in a category that has since been deleted from this list until it is next saved. */}
            {categoryId && !categories.some((c) => c.id === categoryId) && <option value={categoryId}>(current category)</option>}
          </select>
          <p className="field__hint">
            One broad shelf for the post. You can also move a post from the <Link to="/posts">Posts</Link> list. {categories.length === 0 ? <>None exist yet: add them under <Link to="/site">Site</Link>.</> : <>Manage the list under <Link to="/site">Site</Link>.</>}
          </p>
        </div>
        <div className="field">
          <label>Topics (up to 5)</label>
          <TagInput value={tags} onChange={setTags} />
          <p className="field__hint">Type a topic and press Enter. Topics are how readers find this post.</p>
        </div>
        {postId && <RevisionHistory postId={postId} onRestore={restoreRevision} />}
        {postId && <OriginalTexts postId={postId} />}
      </section>

      <div className="actionbar" role="group" aria-label="Post actions">
        <span className={`muted${(dirty || problem) && title.trim() ? ' unsaved' : ''}`}>{state}</span>
        <span className="spacer" />
        {url && <a className="btn" href={url} target="_blank" rel="noopener noreferrer">View</a>}
        <button className={`btn${live ? ' btn--primary' : ''}`} onClick={save} disabled={saving || !dirty || !title.trim() || problem !== null}>
          {live ? 'Save changes' : 'Save draft'}
        </button>
        {!live && <button className="btn btn--primary" onClick={() => changeStatus('Published', false)} disabled={saving || !title.trim() || problem !== null}>Publish</button>}
      </div>
    </div>
  )
}
