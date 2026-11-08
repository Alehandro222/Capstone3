import { useRef, useState } from 'react'
import { api } from '../api'
import QrScanner from './QrScanner.jsx'
import {
  PASS_STATUS_LABEL,
  SOURCE_LABEL,
  ageLabel,
  ago,
  countdown,
  msUntil,
  sectorsLabel,
  servicesLabel,
  sexLabel,
  useNow,
} from './format.js'

// The QR holds the visit's token; the 6-digit code is the backup for when it won't scan.
// A USB/Bluetooth barcode scanner "types" the token and presses Enter, so the same box works.
const asPass = (text) => (/^\d{6}$/.test(text) ? { code: text } : { token: text })

// Help desk: check visitors in, register visitors without a phone, see who is waiting.
//   computers, waitlist, pendingPasses   live data from the dashboard
//   run(action, message)  runs an API action, refreshes the board, shows a toast; returns true on success
//   onWalkIn()            opens the walk-in registration form
export default function HelpDesk({ computers, waitlist, pendingPasses: pending, run, onWalkIn, onLogVisit }) {
  const now = useNow()
  const inputRef = useRef(null)
  const [text, setText] = useState('')
  const [scanning, setScanning] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [found, setFound] = useState(null) // { pass: StaffPassDto, query }

  async function lookUp(raw) {
    const value = raw.replace(/\s+/g, '')
    if (!value) return
    if (/^\d+$/.test(value) && value.length !== 6) {
      setFound(null)
      setError('The code has 6 digits. Check it with the visitor, or scan the QR.')
      inputRef.current?.select()
      return
    }
    setBusy(true)
    setError('')
    setFound(null)
    try {
      const query = asPass(value)
      setFound({ pass: await api.lookupPass(query), query })
    } catch (e) {
      setError(e.message)
      inputRef.current?.select() // the next scan or code replaces it
    } finally {
      setBusy(false)
    }
  }

  async function checkIn() {
    setBusy(true)
    const { pass, query } = found
    const ok = await run(
      () => api.checkIn(query),
      `${pass.visit.firstName} ${pass.visit.lastName} checked in. ${pass.computerName} is unlocking.`,
    )
    setBusy(false)
    if (ok) clear()
  }

  function clear() {
    setFound(null)
    setText('')
    setError('')
    inputRef.current?.focus()
  }

  const free = computers.filter((c) => c.status === 'Available').length
  const pass = found?.pass

  return (
    <div className="desk">
      <section className="panel desk-checkin">
        <h2>Check in a visitor</h2>
        <p className="muted small">
          Scan the QR pass on the visitor's phone, or type the 6-digit code under it.
        </p>
        <form
          className="checkin-form"
          onSubmit={(e) => {
            e.preventDefault()
            lookUp(text)
          }}
        >
          <input
            ref={inputRef}
            className="checkin-input"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder="6-digit code"
            aria-label="Pass code or QR token"
            inputMode="numeric"
            autoComplete="off"
            autoFocus
          />
          <button className="btn btn-primary" disabled={busy || !text.trim()}>
            Look up
          </button>
          <button type="button" className="btn" onClick={() => setScanning(true)}>
            Scan QR with camera
          </button>
        </form>

        {error && <p className="form-error" role="alert">{error}</p>}

        {pass && (
          <div className={`pass-card pass-${pass.status}`}>
            <div className="pass-head">
              <div>
                <p className="pass-name">
                  {pass.visit.firstName} {pass.visit.lastName}
                </p>
                <p className="muted small">
                  Registered {ago(pass.visit.createdAt, now)} · {SOURCE_LABEL[pass.visit.source] ?? pass.visit.source}
                </p>
              </div>
              <div className="pass-pc">
                <span className="muted small">Computer</span>
                <strong>{pass.computerName}</strong>
              </div>
            </div>

            <dl className="details">
              <dt>Age</dt>
              <dd>{ageLabel(pass.visit.ageGroup)}</dd>
              <dt>Sex</dt>
              <dd>{sexLabel(pass.visit.sex)}</dd>
              <dt>Sector</dt>
              <dd>{sectorsLabel(pass.visit.sectors)}</dd>
              <dt>Purpose</dt>
              <dd>{servicesLabel(pass.visit.services)}</dd>
              <dt>Pass</dt>
              <dd>
                {PASS_STATUS_LABEL[pass.status] ?? pass.status} · code {pass.code}
                {pass.status === 'Pending' && ` · ${countdown(msUntil(pass.expiresAt, now))} left`}
              </dd>
            </dl>

            <div className="actions">
              {pass.status === 'Pending' ? (
                <button className="btn btn-primary btn-big" disabled={busy} onClick={checkIn}>
                  Check in and start session on {pass.computerName}
                </button>
              ) : (
                <p className="form-error">
                  {pass.status === 'Used'
                    ? 'This pass was already used to start a session.'
                    : 'This pass can no longer be used. Ask the visitor to pick a PC again on their phone.'}
                </p>
              )}
              <button className="btn" onClick={clear}>
                Clear
              </button>
            </div>
          </div>
        )}

        {pending.length > 0 && (
          <div className="pending">
            <h3>Reserved, waiting to be scanned</h3>
            <ul className="rows">
              {pending.map((p) => (
                <li key={p.id} className="row">
                  <span>
                    <strong>{p.computerName}</strong> · {p.visit.firstName} {p.visit.lastName}
                  </span>
                  <span className="muted small">
                    code {p.code} · {countdown(msUntil(p.expiresAt, now))} left
                  </span>
                  <button
                    className="btn btn-small"
                    onClick={() => {
                      setText(p.code)
                      lookUp(p.code)
                    }}
                  >
                    Open
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}
      </section>

      <aside className="desk-side">
        <section className="panel">
          <h2>Visitor without a phone</h2>
          <p className="muted small">Fill in the DTC form for them here.</p>
          <div className="stack">
            <button className="btn btn-primary" onClick={onWalkIn} disabled={free === 0}>
              Register and give a PC
            </button>
            <button className="btn" onClick={onLogVisit}>
              Log a visit (no PC)
            </button>
          </div>
          <p className="muted small">{free === 0 ? 'No PC is free right now.' : `${free} PC${free === 1 ? '' : 's'} free.`}</p>
        </section>

        <section className="panel">
          <h2>Waitlist</h2>
          {waitlist.length === 0 ? (
            <p className="muted small">Nobody is waiting.</p>
          ) : (
            <ol className="rows">
              {waitlist.map((w) => (
                <li key={w.id} className="row">
                  <span>
                    {w.position ?? '–'}. {w.firstName}
                  </span>
                  <span className="muted small">
                    {w.computerReadyUntil
                      ? `PC held · ${countdown(msUntil(w.computerReadyUntil, now))}`
                      : `waiting ${ago(w.createdAt, now).replace(' ago', '')}`}
                  </span>
                </li>
              ))}
            </ol>
          )}
        </section>
      </aside>

      {scanning && (
        <QrScanner
          onCancel={() => setScanning(false)}
          onScan={(value) => {
            setScanning(false)
            setText('')
            lookUp(value)
          }}
        />
      )}
    </div>
  )
}
