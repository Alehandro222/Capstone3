import { useState } from 'react'

// A dialog around a form. onSubmit may be async; return false to keep the dialog open.
export function FormDialog({ title, description, submitLabel, danger, wide, canSubmit = true, onSubmit, onCancel, children }) {
  const [saving, setSaving] = useState(false)

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
      <form
        className={wide ? 'modal modal-wide' : 'modal'}
        onClick={(e) => e.stopPropagation()}
        onSubmit={submit}
        role="dialog"
        aria-label={title}
      >
        <h2>{title}</h2>
        {description && <p className="muted small">{description}</p>}
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
