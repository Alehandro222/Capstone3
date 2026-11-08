import { parseUtc } from '../time.js'
import PeripheralList from './PeripheralList.jsx'

// The API works out the status (see TrackerApi/Models/Computer.cs).
const STATUS_LABEL = {
  Available: 'Available',
  Reserved: 'Reserved',
  InUse: 'In use',
  Maintenance: 'Maintenance',
  Offline: 'Offline',
}

const STATUS_TONE = {
  Available: 'available',
  Reserved: 'inuse',
  InUse: 'inuse',
  Maintenance: 'broken',
  Offline: 'broken',
}

const EMPTY_TEXT = {
  Available: 'Ready for the next user',
  Reserved: 'Waiting for the visitor to check in',
  Maintenance: 'Out of service',
  Offline: 'Not responding. Is the PC on and the kiosk app running?',
}

// "Started 14 min ago" style text from an ISO date string.
function since(isoDate) {
  const date = parseUtc(isoDate)
  const minutes = Math.max(0, Math.floor((Date.now() - date.getTime()) / 60000))
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.floor(minutes / 60)
  return `${hours} h ${minutes % 60} min ago`
}

export default function ComputerCard({
  computer,
  session,
  onStart,
  onEnd,
  onAssist,
  onTogglePeripheral,
  onSetStatus,
}) {
  const tone = STATUS_TONE[computer.status]

  return (
    <article className={`card card-${tone}`}>
      <div className="card-head">
        <h2>{computer.name}</h2>
        <span className={`badge badge-${tone}`}>{STATUS_LABEL[computer.status]}</span>
      </div>

      {computer.status === 'InUse' && session ? (
        <div className="user">
          <p className="user-name">{session.userName}</p>
          <p className="user-purpose">{session.purpose}</p>
          <p className="muted small">Started {since(session.startedAt)}</p>
        </div>
      ) : (
        <p className="muted user-empty">
          {computer.status === 'Maintenance' && computer.openProblem
            ? `Out of service: ${computer.openProblem}`
            : EMPTY_TEXT[computer.status]}
        </p>
      )}

      <PeripheralList
        peripherals={computer.peripherals}
        canToggle={computer.status === 'InUse'}
        onToggle={onTogglePeripheral}
      />

      <div className="actions">
        {computer.status === 'Available' && (
          <>
            <button className="btn btn-primary" onClick={onStart}>
              Register visitor
            </button>
            <button className="btn btn-quiet" onClick={() => onSetStatus('Maintenance')}>
              Report broken
            </button>
          </>
        )}

        {computer.status === 'InUse' && (
          <>
            <button className="btn" onClick={() => session && onEnd(session)} disabled={!session}>
              End session
            </button>
            <button className="btn btn-help" onClick={onAssist}>
              Need help?
            </button>
          </>
        )}

        {computer.status === 'Maintenance' && (
          <button className="btn" onClick={() => onSetStatus('Available')}>
            Mark as fixed
          </button>
        )}
      </div>
    </article>
  )
}
