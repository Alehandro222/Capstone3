import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { formatElapsed, parseUtc } from './utils.js'

export default function KioskSession({ computer, session, onLoggedOut }) {
  const [now, setNow] = useState(Date.now())
  const [mode, setMode] = useState('main') // main | help | confirm
  const [helpText, setHelpText] = useState('')
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  // Tick every second so the timer moves.
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [])

  // Hide the "help is on the way" message after a while.
  useEffect(() => {
    if (!notice) return
    const timer = setTimeout(() => setNotice(''), 8000)
    return () => clearTimeout(timer)
  }, [notice])

  const elapsed = formatElapsed(now - parseUtc(session.startedAt).getTime())

  // Runs an API call with shared busy / error handling.
  async function run(action) {
    setBusy(true)
    setError('')
    try {
      await action()
      return true
    } catch (err) {
      setError(
        err instanceof TypeError
          ? 'Cannot reach the tracker. Please ask a staff member for help.'
          : err.message,
      )
      return false
    } finally {
      setBusy(false)
    }
  }

  async function sendHelp(e) {
    e.preventDefault()
    const ok = await run(() => api.createAssist(computer.id, helpText.trim()))
    if (ok) {
      setHelpText('')
      setMode('main')
      setNotice('Help is on the way. A staff member has been notified.')
    }
  }

  async function logOut() {
    const ok = await run(() => api.endSession(session.id))
    if (ok) onLoggedOut(session.userName)
  }

  return (
    <div className="k-card">
      <p className="k-eyebrow">Logged in</p>
      <h1>Hi, {session.userName}</h1>
      <p className="k-lead">{session.purpose}</p>

      <div className="k-timer" aria-label="Time on this computer">
        <span className="k-timer-value">{elapsed}</span>
        <span className="k-timer-label">time on this computer</span>
      </div>

      {computer.peripherals.length > 0 && (
        <div className="k-devices">
          <h2>Connected devices</h2>
          <ul>
            {computer.peripherals.map((p) => (
              <li key={p.id}>
                {p.name}
                {p.isInUse && <span className="k-live">in use</span>}
              </li>
            ))}
          </ul>
        </div>
      )}

      {notice && <p className="k-notice" role="status">{notice}</p>}
      {error && <p className="k-error" role="alert">{error}</p>}

      {mode === 'main' && (
        <div className="k-row">
          <button className="k-btn" onClick={() => setMode('help')}>
            Need help?
          </button>
          <button className="k-btn k-btn-danger" onClick={() => setMode('confirm')}>
            Log out
          </button>
        </div>
      )}

      {mode === 'help' && (
        <form className="k-panel" onSubmit={sendHelp}>
          <label className="k-field">
            <span>How can we help?</span>
            <textarea
              autoFocus
              rows={3}
              maxLength={500}
              placeholder="Describe your problem or question"
              value={helpText}
              onChange={(e) => setHelpText(e.target.value)}
            />
          </label>
          <div className="k-row">
            <button type="button" className="k-btn" onClick={() => setMode('main')} disabled={busy}>
              Cancel
            </button>
            <button type="submit" className="k-btn k-btn-primary" disabled={busy || !helpText.trim()}>
              {busy ? 'Sending…' : 'Send request'}
            </button>
          </div>
        </form>
      )}

      {mode === 'confirm' && (
        <div className="k-panel">
          <p className="k-confirm">Log out now? Please save your work and sign out of your accounts first.</p>
          <div className="k-row">
            <button className="k-btn" onClick={() => setMode('main')} disabled={busy}>
              Stay logged in
            </button>
            <button className="k-btn k-btn-danger" onClick={logOut} disabled={busy}>
              {busy ? 'Logging out…' : 'Yes, log out'}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
