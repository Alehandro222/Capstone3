import { ago, clock, countdown, msUntil, useNow } from './format.js'
import { Badge, Empty } from './ui.jsx'

// Everyone waiting for a PC, first come first served. When a PC frees up it is held
// 5 minutes for the person at the top; they pick it on their phone.
export default function Waitlist({ waitlist, computers }) {
  const now = useNow()
  const free = computers.filter((c) => c.status === 'Available').length

  return (
    <section className="stack">
      <div className="toolbar">
        <h2>Waitlist ({waitlist.length})</h2>
        <span className="muted small">
          {free} PC{free === 1 ? '' : 's'} free · a freed PC is held 5 minutes for the first person · entries last 4 hours
        </span>
      </div>
      {waitlist.length === 0 ? (
        <Empty>Nobody is waiting.</Empty>
      ) : (
        <div className="table-wrap" tabIndex={0} role="region" aria-label="People waiting">
          <table className="table">
            <thead>
              <tr>
                <th>#</th>
                <th>Visitor</th>
                <th>Joined</th>
                <th>Status</th>
                <th>Leaves the line</th>
              </tr>
            </thead>
            <tbody>
              {waitlist.map((w) => (
                <tr key={w.id}>
                  <td>{w.position}</td>
                  <td>{w.firstName}</td>
                  <td>
                    {clock(w.createdAt)} <span className="muted small">({ago(w.createdAt, now)})</span>
                  </td>
                  <td>
                    {w.computerReadyUntil ? (
                      <Badge tone="reserved">PC held · {countdown(msUntil(w.computerReadyUntil, now))} to pick it</Badge>
                    ) : (
                      'Waiting'
                    )}
                  </td>
                  <td>{clock(w.expiresAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
