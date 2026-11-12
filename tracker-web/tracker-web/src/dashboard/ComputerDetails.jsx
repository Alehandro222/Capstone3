import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { END_REASON_LABEL, STATUS_LABEL, STATUS_TONE, ago, clock, countdown, dateTime, minutesText, msUntil, useNow } from './format.js'
import { Badge, ConfirmDialog, Empty } from './ui.jsx'
import { parseUtc } from '../time.js'

// One PC: its problem reports, its session history and (admins) its kiosk pairing.
export default function ComputerDetails({ computer, isAdmin, run, onReportProblem, onResolveProblem, onClose }) {
  const now = useNow()
  const [sessions, setSessions] = useState(null)
  const [problems, setProblems] = useState(null)
  const [error, setError] = useState('')
  const [pairing, setPairing] = useState(null) // the one-time code, shown once
  const [confirmUnpair, setConfirmUnpair] = useState(false)

  const load = useCallback(async () => {
    try {
      const [s, p] = await Promise.all([
        api.getComputerSessions(computer.id, 50),
        api.getProblems({ computerId: computer.id }),
      ])
      setSessions(s)
      setProblems(p.sort((a, b) => parseUtc(b.createdAt) - parseUtc(a.createdAt)))
      setError('')
    } catch (e) {
      setError(e.message)
    }
  }, [computer.id])

  // Reload when the PC's status changes (e.g. a problem was resolved from here).
  useEffect(() => {
    load()
  }, [load, computer.status, computer.openProblem])

  // Once the PC pairs (or is unpaired), the code shown here is used up.
  useEffect(() => {
    setPairing(null)
  }, [computer.pairedAt])

  async function createCode() {
    try {
      setPairing(await api.createPairingCode(computer.id))
    } catch (e) {
      setError(e.message)
    }
  }

  return (
    <div className="overlay" onClick={onClose}>
      <div className="modal modal-wide details-modal" onClick={(e) => e.stopPropagation()} role="dialog" aria-label={computer.name}>
        <div className="card-head">
          <h2>{computer.name}</h2>
          <Badge tone={STATUS_TONE[computer.status]}>{STATUS_LABEL[computer.status]}</Badge>
        </div>
        <p className="muted small">
          {computer.lastHeartbeatAt ? `Kiosk app last seen ${ago(computer.lastHeartbeatAt, now)}` : 'The kiosk app has not reported in yet'}
        </p>

        {error && <p className="form-error">{error}</p>}

        <section>
          <div className="section-head">
            <h3>Problem reports</h3>
            <button className="btn btn-small" onClick={() => onReportProblem(computer)}>
              Report a problem
            </button>
          </div>
          {problems === null ? (
            <p className="muted small">Loading…</p>
          ) : problems.length === 0 ? (
            <Empty>No problems reported.</Empty>
          ) : (
            <ul className="rows">
              {problems.map((p) => (
                <li key={p.id} className="row row-top">
                  <div>
                    <p className="m0">{p.description}</p>
                    <p className="muted small">
                      Reported {dateTime(p.createdAt)}
                      {p.resolvedAt && ` · resolved ${dateTime(p.resolvedAt)}`}
                      {p.resolutionNotes && `: ${p.resolutionNotes}`}
                    </p>
                  </div>
                  {p.status === 'Open' ? (
                    <button className="btn btn-small btn-primary" onClick={() => onResolveProblem(p)}>
                      Resolve
                    </button>
                  ) : (
                    <Badge tone="available">Resolved</Badge>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>

        <section>
          <h3>Session history</h3>
          {sessions === null ? (
            <p className="muted small">Loading…</p>
          ) : sessions.length === 0 ? (
            <Empty>No sessions yet.</Empty>
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Visitor</th>
                    <th>Started</th>
                    <th>Length</th>
                    <th>Ended</th>
                  </tr>
                </thead>
                <tbody>
                  {sessions.map((s) => (
                    <tr key={s.id}>
                      <td>{s.userName}</td>
                      <td>{dateTime(s.startedAt)}</td>
                      <td>
                        {minutesText(((s.endedAt ? parseUtc(s.endedAt).getTime() : now) - parseUtc(s.startedAt).getTime()) / 60000)}
                        {s.extensionCount > 0 && ` (+${s.extensionCount})`}
                      </td>
                      <td>
                        {s.endedAt
                          ? `${clock(s.endedAt)} · ${END_REASON_LABEL[s.endReason] ?? s.endReason ?? ''}`
                          : <Badge tone="inuse">Running</Badge>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        {isAdmin && (
          <section>
            <h3>Kiosk app on this PC</h3>
            <p className="m0">
              {computer.pairedAt ? `Paired ${dateTime(computer.pairedAt)}.` : 'Not paired: its kiosk app cannot sign in.'}
            </p>
            <p className="muted small">
              The kiosk app needs a one-time pairing code to get this PC's own key. Pairing again, or
              unpairing, stops the old key at once (do this if you think a key was copied).
            </p>
            {pairing && (
              <div className="pairing">
                <span className="pairing-code">{pairing.code}</span>
                <span className="muted small">
                  Type it into the kiosk app's "Set up this computer" screen on {pairing.computerName}.
                  Works once, {msUntil(pairing.expiresAt, now) > 0 ? `${countdown(msUntil(pairing.expiresAt, now))} left` : 'expired'}.
                </span>
              </div>
            )}
            <div className="actions">
              <button className="btn" onClick={createCode}>
                {pairing ? 'New pairing code' : 'Create pairing code'}
              </button>
              <button className="btn btn-danger-quiet" onClick={() => setConfirmUnpair(true)}>
                Unpair
              </button>
            </div>
          </section>
        )}

        <div className="modal-actions">
          <button className="btn" onClick={onClose}>
            Close
          </button>
        </div>
      </div>

      {confirmUnpair && (
        <div onClick={(e) => e.stopPropagation()}>
          <ConfirmDialog
            title={`Unpair ${computer.name}?`}
            message="Its kiosk app stops working until it is paired again with a new code. A running session is not ended."
            confirmLabel="Unpair"
            onCancel={() => setConfirmUnpair(false)}
            onConfirm={async () => {
              if (await run(() => api.unpairComputer(computer.id), `${computer.name} unpaired`)) {
                setPairing(null)
                setConfirmUnpair(false)
              }
            }}
          />
        </div>
      )}
    </div>
  )
}
