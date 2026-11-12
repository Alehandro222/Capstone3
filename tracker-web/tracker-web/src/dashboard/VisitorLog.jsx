import { useEffect, useState } from 'react'
import { api } from '../api'
import {
  SOURCE_LABEL,
  ageLabel,
  clock,
  localDateInput,
  sectorsLabel,
  servicesLabel,
  sexLabel,
  startOfDayIso,
} from './format.js'
import { Empty } from './ui.jsx'

// Everyone who logged a visit on a given day (phone, help desk or kiosk), newest first.
export default function VisitorLog({ refreshKey }) {
  const [day, setDay] = useState(localDateInput)
  const [visits, setVisits] = useState(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let current = true
    api
      .getVisits({ from: startOfDayIso(day), to: startOfDayIso(day, 1), limit: 1000 })
      .then((list) => current && (setVisits(list), setError('')))
      .catch((e) => current && setError(e.message))
    return () => {
      current = false
    }
  }, [day, refreshKey])

  return (
    <section className="stack">
      <div className="toolbar">
        <h2>Visitor log{visits ? ` (${visits.length})` : ''}</h2>
        <label className="inline-field">
          <span>Day</span>
          <input type="date" value={day} max={localDateInput()} onChange={(e) => setDay(e.target.value)} />
        </label>
      </div>
      {error && <p className="form-error">{error}</p>}
      {visits === null ? (
        <p className="muted">Loading…</p>
      ) : visits.length === 0 ? (
        <Empty>No visits logged on this day.</Empty>
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Time</th>
                <th>Name</th>
                <th>Age</th>
                <th>Sex</th>
                <th>Sector</th>
                <th>Purpose / services</th>
                <th>Logged by</th>
              </tr>
            </thead>
            <tbody>
              {visits.map((v) => (
                <tr key={v.id}>
                  <td>{clock(v.createdAt)}</td>
                  <td>
                    {v.firstName} {v.lastName}
                  </td>
                  <td>{ageLabel(v.ageGroup)}</td>
                  <td>{sexLabel(v.sex)}</td>
                  <td>{sectorsLabel(v.sectors)}</td>
                  <td>{servicesLabel(v.services)}</td>
                  <td>{SOURCE_LABEL[v.source] ?? v.source}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
