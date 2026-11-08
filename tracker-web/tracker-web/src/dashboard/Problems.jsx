import { useEffect, useState } from 'react'
import { api } from '../api'
import { parseUtc } from '../time.js'
import { ago, dateTime, useNow } from './format.js'
import { Badge, Empty } from './ui.jsx'

// Problem reports: a PC with an open report shows as Maintenance until it is resolved.
//   openProblems   the open reports (refreshed by the dashboard)
export default function Problems({ openProblems, refreshKey, onReport, onResolve, onShowComputer }) {
  const now = useNow(30000)
  const [resolved, setResolved] = useState([])

  // Recently resolved reports, reloaded whenever the board refreshes after an action.
  useEffect(() => {
    api
      .getProblems({ status: 'Resolved' })
      .then((list) => setResolved(list.sort((a, b) => parseUtc(b.resolvedAt) - parseUtc(a.resolvedAt)).slice(0, 30)))
      .catch(() => {})
  }, [refreshKey])

  const open = [...openProblems].sort((a, b) => parseUtc(a.createdAt) - parseUtc(b.createdAt))

  return (
    <section className="stack-lg">
      <div className="toolbar">
        <h2>Open problems ({open.length})</h2>
        <button className="btn btn-primary" onClick={() => onReport(null)}>
          Report a problem
        </button>
      </div>

      {open.length === 0 ? (
        <Empty>No open problems. Every PC is in working order.</Empty>
      ) : (
        <ul className="help-list">
          {open.map((p) => (
            <li key={p.id} className="help-card help-Open">
              <div className="help-head">
                <button className="link-button strong" onClick={() => onShowComputer(p.computerId)}>
                  {p.computerName}
                </button>
                <Badge tone="broken">Open</Badge>
              </div>
              <p className="help-message">{p.description}</p>
              <p className="muted small">Reported {ago(p.createdAt, now)} ({dateTime(p.createdAt)})</p>
              <div className="actions">
                <button className="btn btn-primary" onClick={() => onResolve(p)}>
                  Resolve
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <h2>Recently resolved</h2>
      {resolved.length === 0 ? (
        <Empty>Nothing resolved yet.</Empty>
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>PC</th>
                <th>Problem</th>
                <th>Reported</th>
                <th>Resolved</th>
                <th>Notes</th>
              </tr>
            </thead>
            <tbody>
              {resolved.map((p) => (
                <tr key={p.id}>
                  <td>{p.computerName}</td>
                  <td>{p.description}</td>
                  <td>{dateTime(p.createdAt)}</td>
                  <td>{dateTime(p.resolvedAt)}</td>
                  <td>{p.resolutionNotes ?? ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
