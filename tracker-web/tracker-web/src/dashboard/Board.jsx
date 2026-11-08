import { useState } from 'react'
import PeripheralList from '../components/PeripheralList.jsx'
import { STATUS_LABEL, STATUS_TONE, ago, clock, countdown, msUntil, useNow } from './format.js'
import { Badge } from './ui.jsx'

const WARN_MS = 10 * 60 * 1000 // the last 10 minutes: when visitors may extend

const FILTERS = [
  { key: 'all', label: 'All' },
  { key: 'Available', label: 'Available' },
  { key: 'busy', label: 'In use / reserved' },
  { key: 'down', label: 'Maintenance / offline' },
]

const matches = (filter, status) =>
  filter === 'all' ||
  filter === status ||
  (filter === 'busy' && (status === 'InUse' || status === 'Reserved')) ||
  (filter === 'down' && (status === 'Maintenance' || status === 'Offline'))

// Live board: every PC, who is on it, and how long they have left.
//   on.* are the card actions, each given the computer (and its session).
export default function Board({ computers, sessions, pendingPasses, on }) {
  const now = useNow()
  const [filter, setFilter] = useState('all')
  const shown = computers.filter((c) => matches(filter, c.status))

  return (
    <section>
      <div className="toolbar">
        <div className="segmented" role="group" aria-label="Show computers">
          {FILTERS.map((f) => (
            <button
              key={f.key}
              className={filter === f.key ? 'seg seg-on' : 'seg'}
              aria-pressed={filter === f.key}
              onClick={() => setFilter(f.key)}
            >
              {f.label} ({computers.filter((c) => matches(f.key, c.status)).length})
            </button>
          ))}
        </div>
      </div>

      <div className="grid">
        {shown.map((computer) => (
          <ComputerCard
            key={computer.id}
            computer={computer}
            session={sessions.find((s) => s.computerId === computer.id)}
            pass={pendingPasses.find((p) => p.computerId === computer.id)}
            now={now}
            on={on}
          />
        ))}
      </div>
    </section>
  )
}

function ComputerCard({ computer, session, pass, now, on }) {
  const tone = STATUS_TONE[computer.status]
  const endsAt = session?.scheduledEndAt ?? computer.sessionEndsAt
  const left = endsAt ? msUntil(endsAt, now) : null
  const warn = computer.status === 'InUse' && left !== null && left <= WARN_MS

  return (
    <article className={`card card-${tone}${warn ? ' card-warn' : ''}`}>
      <div className="card-head">
        <h2>{computer.name}</h2>
        <Badge tone={tone}>{STATUS_LABEL[computer.status] ?? computer.status}</Badge>
      </div>

      {computer.status === 'InUse' && (
        <div className="user">
          <p className="user-name">{session?.userName ?? computer.currentUser}</p>
          <p className="muted small">{session?.purpose ?? computer.currentPurpose}</p>
          {left !== null && (
            <p className={warn ? 'timeleft timeleft-warn' : 'timeleft'}>
              <span className="timeleft-value">{countdown(left)}</span> left
              <span className="muted small"> · ends {clock(endsAt)}</span>
            </p>
          )}
          {session && (
            <p className="muted small">
              Started {clock(session.startedAt)}
              {session.extensionCount > 0 && ` · extended ${session.extensionCount}×`}
            </p>
          )}
        </div>
      )}

      {computer.status === 'Reserved' && (
        <p className="muted user-empty">
          {pass
            ? `Held for ${pass.visit.firstName} ${pass.visit.lastName} · code ${pass.code} · ${countdown(msUntil(pass.expiresAt, now))} to check in`
            : 'Reserved: waiting for the visitor to check in'}
        </p>
      )}
      {computer.status === 'Available' && <p className="muted user-empty">Ready for the next visitor</p>}
      {computer.status === 'Maintenance' && (
        <p className="user-empty problem-text">{computer.openProblem ?? 'Under maintenance'}</p>
      )}
      {computer.status === 'Offline' && (
        <p className="muted user-empty">
          No signal from the kiosk app
          {computer.lastHeartbeatAt ? ` since ${ago(computer.lastHeartbeatAt, now)}` : ' yet'}. Is the PC on?
        </p>
      )}

      <PeripheralList
        peripherals={computer.peripherals}
        canToggle={computer.status === 'InUse'}
        onToggle={(p) => on.togglePeripheral(p)}
      />

      <div className="actions">
        {computer.status === 'Available' && (
          <button className="btn btn-primary" onClick={() => on.register(computer)}>
            Register visitor
          </button>
        )}
        {computer.status === 'InUse' && session && (
          <>
            <button
              className="btn"
              onClick={() => on.extend(computer, session)}
              disabled={!session.canExtend}
              title={session.canExtend ? 'Add 1 hour' : session.cannotExtendReason ?? ''}
            >
              Extend 1 h
            </button>
            <button className="btn btn-danger-quiet" onClick={() => on.end(computer, session)}>
              End session
            </button>
          </>
        )}
        <button className="btn btn-quiet" onClick={() => on.details(computer)}>
          Details
        </button>
      </div>
    </article>
  )
}
