import { useState } from 'react'
import { api } from '../api.js'

const PURPOSES = [
  'Job application',
  'School work or research',
  'Government forms & services',
  'Printing or documents',
  'Online meeting or class',
  'Other',
]
const OTHER = 'Other'
const NAME_MAX = 100
const PURPOSE_MAX = 200 // matches the limit enforced by the API

export default function KioskLogin({ computer, onStarted }) {
  const [name, setName] = useState('')
  const [choice, setChoice] = useState('') // one of PURPOSES
  const [other, setOther] = useState('') // typed text when "Other" is chosen
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const purpose = choice === OTHER ? other.trim() : choice
  const canSubmit = name.trim().length >= 2 && purpose.length > 0 && !busy

  async function handleSubmit(e) {
    e.preventDefault() // don't reload the page
    if (!canSubmit) return

    setBusy(true)
    setError('')
    try {
      // This is the step that records the user in the tracker.
      await api.startSession(computer.id, name.trim(), purpose)
      await onStarted() // reload; the screen switches to the session view
    } catch (err) {
      setError(
        err instanceof TypeError
          ? 'Cannot reach the tracker. Please ask a staff member for help.'
          : err.message,
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="k-card" onSubmit={handleSubmit}>
      <h1>Welcome</h1>
      <p className="k-lead">Log in to start using {computer.name}.</p>

      <label className="k-field">
        <span>Your full name</span>
        <input
          autoFocus
          autoComplete="off"
          maxLength={NAME_MAX}
          placeholder="e.g. Maria Santos"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
      </label>

      <div className="k-field">
        <span id="purpose-label">What will you use the computer for?</span>
        <div className="k-chips" role="group" aria-labelledby="purpose-label">
          {PURPOSES.map((p) => (
            <button
              key={p}
              type="button"
              className={choice === p ? 'k-chip k-chip-on' : 'k-chip'}
              aria-pressed={choice === p}
              onClick={() => setChoice(p)}
            >
              {p}
            </button>
          ))}
        </div>

        {choice === OTHER && (
          <input
            autoFocus
            autoComplete="off"
            maxLength={PURPOSE_MAX}
            placeholder="Tell us what you'll be doing"
            aria-label="Other purpose"
            value={other}
            onChange={(e) => setOther(e.target.value)}
          />
        )}
      </div>

      {error && <p className="k-error" role="alert">{error}</p>}

      <button type="submit" className="k-btn k-btn-primary" disabled={!canSubmit}>
        {busy ? 'Logging in…' : 'Log in and start'}
      </button>

      <p className="k-fine">
        Your name and purpose are recorded to help the office manage computer use.
      </p>
    </form>
  )
}
