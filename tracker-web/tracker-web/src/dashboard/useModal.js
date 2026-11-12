import { useEffect, useRef } from 'react'

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

// Makes a dialog usable from the keyboard and with a screen reader:
//  - focus moves into it when it opens (unless a field already has autoFocus),
//  - Tab and Shift+Tab stay inside it,
//  - Escape closes it,
//  - focus goes back to whatever opened it when it closes.
// Put the returned ref on the dialog element (with role="dialog" aria-modal="true").
export function useModal(onClose) {
  const ref = useRef(null)
  const closeRef = useRef(onClose)
  closeRef.current = onClose
  // Remembered during the first render, before a field inside grabs focus (autoFocus).
  const openerRef = useRef(typeof document === 'undefined' ? null : document.activeElement)

  useEffect(() => {
    const node = ref.current
    if (!node) return undefined
    const opener = openerRef.current

    if (!node.contains(document.activeElement)) {
      ;(node.querySelector('[autofocus]') ?? node.querySelector(FOCUSABLE) ?? node).focus()
    }

    function onKeyDown(e) {
      if (e.key === 'Escape') {
        e.stopPropagation() // only the innermost dialog closes
        closeRef.current?.()
        return
      }
      if (e.key !== 'Tab') return
      const items = [...node.querySelectorAll(FOCUSABLE)].filter((el) => el.offsetParent !== null || el === document.activeElement)
      if (items.length === 0) {
        e.preventDefault()
        return
      }
      const first = items[0]
      const last = items[items.length - 1]
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault()
        first.focus()
      }
    }

    node.addEventListener('keydown', onKeyDown)
    return () => {
      node.removeEventListener('keydown', onKeyDown)
      if (opener && typeof opener.focus === 'function' && document.contains(opener)) opener.focus()
    }
  }, [])

  return ref
}
