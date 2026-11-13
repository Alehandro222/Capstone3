import { useCallback, useEffect, useRef, useState } from 'react'
import { api, isUnreachable } from './api'
import { startPolling } from './polling.js'
import HelpRequests from './components/HelpRequests.jsx'
import WalkInDialog from './components/WalkInDialog.jsx'
import AuditLog from './dashboard/AuditLog.jsx'
import Board from './dashboard/Board.jsx'
import ComputerDetails from './dashboard/ComputerDetails.jsx'
import Reports from './dashboard/Reports.jsx'
import HelpDesk from './dashboard/HelpDesk.jsx'
import Problems from './dashboard/Problems.jsx'
import StaffAccounts from './dashboard/StaffAccounts.jsx'
import VisitorLog from './dashboard/VisitorLog.jsx'
import Waitlist from './dashboard/Waitlist.jsx'
import { ConfirmDialog, FormDialog } from './dashboard/ui.jsx'
import { heldForWaitlist, heldNames } from './dashboard/format.js'
import './dashboard/dashboard.css'
import './help.css'

const REFRESH_MS = 5000 // re-fetch every 5 seconds so the board stays current

const TABS = [
  { key: 'desk', label: 'Help desk' },
  { key: 'board', label: 'Computers' },
  { key: 'waitlist', label: 'Waitlist' },
  { key: 'problems', label: 'Problems' },
  { key: 'help', label: 'Help requests' },
  { key: 'visits', label: 'Visitor log' },
  { key: 'reports', label: 'Reports' },
  { key: 'staff', label: 'Staff accounts', admin: true },
  { key: 'audit', label: 'Audit log', admin: true },
]

