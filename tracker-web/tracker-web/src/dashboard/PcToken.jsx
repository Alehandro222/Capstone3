import { parseUtc } from '../time.js'
import { STATUS_LABEL, STATUS_TONE, countdown, msUntil } from './format.js'

const WARN_MS = 10 * 60 * 1000 // the last 10 minutes of a session
const DEFAULT_MS = 3 * 60 * 60 * 1000 // a normal session is 3 hours

// One PC as a circle. The ring drains as the session runs out; its colour is the status.
// The only round thing on the dashboard: everything around it is square-cornered.
export default function PcToken({ computer, session, now, size = 104, compact = false, dim = false, selected = false, changed = false, onClick }) {
  const tone = STATUS_TONE[computer.status]
  const endsAt = session?.scheduledEndAt ?? computer.sessionEndsAt
  const left = computer.status === 'InUse' && endsAt ? Math.max(0, msUntil(endsAt, now)) : null
  const total = session?.startedAt && endsAt ? parseUtc(endsAt) - parseUtc(session.startedAt) : DEFAULT_MS
  const fraction = left === null ? 0 : Math.min(1, left / Math.max(total, 1))
  const warn = left !== null && left <= WARN_MS

  const stroke = compact ? 4 : 9
  const radius = (size - stroke) / 2
  const circumference = 2 * Math.PI * radius
  const status = STATUS_LABEL[computer.status] ?? computer.status
  const detail = left !== null ? `${countdown(left)} left` : status

  return (
    <button
      type="button"
      className={`pc pc-${tone}${compact ? ' pc-compact' : ''}${dim ? ' pc-dim' : ''}${selected ? ' pc-selected' : ''}${warn ? ' pc-warn' : ''}${changed ? ' pc-changed' : ''}`}
      style={{ width: size, height: size }}
      aria-label={`${computer.name}: ${status}${left !== null ? `, ${detail}` : ''}`}
      aria-pressed={onClick ? selected : undefined}
      title={`${computer.name}: ${detail}`}
      onClick={onClick}
    >
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} aria-hidden="true">
        <circle className="pc-track" cx={size / 2} cy={size / 2} r={radius} strokeWidth={stroke} />
        {fraction > 0 && (
          <circle className="pc-arc" cx={size / 2} cy={size / 2} r={radius} strokeWidth={stroke}
            strokeDasharray={`${fraction * circumference} ${circumference}`}
            transform={`rotate(-90 ${size / 2} ${size / 2})`} />
        )}
      </svg>
      <span className="pc-name">{compact ? computer.name.replace(/^PC-?/i, '') : computer.name}</span>
      {!compact && <span className="pc-sub">{left !== null ? countdown(left) : status}</span>}
    </button>
  )
}
