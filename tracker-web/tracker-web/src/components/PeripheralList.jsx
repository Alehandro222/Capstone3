// Display text per peripheral type from the API.
const TYPE_LABEL = {
  Mouse: 'Mouse',
  Keyboard: 'Keyboard',
  Headset: 'Headset',
  Microphone: 'Microphone',
  DrawingTablet: 'Drawing tablet',
  Webcam: 'Webcam',
  Other: 'Other',
}

export default function PeripheralList({ peripherals, canToggle, onToggle }) {
  if (peripherals.length === 0) {
    return <p className="muted small">No peripherals connected</p>
  }

  return (
    <div className="peripherals">
      <h3>Connected devices</h3>
      <ul>
        {peripherals.map((p) => (
          <li key={p.id} className={p.isInUse ? 'peripheral peripheral-live' : 'peripheral'}>
            <div className="peripheral-info">
              <span className="peripheral-name">{p.name}</span>
              <span className="muted small">{TYPE_LABEL[p.type] ?? p.type}</span>
            </div>

            {/* Only let staff toggle "in use" while a session is running. */}
            <button
              className={p.isInUse ? 'chip chip-on' : 'chip'}
              disabled={!canToggle}
              onClick={() => onToggle(p)}
              title={canToggle ? 'Toggle in-use state' : 'Start a session first'}
            >
              {p.isInUse ? 'In use' : 'Idle'}
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
