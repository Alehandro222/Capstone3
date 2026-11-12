import { useCallback, useEffect, useState } from 'react'
import { api } from '../api.js'
import { parseUtc } from '../time.js'
import ComputersScreen from './ComputersScreen.jsx'
import RegisterScreen from './RegisterScreen.jsx'
import {
  EndedScreen,
  MessageScreen,
  PassScreen,
  SessionScreen,
  VisitLoggedScreen,
  WaitlistScreen,
  WelcomeScreen,
} from './VisitScreens.jsx'
import { formatDuration, msUntil, useNow, usePolling } from './useClock.js'
import './visitor.css'

// The phone keeps its visit token here, so a reload (or closing the browser)
// does not lose the visit. It is cleared when the visit is done.
const TOKEN_KEY = 'misa.visitToken'

function readToken() {
  try {
    return localStorage.getItem(TOKEN_KEY)
  } catch {
    return null // private browsing: the visit just lasts as long as the page
  }
}

function saveToken(token) {
  try {
    if (token) localStorage.setItem(TOKEN_KEY, token)
    else localStorage.removeItem(TOKEN_KEY)
  } catch {
    /* not saved; still works until the page is closed */
  }
}

// The PC a kiosk's lock-screen QR code points to: #/visit?pc=4 -> 4 (else null).
function pcFromAddress() {
  const query = window.location.hash.split('?')[1] ?? ''
  const id = Number(new URLSearchParams(query).get('pc'))
  return Number.isInteger(id) && id > 0 ? id : null
}