// The staff dashboard: everything the help desk needs, refreshed every few seconds.
export default function App({ me, onSignOut }) {
  const isAdmin = me.role === 'Admin'
  const [tab, setTab] = useState(() => {
    const saved = sessionStorage.getItem('dashboard-tab')
    return TABS.some((t) => t.key === saved && (!t.admin || isAdmin)) ? saved : 'desk'
  })
  const [data, setData] = useState({
    computers: [], sessions: [], tickets: [], waitlist: [], problems: [], passes: [],
  })
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState('')
  const [refreshKey, setRefreshKey] = useState(0) // bumped after every action, for screens that load their own data
  const [dialog, setDialog] = useState(null)
  const [toast, setToast] = useState(null) // { text, bad }
  const toastTimer = useRef()

  useEffect(() => {
    try {
      sessionStorage.setItem('dashboard-tab', tab)
    } catch {
      /* storage blocked: the tab just isn't remembered */
    }
  }, [tab])

  const load = useCallback(async () => {
    try {
      // Everything the help desk shows, in one request (GET /api/board).
      const { computers, sessions, tickets, waitlist, problems, passes } = await api.getBoard()
      setData({ computers, sessions, tickets, waitlist, problems, passes })
      setError('')
      setLoaded(true)
    } catch (e) {
      if (e.status === 401) return onSignOut() // signed out elsewhere, deactivated, or the sign-in expired
      // Cleared by the next successful refresh, e.g. once a restarting API is back.
      setError(
        isUnreachable(e)
          ? 'Cannot reach the API. It may be restarting; this clears as soon as it answers.'
          : `The API returned an error: ${e.message}`,
      )
    }
  }, [onSignOut])

  // Paused while the dashboard's tab is hidden (see polling.js).
  useEffect(() => startPolling(load, REFRESH_MS), [load])

  function showToast(text, bad = false) {
    clearTimeout(toastTimer.current)
    setToast({ text, bad })
    toastTimer.current = setTimeout(() => setToast(null), bad ? 6000 : 3500)
  }

  // Runs an API action, refreshes everything, and shows the outcome. Returns true on success.
  const run = useCallback(
    async (action, successMessage) => {
      try {
        await action()
        await load()
        setRefreshKey((k) => k + 1)
        if (successMessage) showToast(successMessage)
        return true
      } catch (e) {
        if (e.status === 401) {
          onSignOut()
          return false
        }
        showToast(e.message, true)
        return false
      }
    },
    [load, onSignOut],
  )

  const { computers, sessions, tickets, waitlist, problems, passes } = data
  const count = (...statuses) => computers.filter((c) => statuses.includes(c.status)).length
  const openRequests = tickets.filter((t) => t.status === 'Open').length
  const badge = { waitlist: waitlist.length, problems: problems.length, help: openRequests }
  const close = () => setDialog(null)
  const detailsComputer = dialog?.type === 'details' ? computers.find((c) => c.id === dialog.computerId) : null

  useEffect(() => {
    document.title = openRequests > 0 ? `(${openRequests}) MISA Tracker` : 'MISA Tracker'
  }, [openRequests])

  // Actions on a board card.
  const boardActions = {
    register: (computer) => setDialog({ type: 'walkin', computer }),
    end: (computer, session) => setDialog({ type: 'end', computer, session }),
    extend: (computer, session) =>
      run(() => api.extendSession(session.id), `${computer.name}: 1 hour added`),
    details: (computer) => setDialog({ type: 'details', computerId: computer.id }),
    togglePeripheral: (p) => run(() => api.setPeripheralInUse(p.id, !p.isInUse)),
  }

  return (
    <div className="page">
      <a className="skip-link" href="#main">Skip to content</a>
      <header className="header">
        <div>
          <h1>MISA Computer System Tracker</h1>
          <p className="subtitle">DICT Digital Transformation Center, Baguio City</p>
          <p className="signed-in">
            Signed in as <strong>{me.fullName}</strong> ({me.role})
            <button className="link-button" onClick={() => setDialog({ type: 'password' })}>
              Change password
            </button>
            <a className="link-button" href="#/poster" target="_blank" rel="noreferrer">
              Entrance QR poster
            </a>
            <button className="link-button" onClick={onSignOut}>
              Sign out
            </button>
          </p>
        </div>
        <div className="summary">
          <Stat label="Available" value={count('Available')} tone="available" />
          <Stat label="In use" value={count('InUse')} tone="inuse" />
          <Stat label="Reserved" value={count('Reserved')} tone="reserved" />
          <Stat label="Unavailable" value={count('Maintenance', 'Offline')} tone="broken" />
        </div>
      </header>

      <nav className="nav" aria-label="Sections">
        {TABS.filter((t) => !t.admin || isAdmin).map((t) => (
          <button
            key={t.key}
            className={tab === t.key ? 'nav-tab nav-tab-on' : 'nav-tab'}
            aria-current={tab === t.key ? 'page' : undefined}
            onClick={() => setTab(t.key)}
          >
            {t.label}
            {badge[t.key] > 0 && <span className="nav-count">{badge[t.key]}</span>}
          </button>
        ))}
      </nav>

      <main id="main" tabIndex={-1}>
      {error && <div className="banner" role="alert">{error}</div>}
      {!loaded && !error && <p className="muted">Loading…</p>}

      {loaded && tab === 'desk' && (
        <HelpDesk
          computers={computers}
          waitlist={waitlist}
          pendingPasses={passes}
          run={run}
          onWalkIn={() => setDialog({ type: 'walkin', choose: true })}
          onLogVisit={() => setDialog({ type: 'walkin' })}
        />
      )}
      {loaded && tab === 'board' && (
        <>
          {isAdmin && (
            <div className="toolbar toolbar-end">
              <button className="btn btn-small" onClick={() => setDialog({ type: 'addPc' })}>
                Add a PC
              </button>
            </div>
          )}
          <Board computers={computers} sessions={sessions} pendingPasses={passes} waitlist={waitlist} on={boardActions} />
        </>
      )}
      {loaded && tab === 'waitlist' && <Waitlist waitlist={waitlist} computers={computers} />}
      {loaded && tab === 'problems' && (
        <Problems
          openProblems={problems}
          refreshKey={refreshKey}
          onReport={(computer) => setDialog({ type: 'report', computer })}
          onResolve={(problem) => setDialog({ type: 'resolve', problem })}
          onShowComputer={(computerId) => setDialog({ type: 'details', computerId })}
        />
      )}
      {loaded && tab === 'help' && (
        <HelpRequests
          tickets={tickets}
          sessions={sessions}
          onSetStatus={(ticket, status) => run(() => api.setAssistStatus(ticket.id, status))}
        />
      )}
      {tab === 'visits' && <VisitorLog refreshKey={refreshKey} />}
      {tab === 'reports' && <Reports isAdmin={isAdmin} refreshKey={refreshKey} />}
      {tab === 'staff' && isAdmin && <StaffAccounts me={me} run={run} />}
      {tab === 'audit' && isAdmin && <AuditLog />}

      </main>

      {dialog?.type === 'walkin' && (
        <WalkInDialog
          computer={dialog.computer}
          warning={heldWarning(computers, waitlist)}
          freeComputers={dialog.choose ? computers.filter((c) => c.status === 'Available') : undefined}
          onCancel={close}
          onSubmit={async (registration) => {
            const pc = computers.find((c) => c.id === registration.computerId)
            const ok = await run(
              () => api.walkIn(registration),
              pc ? `Session started on ${pc.name} for ${registration.firstName}` : `Visit logged for ${registration.firstName}`,
            )
            if (ok) close()
          }}
        />
      )}

      {dialog?.type === 'end' && (
        <ConfirmDialog
          title={`End the session on ${dialog.computer.name}?`}
          message={`${dialog.session.userName} will be logged out and the PC locks. It is recorded as ended by staff.`}
          confirmLabel="End session"
          onCancel={close}
          onConfirm={async () => {
            if (await run(() => api.endSession(dialog.session.id), `${dialog.computer.name} is free again`)) close()
          }}
        />
      )}

      {detailsComputer && (
        <ComputerDetails
          computer={detailsComputer}
          isAdmin={isAdmin}
          run={run}
          onReportProblem={(computer) => setDialog({ type: 'report', computer, back: dialog })}
          onResolveProblem={(problem) => setDialog({ type: 'resolve', problem, back: dialog })}
          onClose={close}
        />
      )}

      {dialog?.type === 'report' && (
        <ReportProblemDialog
          computer={dialog.computer}
          computers={computers}
          onCancel={() => setDialog(dialog.back ?? null)}
          onSave={async (computerId, description) => {
            const pc = computers.find((c) => c.id === computerId)
            if (await run(() => api.openProblem(computerId, description), `${pc?.name ?? 'PC'} is now under maintenance`))
              setDialog(dialog.back ?? null)
          }}
        />
      )}

      {dialog?.type === 'resolve' && (
        <ResolveProblemDialog
          problem={dialog.problem}
          onCancel={() => setDialog(dialog.back ?? null)}
          onSave={async (notes) => {
            if (await run(() => api.resolveProblem(dialog.problem.id, notes), `Problem on ${dialog.problem.computerName} resolved`))
              setDialog(dialog.back ?? null)
          }}
        />
      )}

      {dialog?.type === 'addPc' && (
        <AddComputerDialog
          onCancel={close}
          onSave={async (name) => {
            if (await run(() => api.addComputer(name), `${name} added. Open its Details to pair the kiosk app.`)) close()
          }}
        />
      )}

      {dialog?.type === 'password' && (
        <ChangePasswordDialog
          onCancel={close}
          onSave={async (current, next) => {
            if (await run(() => api.changePassword(current, next), 'Password changed')) close()
          }}
        />
      )}

      {toast && (
        <div className={toast.bad ? 'toast toast-bad' : 'toast'} role="status">
          {toast.text}
        </div>
      )}
    </div>
  )
}

