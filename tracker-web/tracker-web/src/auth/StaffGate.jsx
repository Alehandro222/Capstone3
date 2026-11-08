import { useCallback, useEffect, useState } from 'react'
import { api, isUnreachable } from '../api'
import Login from './Login.jsx'

const RETRY_MS = 3000 // while the API is down or starting, ask again this often

// Shows the sign-in screen until a staff member is signed in, then renders
// children(me, signOut). "me" is the signed-in staff member from the API.
export default function StaffGate({ children }) {
  const [me, setMe] = useState(null)
  const [checking, setChecking] = useState(true)
  const [apiDown, setApiDown] = useState(false)
  const [attempt, setAttempt] = useState(0) // bumped to start checking again

  // Already signed in? (The cookie survives page reloads.) If the API cannot be
  // reached yet (often it is still starting), keep asking until it answers, so the
  // "Cannot reach the API" message goes away by itself.
  useEffect(() => {
    if (me) return undefined
    let stopped = false
    let timer

    async function check() {
      try {
        const who = await api.me()
        if (stopped) return
        setApiDown(false)
        setMe(who)
      } catch (e) {
        if (stopped) return
        const down = isUnreachable(e) || e.status >= 500
        setApiDown(down)
        if (down) timer = setTimeout(check, RETRY_MS)
      } finally {
        if (!stopped) setChecking(false)
      }
    }

    check()
    return () => {
      stopped = true
      clearTimeout(timer)
    }
  }, [me, attempt])

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
  if (!me) {
    return (
      <Login
        onSignedIn={setMe}
        apiDown={apiDown}
        onUnreachable={() => {
          setApiDown(true)
          setAttempt((n) => n + 1)
        }}
      />
    )
  }
  return children(me, signOut)
}
