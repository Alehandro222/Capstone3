import PeripheralList from './PeripheralList.jsx'

const STATUS_LABEL = {
  Available: 'Available',
  InUse: 'In use',
  NotWorking: 'Not working',
}

const STATUS_TONE = {
  Available: 'available',
  InUse: 'inuse',
  NotWorking: 'broken',
}

// "Started 14 min ago" style text from an ISO date string.
function since(isoDate) {
  // The API sends UTC times; add "Z" if the serializer left it off.
  const date = new Date(/[zZ]|[+-]\d\d:\d\d$/.test(isoDate) ? isoDate : `${isoDate}Z`)
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
          {computer.status === 'NotWorking' ? 'Out of service' : 'Ready for the next user'}
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
              Start session
            </button>
            <button className="btn btn-quiet" onClick={() => onSetStatus('NotWorking')}>
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

        {computer.status === 'NotWorking' && (
          <button className="btn" onClick={() => onSetStatus('Available')}>
            Mark as fixed
          </button>
        )}
      </div>
    </article>
  )
}
