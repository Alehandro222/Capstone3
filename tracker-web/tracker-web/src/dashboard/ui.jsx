import { useId, useState } from 'react'
import { useModal } from './useModal.js'

// A dialog around a form. onSubmit may be async; return false to keep the dialog open.
export function FormDialog({ title, description, submitLabel, danger, wide, canSubmit = true, onSubmit, onCancel, children }) {
  const [saving, setSaving] = useState(false)
  const ref = useModal(onCancel)
  const titleId = useId()
  const descriptionId = useId()

  async function submit(e) {
    e.preventDefault()
    if (saving || !canSubmit) return
    setSaving(true)
    try {
      await onSubmit()
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="overlay" onClick={onCancel}>
      <div
        className={wide ? 'modal modal-wide' : 'modal'}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        ref={ref}
        tabIndex={-1}
      >
      <form className="modal-form" onSubmit={submit}>
        <h2 id={titleId}>{title}</h2>
        {description && <p className="muted small" id={descriptionId}>{description}</p>}
        {children}
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="submit"
            className={danger ? 'btn btn-danger' : 'btn btn-primary'}
            disabled={saving || !canSubmit}
            autoFocus={!children}
          >
            {saving ? 'Working…' : submitLabel}
          </button>
        </div>
      </form>
      </div>
    </div>
  )
}

// "Are you sure?" for actions that cannot be undone.
export function ConfirmDialog({ title, message, confirmLabel, danger = true, onConfirm, onCancel }) {
  return (
    <FormDialog title={title} description={message} submitLabel={confirmLabel} danger={danger}
      onSubmit={onConfirm} onCancel={onCancel} />
  )
}

export function Badge({ tone, children }) {
  return <span className={`badge badge-${tone}`}>{children}</span>
}

export function Empty({ children }) {
  return <p className="muted empty">{children}</p>
}
