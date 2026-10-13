import { useEffect } from 'react'
import { EditorContent, useEditor } from '@tiptap/react'
import type { Editor as TiptapEditor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import Placeholder from '@tiptap/extension-placeholder'
import Link from '@tiptap/extension-link'
import { GrammarPanel } from './GrammarPanel'
import { ImprovePanel } from './ImprovePanel'

interface Props {
  /** Stored ProseMirror JSON, or null for a new post. */
  initialContent: string | null
  onChange: (contentJson: string) => void
  /** Told, just before the AI's improved text replaces the section's, what was there and what replaces it (both the editor's JSON). */
  onImproved?: (previousDoc: string, newDoc: string) => void
}

export function RichEditor({ initialContent, onChange, onImproved }: Props) {
  const editor = useEditor({
    extensions: [
      StarterKit.configure({ heading: { levels: [2, 3] } }),
      Placeholder.configure({ placeholder: 'Write your post…' }),
      Link.configure({ openOnClick: false, autolink: true }),
    ],
    content: initialContent ? safeParse(initialContent) : '',
    onUpdate: ({ editor }) => onChange(JSON.stringify(editor.getJSON())),
  })

  // A draft loaded after first render (the edit route fetches asynchronously) must replace
  // the empty document without pushing an entry onto the undo stack.
  useEffect(() => {
    if (!editor || !initialContent) return
    const incoming = safeParse(initialContent)
    if (JSON.stringify(editor.getJSON()) === JSON.stringify(incoming)) return

    editor.commands.setContent(incoming, { emitUpdate: false })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editor, initialContent])

  if (!editor) return null

  return (
    <>
      <Toolbar editor={editor} />
      <EditorContent editor={editor} className="prose" />
      <GrammarPanel editor={editor}>
        <ImprovePanel editor={editor} onImproved={onImproved} />
      </GrammarPanel>
    </>
  )
}

function Toolbar({ editor }: { editor: TiptapEditor }) {
  const tool = (active: boolean) => `editor__tool${active ? ' editor__tool--active' : ''}`

  return (
    <div className="editor__toolbar" role="toolbar" aria-label="Formatting">
      <button type="button" aria-label="Bold" className={tool(editor.isActive('bold'))}
        onClick={() => editor.chain().focus().toggleBold().run()}>
        <strong>B</strong>
      </button>
      <button type="button" aria-label="Italic" className={tool(editor.isActive('italic'))}
        onClick={() => editor.chain().focus().toggleItalic().run()}>
        <em>I</em>
      </button>
      <button type="button" className={tool(editor.isActive('heading', { level: 2 }))}
        onClick={() => editor.chain().focus().toggleHeading({ level: 2 }).run()}>
        H2
      </button>
      <button type="button" className={tool(editor.isActive('heading', { level: 3 }))}
        onClick={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}>
        H3
      </button>
      <button type="button" className={tool(editor.isActive('blockquote'))}
        onClick={() => editor.chain().focus().toggleBlockquote().run()}>
        Quote
      </button>
      <button type="button" className={tool(editor.isActive('bulletList'))}
        onClick={() => editor.chain().focus().toggleBulletList().run()}>
        List
      </button>
      <button type="button" className={tool(editor.isActive('orderedList'))}
        onClick={() => editor.chain().focus().toggleOrderedList().run()}>
        1. List
      </button>
      <button type="button" className={tool(editor.isActive('codeBlock'))}
        onClick={() => editor.chain().focus().toggleCodeBlock().run()}>
        Code
      </button>
      <button type="button" className={tool(editor.isActive('link'))} onClick={() => promptForLink(editor)}>
        Link
      </button>
      <button type="button" aria-label="Divider line" className="editor__tool"
        onClick={() => editor.chain().focus().setHorizontalRule().run()}>
        —
      </button>
    </div>
  )
}

function promptForLink(editor: TiptapEditor) {
  const previous = editor.getAttributes('link').href as string | undefined
  const href = window.prompt('Link URL', previous ?? 'https://')

  if (href === null) return
  if (href === '') {
    editor.chain().focus().extendMarkRange('link').unsetLink().run()
    return
  }

  // Only http(s) links are accepted; the reading view drops anything else anyway.
  if (!/^https?:\/\//i.test(href)) {
    window.alert('Links must start with http:// or https://')
    return
  }

  editor.chain().focus().extendMarkRange('link').setLink({ href }).run()
}

function safeParse(json: string): object | string {
  try {
    return JSON.parse(json)
  } catch {
    return ''
  }
}
