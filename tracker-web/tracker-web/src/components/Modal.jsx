import { useState } from 'react'

// A small reusable form dialog. `fields` describes the inputs to show.
export default function Modal({ title, fields, submitLabel, onSubmit, onCancel }) {
  const [values, setValues] = useState(() =>
    Object.fromEntries(fields.map((f) => [f.name, ''])),
  )
  const [saving, setSaving] = useState(false)

  const canSubmit = fields.every((f) => values[f.name].trim().length > 0) && !saving

  async function handleSubmit(e) {
    e.preventDefault() // stop the browser from reloading the page
    if (!canSubmit) return
    setSaving(true)
    await onSubmit(values)
    setSaving(false)
  }

  return (
    <div className="overlay" onClick={onCancel}>
      <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleSubmit}>
        <h2>{title}</h2>

        {fields.map((f) => (
          <label key={f.name} className="field">
            <span>{f.label}</span>
            {f.multiline ? (
              <textarea
                rows={4}
                autoFocus={f === fields[0]}
                placeholder={f.placeholder}
                value={values[f.name]}
                onChange={(e) => setValues({ ...values, [f.name]: e.target.value })}
              />
            ) : (
              <input
                autoFocus={f === fields[0]}
                placeholder={f.placeholder}
                value={values[f.name]}
                onChange={(e) => setValues({ ...values, [f.name]: e.target.value })}
              />
            )}
          </label>
        ))}

        <div className="modal-actions">
          <button type="button" className="btn" onClick={onCancel}>
            Cancel
          </button>
          <button type="submit" className="btn btn-primary" disabled={!canSubmit}>
            {saving ? 'Saving…' : submitLabel}
          </button>
        </div>
      </form>
    </div>
  )
}
