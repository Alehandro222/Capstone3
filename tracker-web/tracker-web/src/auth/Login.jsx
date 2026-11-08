import { useState } from 'react'
import { api, isUnreachable } from '../api'
import './auth.css'

// apiDown: the API cannot be reached right now. StaffGate keeps checking and clears
// it as soon as the API answers, so the message never outlives the problem.
export default function Login({ onSignedIn, apiDown, onUnreachable }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function submit(event) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      onSignedIn(await api.login(username, password))
    } catch (e) {
      if (isUnreachable(e)) {
        onUnreachable() // shown as apiDown until the API answers again
      } else {
        setError(e.message)
        setPassword('')
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="auth-page">
      <form className="auth-card" onSubmit={submit}>
        <div>
          <h1>Staff sign-in</h1>
          <p className="muted">Computer System Tracker</p>
        </div>

        {apiDown && (
          <p className="auth-error" role="alert">
            Cannot reach the API. It may still be starting; this message goes away when it answers.
          </p>
        )}
        {error && !apiDown && (
          <p className="auth-error" role="alert">
            {error}
          </p>
        )}

        <label className="field">
          <span>Username</span>
          <input
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            autoComplete="username"
            autoFocus
            required
          />
        </label>
        <label className="field">
          <span>Password</span>
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            required
          />
        </label>

        <button className="btn btn-primary auth-submit" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>

        <p className="muted small">Forgot your password? Ask an admin to set a new one.</p>
      </form>
    </main>
  )
}
