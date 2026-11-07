import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import Login from './Login.jsx'

// Shows the sign-in screen until a staff member is signed in, then renders
// children(me, signOut). "me" is the signed-in staff member from the API.
export default function StaffGate({ children }) {
  const [me, setMe] = useState(null)
  const [checking, setChecking] = useState(true)
  const [error, setError] = useState('')

  // Already signed in? (The cookie survives page reloads.)
  useEffect(() => {
    api
      .me()
      .then(setMe)
      .catch((e) => {
        if (e.status !== 401) setError('Cannot reach the API. Is it running?')
      })
      .finally(() => setChecking(false))
  }, [])

  // Stable across renders, so the dashboard's refresh loop does not restart.
  const signOut = useCallback(async () => {
    try {
      await api.logout()
    } catch {
      /* already signed out on the server */
    }
    setMe(null)
  }, [])

  if (checking) return <p className="auth-checking">Loading…</p>
  if (!me) return <Login onSignedIn={setMe} startupError={error} />
  return children(me, signOut)
}
