import { useState } from 'react'
import {
  AGE_GROUPS,
  SECTORS,
  SERVICES,
  SEXES,
  PRIVACY_CONSENT,
  PRIVACY_STATEMENT,
  emptyRegistration,
  registrationProblem,
} from '../registration.js'

// Staff register a visitor at the desk (the DTC login form). With a computer,
// the session on that PC starts right away; without it, the visit is only logged.
//   computer        the PC chosen on the board (fixed), or
//   freeComputers   PCs to choose from in the form (the help desk's "Register and give a PC")
export default function WalkInDialog({ computer: fixedComputer, freeComputers, onSubmit, onCancel }) {
  const [form, setForm] = useState(emptyRegistration)
  const [chosenId, setChosenId] = useState(() => freeComputers?.[0]?.id ?? '')
  const computer = fixedComputer ?? freeComputers?.find((c) => c.id === Number(chosenId))
  const [saving, setSaving] = useState(false)
  const [tried, setTried] = useState(false)

  const problem = registrationProblem(form)
  const set = (field, value) => setForm((f) => ({ ...f, [field]: value }))
  const toggle = (field, value) =>
    setForm((f) => ({
      ...f,
      [field]: f[field].includes(value) ? f[field].filter((v) => v !== value) : [...f[field], value],
    }))

  async function submit(e) {
    e.preventDefault()
    setTried(true)
    if (problem || saving) return
    setSaving(true)
    await onSubmit({ ...form, computerId: computer?.id ?? null })
    setSaving(false)
  }

  return (
    <div className="overlay" onClick={onCancel}>
      <form className="modal modal-wide" onClick={(e) => e.stopPropagation()} onSubmit={submit}>
        <h2>{fixedComputer ? `Register a visitor on ${fixedComputer.name}` : freeComputers ? 'Register a visitor' : 'Log a visit'}</h2>
        <p className="muted small">
          {computer
            ? 'For a visitor without a phone. Their session starts as soon as you save.'
            : 'For a visitor who does not need a computer (Wi-Fi, co-working space, eGov help...).'}
        </p>

        {freeComputers && (
          <label className="field">
            <span>Computer</span>
            <select value={chosenId} onChange={(e) => setChosenId(e.target.value)}>
              {freeComputers.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
              <option value="">No computer (log the visit only)</option>
            </select>
          </label>
        )}

        <div className="form-row">
          <label className="field">
            <span>First name</span>
            <input autoFocus value={form.firstName} onChange={(e) => set('firstName', e.target.value)} />
          </label>
          <label className="field">
            <span>Last name</span>
            <input value={form.lastName} onChange={(e) => set('lastName', e.target.value)} />
          </label>
        </div>

        <Choices legend="Age" type="radio" options={AGE_GROUPS} selected={[form.ageGroup]}
          onChange={(v) => set('ageGroup', v)} />
        <Choices legend="Sex" type="radio" options={SEXES} selected={[form.sex]} onChange={(v) => set('sex', v)} />
        <Choices legend="Sector (select all that apply)" type="checkbox" options={SECTORS}
          selected={form.sectors} onChange={(v) => toggle('sectors', v)} />
        <Choices legend="Purpose / services availed (select all that apply)" type="checkbox" options={SERVICES}
          selected={form.services} onChange={(v) => toggle('services', v)} />

        <details className="privacy">
          <summary>Privacy statement (read it to the visitor)</summary>
          <p>{PRIVACY_STATEMENT}</p>
          <p>{PRIVACY_CONSENT}</p>
        </details>

        {tried && problem && <p className="form-error">{problem}</p>}

        <div className="modal-actions">
          <button type="button" className="btn" onClick={onCancel}>
            Cancel
          </button>
          <button type="submit" className="btn btn-primary" disabled={saving}>
            {saving ? 'Saving…' : computer ? 'Register and start session' : 'Log visit'}
          </button>
        </div>
      </form>
    </div>
  )
}

function Choices({ legend, type, options, selected, onChange }) {
  return (
    <fieldset className="choices">
      <legend>{legend}</legend>
      <div className="choices-grid">
        {options.map((o) => (
          <label key={o.value} className="choice">
            <input
              type={type}
              name={legend}
              checked={selected.includes(o.value)}
              onChange={() => onChange(o.value)}
            />
            <span>{o.label}</span>
          </label>
        ))}
      </div>
    </fieldset>
  )
}
