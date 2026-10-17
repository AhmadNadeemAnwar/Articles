import { useEffect, useRef } from 'react'

/** The game page, asked to leave out its own "back to Articles" link because this window has a close button instead. */
export const GAME_URL = '/play/?embed=1'

/**
 * Catch the Ball in a window over the page the reader is on. The game itself is its own page, shown in a frame, so its
 * full-screen styles and key handling stay inside it. Closing the window removes the frame, which ends the game and its sound.
 */
export function GameDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const frame = useRef<HTMLIFrameElement>(null)

  useEffect(() => {
    const element = dialog.current
    if (!element) return
    if (open && !element.open) element.showModal()
    if (!open && element.open) element.close()
  }, [open])

  return (
    <dialog ref={dialog} className="game-dialog" aria-label="Catch the Ball game"
      onClose={onClose}
      onCancel={(event) => { event.preventDefault(); onClose() }}
      onClick={(event) => { if (event.target === dialog.current) onClose() }}>
      {open && (
        <>
          <button type="button" className="game-dialog__close" aria-label="Close the game" title="Close" onClick={onClose}>×</button>
          <iframe ref={frame} className="game-dialog__frame" src={GAME_URL} title="Catch the Ball"
            // Keys go straight to the game, so the arrow keys move the basket at once.
            onLoad={() => frame.current?.focus()} />
        </>
      )}
    </dialog>
  )
}
