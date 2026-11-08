import { useState } from 'react'
import { parseUtc } from '../time.js'
import '../help.css'

const FILTERS = ['Active', 'Resolved', 'All']
const STATUS_LABEL = { Open: 'Open', InProgress: 'In progress', Resolved: 'Resolved' }
const MAX_RESOLVED_SHOWN = 50

// "5 min ago" style text from the ISO time the API sends (UTC, maybe without the trailing Z).
function ago(iso) {
  const minutes = Math.max(0, Math.floor((Date.now() - parseUtc(iso).getTime()) / 60000))
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} h ${minutes % 60} min ago`
  return `${Math.floor(hours / 24)} d ago`
}

const byOldest = (a, b) => parseUtc(a.createdAt) - parseUtc(b.createdAt)
const byNewest = (a, b) => parseUtc(b.createdAt) - parseUtc(a.createdAt)

// Staff screen: everyone who asked for help, and what is being done about it.
//   tickets      help requests from the API
//   sessions     active sessions, to show who is at the computer that asked
//   onSetStatus  (ticket, status) => Promise; changes a request's status
export default function HelpRequests({ tickets, sessions, onSetStatus }) {
  const [filter, setFilter] = useState('Active')
  const [busyId, setBusyId] = useState(null)

  // Waiting requests: the one waiting longest comes first.
  const active = tickets.filter((t) => t.status !== 'Resolved').sort(byOldest)
  // Finished requests: newest first, and only the latest few.
  const resolved = tickets
    .filter((t) => t.status === 'Resolved')
    .sort(byNewest)
    .slice(0, MAX_RESOLVED_SHOWN)

  const visible =
    filter === 'Active' ? active : filter === 'Resolved' ? resolved : [...active, ...resolved]

  async function change(ticket, status) {
    setBusyId(ticket.id)
    await onSetStatus(ticket, status)
    setBusyId(null)
  }

  return (
    <section>
      <div className="help-bar">
        <h2>Help requests</h2>
        <div className="help-filters" role="group" aria-label="Filter requests">
          {FILTERS.map((f) => (
            <button
              key={f}
              className={filter === f ? 'nav-tab nav-tab-on' : 'nav-tab'}
              aria-pressed={filter === f}
              onClick={() => setFilter(f)}
            >
              {f}
              {f === 'Active' && ` (${active.length})`}
            </button>
          ))}
        </div>
      </div>

      {visible.length === 0 && (
        <p className="muted">
          {filter === 'Active' ? 'No open help requests right now.' : 'Nothing to show.'}
        </p>
      )}

      <ul className="help-list">
        {visible.map((t) => {
          // Who is using that computer right now (only useful while the request is still open).
          const user = t.status === 'Resolved' ? null : sessions.find((s) => s.computerId === t.computerId)
          const busy = busyId === t.id

          return (
            <li key={t.id} className={`help-card help-${t.status}`}>
              <div className="help-head">
                <strong>{t.computerName}</strong>
                <span className={`help-pill help-pill-${t.status}`}>{STATUS_LABEL[t.status] ?? t.status}</span>
              </div>

              {user && (
                <p className="muted small">
                  At the computer: {user.userName} ({user.purpose})
                </p>
              )}

              <p className="help-message">{t.message}</p>

              <p className="muted small" title={parseUtc(t.createdAt).toLocaleString()}>
                Requested {ago(t.createdAt)}
                {t.resolvedAt && ` · resolved ${ago(t.resolvedAt)}`}
              </p>

              <div className="actions">
                {t.status === 'Open' && (
                  <button className="btn" disabled={busy} onClick={() => change(t, 'InProgress')}>
                    Take it
                  </button>
                )}
                {t.status !== 'Resolved' && (
                  <button className="btn btn-primary" disabled={busy} onClick={() => change(t, 'Resolved')}>
                    Mark resolved
                  </button>
                )}
                {t.status === 'Resolved' && (
                  <button className="btn btn-quiet" disabled={busy} onClick={() => change(t, 'Open')}>
                    Reopen
                  </button>
                )}
              </div>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
