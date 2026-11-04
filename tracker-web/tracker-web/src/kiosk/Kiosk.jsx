import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api.js'
import KioskLogin from './KioskLogin.jsx'
import KioskSession from './KioskSession.jsx'
import KioskSetup from './KioskSetup.jsx'
import { STORAGE_KEY } from './utils.js'
import './kiosk.css'

const REFRESH_MS = 5000 // re-check the server so staff changes show up here
const FAREWELL_MS = 5000 // how long the "thank you" screen stays

// Works out which computer this screen is for:
//   #/kiosk/2      -> computer 2 (and remember it on this machine)
//   #/kiosk        -> the computer remembered on this machine, if any
//   #/kiosk/setup  -> always show the picker
function resolveComputerId(hash) {
  const part = hash.replace(/^#\/kiosk\/?/, '')
  if (part === 'setup') return null

  const fromUrl = Number.parseInt(part, 10)
  if (Number.isInteger(fromUrl) && fromUrl > 0) {
    try {
      localStorage.setItem(STORAGE_KEY, String(fromUrl))
    } catch {
      /* storage blocked: the URL still works */
    }
    return fromUrl
  }

  try {
    const saved = Number.parseInt(localStorage.getItem(STORAGE_KEY) ?? '', 10)
    return Number.isInteger(saved) && saved > 0 ? saved : null
  } catch {
    return null
  }
}

export default function Kiosk({ hash }) {
  const computerId = resolveComputerId(hash)
  if (computerId === null) return <KioskSetup />
  // "key" resets all state if the computer id changes.
  return <KioskScreen key={computerId} computerId={computerId} />
}

function KioskScreen({ computerId }) {
  const [computer, setComputer] = useState(null)
  const [session, setSession] = useState(null)
  const [phase, setPhase] = useState('loading') // loading | ready | missing
  const [offline, setOffline] = useState(false)
  const [farewell, setFarewell] = useState('') // name of the user who just logged out
  const farewellTimer = useRef(null)

  const load = useCallback(async () => {
    try {
      const [c, sessions] = await Promise.all([api.getComputer(computerId), api.getActiveSessions()])
      setComputer(c)
      setSession(sessions.find((s) => s.computerId === computerId) ?? null)
      setPhase('ready')
      setOffline(false)
    } catch (e) {
      if (e.status === 404) setPhase('missing')
      else setOffline(true) // keep showing the last known screen
    }
  }, [computerId])

  useEffect(() => {
    load()
    const timer = setInterval(load, REFRESH_MS)
    return () => clearInterval(timer)
  }, [load])

  useEffect(() => () => clearTimeout(farewellTimer.current), [])

  function handleLoggedOut(name) {
    setFarewell(name)
    clearTimeout(farewellTimer.current)
    farewellTimer.current = setTimeout(() => setFarewell(''), FAREWELL_MS)
    load()
  }

  let body
  if (phase === 'loading') {
    body = <Message title="Loading…" />
  } else if (phase === 'missing') {
    body = (
      <Message
        title="This computer isn't set up"
        text="Please ask a staff member for help."
      />
    )
  } else if (farewell && computer.status !== 'InUse') {
    body = (
      <Message
        tone="ok"
        title={`Thank you, ${farewell}!`}
        text="You have been logged out. Have a great day."
      />
    )
  } else if (computer.status === 'NotWorking') {
    body = (
      <Message
        tone="broken"
        title="Out of service"
        text="This computer isn't working. Please use another one or ask a staff member."
      />
    )
  } else if (computer.status === 'InUse' && session) {
    body = <KioskSession computer={computer} session={session} onLoggedOut={handleLoggedOut} />
  } else if (computer.status === 'InUse') {
    body = <Message title="Computer in use" text="Please ask a staff member for help." />
  } else {
    body = <KioskLogin computer={computer} onStarted={load} />
  }

  return (
    <div className="k-screen">
      <header className="k-top">
        <span className="k-pc">{computer?.name ?? `Computer ${computerId}`}</span>
        <span className="k-title">Public Computer Station</span>
      </header>

      {offline && <div className="k-offline">Connection lost. Trying to reconnect…</div>}

      <main className="k-main">{body}</main>
    </div>
  )
}

function Message({ title, text, tone }) {
  return (
    <div className={`k-card k-message ${tone ? `k-message-${tone}` : ''}`}>
      <h1>{title}</h1>
      {text && <p>{text}</p>}
    </div>
  )
}
