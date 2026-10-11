import { useEffect, useId, useRef, useState } from 'react'
import { backgroundProblem, toHex } from '../lib/palette'
import type { ThemeColors } from '../lib/palette'
import { chooseReaderColors, pickerColors, readerColors } from '../lib/theme'

/**
 * Lets a reader pick three colours of their own. The choice is applied as they pick, kept in this
 * browser only, and never sent anywhere; "Use the site's colours" forgets it.
 */
export function ThemePicker() {
  const id = useId()
  const wrap = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [colors, setColors] = useState<ThemeColors>(() => pickerColors())
  const [own, setOwn] = useState(() => readerColors() !== null)
  const [problem, setProblem] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') setOpen(false) }
    const onPress = (event: PointerEvent) => { if (!wrap.current?.contains(event.target as Node)) setOpen(false) }
    document.addEventListener('keydown', onKey)
    document.addEventListener('pointerdown', onPress)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onPress)
    }
  }, [open])

  function toggle() {
    if (!open) {
      // The site's look may have arrived since this was last opened.
      setColors(pickerColors())
      setProblem(null)
    }
    setOpen(!open)
  }

  function pick(part: keyof ThemeColors, value: string) {
    const hex = toHex(value)
    if (!hex) return
    const next = { ...colors, [part]: hex }
    setColors(next)

    const trouble = backgroundProblem(next.background)
    setProblem(trouble)
    if (!trouble && chooseReaderColors(next)) setOwn(true)
  }

  function reset() {
    chooseReaderColors(null)
    setOwn(false)
    setProblem(null)
    setColors(pickerColors())
  }

  return (
    <div className="palette" ref={wrap}>
      <button type="button" className="palette__button" aria-label="Choose your own colours" title="Choose your own colours"
        aria-expanded={open} aria-controls={`${id}-panel`} onClick={toggle}>
        <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
          <path d="M12 3a9 9 0 1 0 0 18c1.2 0 1.9-.9 1.9-1.9 0-.5-.2-.9-.5-1.3-.3-.3-.5-.8-.5-1.3 0-1 .9-1.9 1.9-1.9H17a4 4 0 0 0 4-4c0-4.2-4-7.6-9-7.6Z" />
          <circle cx="7.5" cy="11.5" r="1" fill="currentColor" stroke="none" />
          <circle cx="10.5" cy="7.5" r="1" fill="currentColor" stroke="none" />
          <circle cx="15" cy="7.5" r="1" fill="currentColor" stroke="none" />
        </svg>
        <span className="palette__label">Colours</span>
      </button>

      {open && (
        <div className="palette__panel" id={`${id}-panel`} role="group" aria-label="Your colours">
          <p className="palette__title">Your colours</p>

          <label className="palette__row" htmlFor={`${id}-main`}>
            <span>Header<small>The bar across the top</small></span>
            <input id={`${id}-main`} type="color" value={colors.main} onChange={(e) => pick('main', e.target.value)} />
          </label>

          <label className="palette__row" htmlFor={`${id}-accent`}>
            <span>Links and buttons<small>Whatever you can click</small></span>
            <input id={`${id}-accent`} type="color" value={colors.accent} onChange={(e) => pick('accent', e.target.value)} />
          </label>

          <label className="palette__row" htmlFor={`${id}-background`}>
            <span>Background<small>The page behind the text</small></span>
            <input id={`${id}-background`} type="color" value={colors.background} onChange={(e) => pick('background', e.target.value)}
              aria-describedby={problem ? `${id}-problem` : undefined} />
          </label>

          {problem && <p className="palette__problem" id={`${id}-problem`} role="alert">{problem}</p>}

          <p className="palette__note">
            Text colours are worked out for you so the page stays readable. Your choice is kept on this device only.
          </p>

          <button type="button" className="btn" onClick={reset} disabled={!own}>Use the site’s colours</button>
        </div>
      )}
    </div>
  )
}
