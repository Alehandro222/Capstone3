import { useState } from 'react'
import { api } from '../api'
import './auth.css'

export default function Login({ onSignedIn, startupError }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(startupError ?? '')

  async function submit(event) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      onSignedIn(await api.login(username, password))
    } catch (e) {
      setError(e instanceof TypeError ? 'Cannot reach the API. Is it running?' : e.message)
      setPassword('')
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

        {error && (
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