// The visitor phone app: http://<server>:5173/#/visit (the entrance QR code).
// A PC's own lock screen links to #/visit?pc=4, so that PC is already chosen.
//
// What the phone shows comes from the visit itself (GET /api/visits/{token},
// checked every few seconds), so it always matches what staff and the PC see:
//   running session -> time left;  reserved PC -> QR pass;  waiting -> place in line.
export default function VisitorApp() {
  const [token, setToken] = useState(readToken)
  const [visit, setVisit] = useState(null)
  const [wantedPc] = useState(pcFromAddress) // scanned at a PC: preselect it
  // Already registered and scanned a PC's QR? Go straight to choosing (that PC is preselected).
  const [page, setPage] = useState(() => (wantedPc && readToken() ? 'computers' : 'home')) // home | register | computers | browse
  const [seen, setSeen] = useState({})     // messages already dismissed (ended session, expired pass, missed turn)
  const [offline, setOffline] = useState(false)
  const [loading, setLoading] = useState(Boolean(readToken()))
  const [counts, setCounts] = useState({ free: 0, total: 0 })
  const now = useNow()

  const forget = useCallback(() => {
    saveToken(null)
    setToken(null)
    setVisit(null)
    setSeen({})
    setPage('home')
  }, [])

  // ---- Keep the visit up to date ----
  const loadVisit = useCallback(async () => {
    if (!token) return
    try {
      setVisit(await api.getVisit(token))
      setOffline(false)
    } catch (e) {
      if (e.status === 404) forget() // the registration no longer exists
      else setOffline(true)
    } finally {
      setLoading(false)
    }
  }, [token, forget])
  usePolling(loadVisit, 3000)

  // Free computers, for the welcome screen.
  const loadCounts = useCallback(async () => {
    if (token) return
    try {
      const list = await api.publicComputers()
      setCounts({ free: list.filter((c) => c.status === 'Available').length, total: list.length })
      setOffline(false)
    } catch {
      setOffline(true)
    }
  }, [token])
  usePolling(loadCounts, 10000)

  useEffect(() => {
    document.title = 'MISA · DTC Baguio digital login'
  }, [])

  // ---- Actions ----
  async function register(form) {
    const created = await api.register(form)
    saveToken(created.token)
    setToken(created.token)
    setVisit(created.visit)
    // Ticked "Use of ICT Equipment", or came from a PC's QR code? Go straight to choosing a PC.
    setPage(wantedPc || form.services.includes('IctEquipment') ? 'computers' : 'home')
  }

  const update = (action) => async (...args) => {
    setVisit(await action(token, ...args))
    setPage('home')
  }
  const pickComputer = update(api.pickComputer)
  const cancelPass = update(api.cancelPass)
  const endSession = update(api.endMySession)
  const extendSession = update(api.extendMySession)
  const joinWaitlist = update(api.joinWaitlist)
  const leaveWaitlist = update(api.leaveWaitlist)

  // ---- Which screen ----
  let screen
  if (loading) {
    screen = <MessageScreen ok eyebrow="MISA" title="Loading…" text="" action="Start over" onAction={forget} />
  } else if (!token || !visit) {
    screen =
      page === 'register' ? (
        <RegisterScreen onBack={() => setPage('home')} onSubmit={register} />
      ) : page === 'browse' ? (
        <ComputersScreen onBack={() => setPage('home')} />
      ) : (
        <WelcomeScreen {...counts} onStart={() => setPage('register')} onBrowse={() => setPage('browse')} />
      )
  } else {
    screen = visitScreen()
  }

  function visitScreen() {
    const { session, pass, waitlist } = visit
    const expired = parseUtc(visit.expiresAt).getTime() <= now

    if (session && !session.endedAt)
      return <SessionScreen visit={visit} onExtend={extendSession} onEnd={endSession} />

    if (pass?.status === 'Pending' && msUntil(pass.expiresAt, now) > 0)
      return (
        <PassScreen token={token} visit={visit} onChooseAgain={() => setPage('computers')} onCancel={cancelPass} />
      )

    const readyLeft = msUntil(waitlist?.computerReadyUntil, now)
    if (waitlist?.status === 'Waiting') {
      if (page === 'computers' && readyLeft > 0)
        return (
          <ComputersScreen
            onBack={() => setPage('home')}
            onPick={pickComputer}
            preferredId={wantedPc}
            readyUntilText={`A computer is held for you for ${formatDuration(readyLeft)}.`}
          />
        )
      return <WaitlistScreen visit={visit} onChooseComputer={() => setPage('computers')} onLeave={leaveWaitlist} />
    }

    if (session?.endedAt && seen.session !== session.id)
      return <EndedScreen visit={visit} onDone={forget} />

    if (pass?.status === 'Expired' && seen.pass !== pass.createdAt && page !== 'computers')
      return (
        <MessageScreen
          eyebrow="Pass expired"
          title="Your pass was not scanned in time"
          text={`${pass.computerName} has been given back. You can pick a computer again.`}
          action="Pick a computer"
          onAction={() => { setSeen((s) => ({ ...s, pass: pass.createdAt })); setPage('computers') }}
          secondary="Done"
          onSecondary={forget}
        />
      )

    if (waitlist?.status === 'Missed' && seen.missed !== waitlist.computerReadyUntil && page !== 'computers')
      return (
        <MessageScreen
          eyebrow="Waitlist"
          title="You missed your turn"
          text="A computer was held for you, but it was not picked in time, so it went to the next person."
          action="Join the waitlist again"
          onAction={async () => { setSeen((s) => ({ ...s, missed: waitlist.computerReadyUntil })); await joinWaitlist() }}
          secondary="Pick a computer"
          onSecondary={() => { setSeen((s) => ({ ...s, missed: waitlist.computerReadyUntil })); setPage('computers') }}
        />
      )

    if (page === 'computers' && !expired)
      return (
        <ComputersScreen
          onBack={() => setPage('home')}
          onPick={pickComputer}
          onJoinWaitlist={joinWaitlist}
          preferredId={wantedPc}
        />
      )

    return (
      <VisitLoggedScreen
        visit={visit}
        expired={expired}
        onChooseComputer={() => setPage('computers')}
        onDone={forget}
        onRegisterAgain={() => { forget(); setPage('register') }}
      />
    )
  }

  return (
    <div className="v-app">
      {offline && (
        <p className="v-alert v-alert--error" style={{ position: 'fixed', top: 8, left: 8, right: 8, zIndex: 10 }}>
          Cannot reach the library system. Trying again…
        </p>
      )}
      {screen}
    </div>
  )
}
