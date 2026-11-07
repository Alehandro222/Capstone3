import { useCallback, useEffect, useState } from 'react'
import { api } from './api'
import ComputerCard from './components/ComputerCard.jsx'
import HelpRequests from './components/HelpRequests.jsx'
import Modal from './components/Modal.jsx'
import './help.css'

const REFRESH_MS = 5000 // re-fetch every 5 seconds so the board stays current

export default function App({ me, onSignOut }) {
  const [computers, setComputers] = useState([])
  const [sessions, setSessions] = useState([])
  const [tickets, setTickets] = useState([]) // help requests
  const [view, setView] = useState('computers') // 'computers' | 'help'
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  // Which dialog is open: { type: 'start' | 'assist', computer } or null
  const [dialog, setDialog] = useState(null)
  const [toast, setToast] = useState('')

  const load = useCallback(async () => {
    try {
      // Run all three requests at the same time.
      const [c, s, a] = await Promise.all([
        api.getComputers(),
        api.getActiveSessions(),
        api.getAssist(),
      ])
      setComputers(c)
      setSessions(s)
      setTickets(a)
      setError('')
    } catch (e) {
      if (e.status === 401) return onSignOut() // signed out elsewhere, or the sign-in expired
      // A TypeError means the request never reached the API; anything else is an error it returned.
      setError(
        e instanceof TypeError
          ? 'Cannot reach the API. Is it running?'
          : `The API returned an error: ${e.message}`,
      )
    } finally {
      setLoading(false)
    }
  }, [onSignOut])

  useEffect(() => {
    load()
    const timer = setInterval(load, REFRESH_MS)
    return () => clearInterval(timer) // cleanup when the component unmounts
  }, [load])

  function showToast(message) {
    setToast(message)
    setTimeout(() => setToast(''), 3000)
  }

  // Wraps an API action: run it, refresh the board, show any error as a toast.
  async function run(action, successMessage) {
    try {
      await action()
      await load()
      if (successMessage) showToast(successMessage)
      return true
    } catch (e) {
      showToast(e.message)
      return false
    }
  }

  const count = (...statuses) => computers.filter((c) => statuses.includes(c.status)).length

  // Requests nobody has picked up yet: shown as a badge, and in the browser tab title.
  const openRequests = tickets.filter((t) => t.status === 'Open').length

  useEffect(() => {
    document.title = openRequests > 0
      ? `(${openRequests}) Computer System Tracker`
      : 'Computer System Tracker'
  }, [openRequests])

  return (
    <div className="page">
      <header className="header">
        <div>
          <h1>Computer System Tracker</h1>
          <p className="subtitle">Open workspace office: live computer status</p>
          <p className="signed-in">
            Signed in as <strong>{me.fullName}</strong> ({me.role})
            <button className="link-button" onClick={onSignOut}>Sign out</button>
          </p>
        </div>
        <div className="summary">
          <Stat label="Available" value={count('Available')} tone="available" />
          <Stat label="In use" value={count('InUse', 'Reserved')} tone="inuse" />
          <Stat label="Unavailable" value={count('Maintenance', 'Offline')} tone="broken" />
        </div>
      </header>

      <nav className="nav" aria-label="Sections">
        <button
          className={view === 'computers' ? 'nav-tab nav-tab-on' : 'nav-tab'}
          onClick={() => setView('computers')}
        >
          Computers
        </button>
        <button
          className={view === 'help' ? 'nav-tab nav-tab-on' : 'nav-tab'}
          onClick={() => setView('help')}
        >
          Help requests
          {openRequests > 0 && <span className="nav-count">{openRequests}</span>}
        </button>
      </nav>

      {error && <div className="banner">{error}</div>}
      {loading && !error && <p className="muted">Loading…</p>}

      {view === 'help' && (
        <HelpRequests
          tickets={tickets}
          sessions={sessions}
          onSetStatus={(ticket, status) => run(() => api.setAssistStatus(ticket.id, status))}
        />
      )}

      {view === 'computers' && <main className="grid">
        {computers.map((computer) => (
          <ComputerCard
            key={computer.id}
            computer={computer}
            session={sessions.find((s) => s.computerId === computer.id)}
            onStart={() => setDialog({ type: 'start', computer })}
            onAssist={() => setDialog({ type: 'assist', computer })}
            onEnd={(session) => run(() => api.endSession(session.id), `${computer.name} is now available`)}
            onTogglePeripheral={(p) =>
              run(() => api.setPeripheralInUse(p.id, !p.isInUse))
            }
            onSetStatus={(status) =>
              run(() => api.setComputerStatus(computer.id, status), `${computer.name} marked ${status === 'Maintenance' ? 'out of service' : 'available'}`)
            }
          />
        ))}
      </main>}

      {dialog?.type === 'start' && (
        <Modal
          title={`Start session on ${dialog.computer.name}`}
          fields={[
            { name: 'userName', label: 'Who is using the computer?', placeholder: 'Full name' },
            { name: 'purpose', label: 'What will you use it for?', placeholder: 'e.g. Job application, school work' },
          ]}
          submitLabel="Start session"
          onCancel={() => setDialog(null)}
          onSubmit={async (v) => {
            const ok = await run(
              () => api.startSession(dialog.computer.id, v.userName, v.purpose),
              `Session started on ${dialog.computer.name}`,
            )
            if (ok) setDialog(null)
          }}
        />
      )}

      {dialog?.type === 'assist' && (
        <Modal
          title={`Ask for help at ${dialog.computer.name}`}
          fields={[
            { name: 'message', label: 'How can we help?', placeholder: 'Describe your problem or question', multiline: true },
          ]}
          submitLabel="Send request"
          onCancel={() => setDialog(null)}
          onSubmit={async (v) => {
            const ok = await run(
              () => api.createAssist(dialog.computer.id, v.message),
              'Help is on the way. Staff have been notified.',
            )
            if (ok) setDialog(null)
          }}
        />
      )}

      {toast && <div className="toast">{toast}</div>}
    </div>
  )
}

function Stat({ label, value, tone }) {
  return (
    <div className={`stat stat-${tone}`}>
      <span className="stat-value">{value}</span>
      <span className="stat-label">{label}</span>
    </div>
  )
}
