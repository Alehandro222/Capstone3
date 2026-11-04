import { useEffect, useState } from 'react'
import { api } from '../api.js'
import './kiosk.css'

// One-time screen for staff: pick which computer this browser is the kiosk for.
// Choosing one changes the URL to #/kiosk/<id>, which Kiosk.jsx also remembers.
export default function KioskSetup() {
  const [computers, setComputers] = useState([])
  const [error, setError] = useState('')

  useEffect(() => {
    api
      .getComputers()
      .then(setComputers)
      .catch((e) => setError(e.message))
  }, [])

  return (
    <div className="k-screen">
      <main className="k-main">
        <div className="k-card">
          <h1>Set up this computer</h1>
          <p className="k-lead">Staff: choose which computer this screen belongs to.</p>

          {error && <p className="k-error">Cannot load the computer list: {error}</p>}

          <div className="k-chips">
            {computers.map((c) => (
              <button
                key={c.id}
                className="k-chip"
                onClick={() => {
                  window.location.hash = `#/kiosk/${c.id}`
                }}
              >
                {c.name}
              </button>
            ))}
          </div>
        </div>
      </main>
    </div>
  )
}
