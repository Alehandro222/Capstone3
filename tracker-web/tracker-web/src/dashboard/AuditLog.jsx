import { useEffect, useState } from 'react'
import { api } from '../api'
import { dateTime, localDateInput, startOfDayIso } from './format.js'
import { Empty } from './ui.jsx'

// The action filter matches the start of the action name, so "pass." finds every pass action.
const ACTIONS = [
  { value: '', label: 'All actions' },
  { value: 'auth.', label: 'Sign-ins and passwords' },
  { value: 'staff.', label: 'Staff accounts' },
  { value: 'pass.lookup', label: 'Viewed a visitor (pass lookup)' },
  { value: 'pass.check_in', label: 'Check-ins' },
  { value: 'visit.', label: 'Visits registered by staff' },
  { value: 'session.', label: 'Sessions ended / extended' },
  { value: 'kiosk.', label: 'Kiosk pairing' },
  { value: 'problem.', label: 'Problem reports' },
  { value: 'computer.', label: 'Computers' },
  { value: 'peripheral.', label: 'Peripherals' },
  { value: 'assist.', label: 'Help requests' },
]

// Admin: who did what, when and from where. Read-only; entries are never changed.
export default function AuditLog() {
  const [staff, setStaff] = useState([])
  const [filters, setFilters] = useState({ staffId: '', action: '', from: localDateInput(), to: localDateInput() })
  const [entries, setEntries] = useState(null)
  const [error, setError] = useState('')
  const set = (k) => (e) => setFilters({ ...filters, [k]: e.target.value })

  useEffect(() => {
    api.getStaff().then(setStaff).catch(() => {})
  }, [])

  useEffect(() => {
    let current = true
    setEntries(null)
    api
      .getAudit({
        staffId: filters.staffId,
        action: filters.action,
        from: startOfDayIso(filters.from),
        to: startOfDayIso(filters.to, 1),
        limit: 500,
      })
      .then((list) => current && (setEntries(list), setError('')))
      .catch((e) => current && (setEntries([]), setError(e.message)))
    return () => {
      current = false
    }
  }, [filters])

  return (
    <section className="stack">
      <div className="toolbar">
        <h2>Audit log</h2>
      </div>
      <div className="filters">
        <label className="inline-field">
          <span>Who</span>
          <select value={filters.staffId} onChange={set('staffId')}>
            <option value="">Everyone (incl. kiosks)</option>
            {staff.map((s) => (
              <option key={s.id} value={s.id}>
                {s.fullName} ({s.username})
              </option>
            ))}
          </select>
        </label>
        <label className="inline-field">
          <span>Action</span>
          <select value={filters.action} onChange={set('action')}>
            {ACTIONS.map((a) => (
              <option key={a.value} value={a.value}>
                {a.label}
              </option>
            ))}
          </select>
        </label>
        <label className="inline-field">
          <span>From</span>
          <input type="date" value={filters.from} onChange={set('from')} />
        </label>
        <label className="inline-field">
          <span>To</span>
          <input type="date" value={filters.to} onChange={set('to')} />
        </label>
      </div>

      {error && <p className="form-error">{error}</p>}
      {entries === null ? (
        <p className="muted">Loading…</p>
      ) : entries.length === 0 ? (
        <Empty>No entries match.</Empty>
      ) : (
        <>
          {entries.length >= 500 && <p className="muted small">Showing the latest 500. Narrow the filters to see older ones.</p>}
          <div className="table-wrap" tabIndex={0} role="region" aria-label="Audit log entries">
            <table className="table">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Who</th>
                  <th>Action</th>
                  <th>Record</th>
                  <th>Details</th>
                  <th>IP address</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((e) => (
                  <tr key={e.id}>
                    <td className="nowrap">{dateTime(e.at)}</td>
                    <td>
                      {e.actorName}
                      {e.actorType !== 'Staff' && <span className="muted small"> ({e.actorType})</span>}
                    </td>
                    <td><code>{e.action}</code></td>
                    <td>{e.targetType ? `${e.targetType} ${e.targetId ?? ''}` : ''}</td>
                    <td className="cell-wrap">{e.details ?? ''}</td>
                    <td className="muted small">{e.ipAddress ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  )
}