// Shown in the walk-in form when every free PC is held for someone on the waitlist.
function heldWarning(computers, waitlist) {
  const free = computers.filter((c) => c.status === 'Available').length
  const held = heldForWaitlist(waitlist)
  if (held.length === 0 || held.length < free) return ''
  return `Every free PC is held for the waitlist: ${heldNames(held)}. Giving one to this visitor takes the turn ` +
    'of the last person offered one; they go back to waiting.'
}

function Stat({ label, value, tone }) {
  return (
    <div className={`stat stat-${tone}`}>
      <span className="stat-value">{value}</span>
      <span className="stat-label">{label}</span>
    </div>
  )
}

function ReportProblemDialog({ computer, computers, onSave, onCancel }) {
  const [computerId, setComputerId] = useState(computer?.id ?? computers[0]?.id ?? '')
  const [description, setDescription] = useState('')
  const pc = computers.find((c) => c.id === Number(computerId))

  return (
    <FormDialog
      title={computer ? `Report a problem on ${computer.name}` : 'Report a problem'}
      description="The PC shows as Maintenance (visitors can't pick it) until the report is resolved."
      submitLabel="Report problem"
      canSubmit={!!computerId && description.trim().length > 0}
      onCancel={onCancel}
      onSubmit={() => onSave(Number(computerId), description)}
    >
      {!computer && (
        <label className="field">
          <span>Computer</span>
          <select value={computerId} onChange={(e) => setComputerId(e.target.value)}>
            {computers.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
      )}
      <label className="field">
        <span>What is wrong?</span>
        <textarea rows={3} maxLength={500} value={description} autoFocus
          placeholder="e.g. No sound from the headset jack" onChange={(e) => setDescription(e.target.value)} />
      </label>
      {pc?.status === 'InUse' && (
        <p className="muted small">Someone is using {pc.name}. Their session keeps running; end it from the board if needed.</p>
      )}
    </FormDialog>
  )
}

function ResolveProblemDialog({ problem, onSave, onCancel }) {
  const [notes, setNotes] = useState('')
  return (
    <FormDialog title={`Resolve: ${problem.computerName}`} description={problem.description}
      submitLabel="Mark resolved" onCancel={onCancel} onSubmit={() => onSave(notes)}>
      <label className="field">
        <span>What was done? (optional)</span>
        <textarea rows={3} maxLength={500} value={notes} autoFocus onChange={(e) => setNotes(e.target.value)} />
      </label>
    </FormDialog>
  )
}

function AddComputerDialog({ onSave, onCancel }) {
  const [name, setName] = useState('')
  return (
    <FormDialog title="Add a PC" submitLabel="Add PC" canSubmit={name.trim().length > 0}
      onCancel={onCancel} onSubmit={() => onSave(name.trim())}>
      <label className="field">
        <span>Name</span>
        <input value={name} maxLength={50} placeholder="PC-13" autoFocus onChange={(e) => setName(e.target.value)} />
      </label>
    </FormDialog>
  )
}

function ChangePasswordDialog({ onSave, onCancel }) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [again, setAgain] = useState('')
  const mismatch = again.length > 0 && next !== again

  return (
    <FormDialog title="Change your password" submitLabel="Change password"
      canSubmit={current.length > 0 && next.length >= 8 && next === again}
      onCancel={onCancel} onSubmit={() => onSave(current, next)}>
      <label className="field">
        <span>Current password</span>
        <input type="password" value={current} autoComplete="current-password" autoFocus onChange={(e) => setCurrent(e.target.value)} />
      </label>
      <label className="field">
        <span>New password (at least 8 characters)</span>
        <input type="password" value={next} autoComplete="new-password" onChange={(e) => setNext(e.target.value)} />
      </label>
      <label className="field">
        <span>New password again</span>
        <input type="password" value={again} autoComplete="new-password" onChange={(e) => setAgain(e.target.value)} />
      </label>
      {mismatch && <p className="form-error">The new passwords don't match.</p>}
    </FormDialog>
  )
}
