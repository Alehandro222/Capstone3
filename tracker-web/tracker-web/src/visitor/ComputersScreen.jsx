import { useCallback, useState } from 'react'
import { api } from '../api.js'
import { Button, CheckIcon, ComputerIcon, ErrorAlert, Header, Shell } from './ui.jsx'
import { usePolling } from './useClock.js'

const LABEL = {
  Available: 'Available',
  Reserved: 'Reserved',
  InUse: 'In use',
  Maintenance: 'Unavailable',
  Offline: 'Unavailable',
}

// Live PC availability. With onPick, visitors choose a free PC; without it
// (from the welcome screen) the list is only for looking.
export default function ComputersScreen({ onBack, onPick, onJoinWaitlist, readyUntilText }) {
  const [computers, setComputers] = useState([])
  const [selected, setSelected] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const viewOnly = !onPick

  const load = useCallback(async () => {
    try {
      const list = await api.publicComputers()
      setComputers(list)
      // A PC someone else just took can no longer stay selected.
      setSelected((id) => (list.some((c) => c.id === id && c.status === 'Available') ? id : null))
    } catch {
      setError('Cannot reach the library system. Check the Wi-Fi.')
    }
  }, [])
  usePolling(load, 5000)

  const free = computers.filter((c) => c.status === 'Available').length
  const chosen = computers.find((c) => c.id === selected)

  async function choose() {
    if (!chosen || busy) return
    setBusy(true)
    setError('')
    try {
      await onPick(chosen.id)
    } catch (e) {
      setError(e.message) // e.g. "That computer was just taken. Please choose another."
      setSelected(null)
      load()
    } finally {
      setBusy(false)
    }
  }

  async function join() {
    setBusy(true)
    setError('')
    try {
      await onJoinWaitlist()
    } catch (e) {
      setError(e.message)
      load()
    } finally {
      setBusy(false)
    }
  }

  return (
    <Shell>
      <Header
        title={viewOnly ? 'Available computers' : 'Choose a computer'}
        step={viewOnly ? 'Live status' : 'Pick a green one'}
        onBack={onBack}
      />
      <div className="v-body">
        <div className={`v-availability ${free === 0 ? 'v-availability--none' : ''}`}>
          <div className="v-availability__count">{free}</div>
          <div>
            <strong>{free === 1 ? 'computer available' : 'computers available'}</strong>
            <span>Updates every few seconds</span>
          </div>
          <span className="v-live">Live</span>
        </div>

        {readyUntilText && <p className="v-alert v-alert--ok">{readyUntilText}</p>}

        <div>
          <h1>{viewOnly ? 'Computer availability' : 'Pick a green computer'}</h1>
          <p className="v-supporting">
            {viewOnly ? 'See which computers are free right now.' : 'Tap the computer you would like to use.'}
          </p>
        </div>

        <div className="v-legend" aria-label="Legend">
          <span><i className="v-dot v-dot--available" />Available</span>
          <span><i className="v-dot v-dot--reserved" />Reserved</span>
          <span><i className="v-dot v-dot--busy" />In use / unavailable</span>
        </div>

        <div className="v-grid">
          {computers.map((c) => {
            const tone = c.status === 'Available' ? 'available' : c.status === 'Reserved' ? 'reserved' : 'busy'
            const active = selected === c.id
            return (
              <button
                key={c.id}
                type="button"
                className={`v-pc v-pc--${tone} ${active ? 'v-pc--selected' : ''}`}
                disabled={viewOnly || c.status !== 'Available'}
                onClick={() => setSelected(c.id)}
                aria-pressed={active}
                aria-label={`${c.name}, ${LABEL[c.status]}`}
              >
                {active && <span className="v-pc__check"><CheckIcon /></span>}
                <ComputerIcon />
                <strong>{c.name}</strong>
                <span>{active ? 'Selected' : LABEL[c.status]}</span>
              </button>
            )
          })}
        </div>

        <ErrorAlert message={error} />

        {!viewOnly && free === 0 && onJoinWaitlist && (
          <button className="v-link" type="button" onClick={join} disabled={busy}>
            No computer free? Join the waitlist
          </button>
        )}

        <div className="v-spacer" />
        <div className="v-sticky">
          {viewOnly ? (
            <Button onClick={onBack}>Back</Button>
          ) : (
            <Button onClick={choose} disabled={!chosen || busy}>
              {busy ? 'Reserving…' : chosen ? `Choose ${chosen.name}` : 'Choose a computer'}
            </Button>
          )}
        </div>
      </div>
    </Shell>
  )
}
