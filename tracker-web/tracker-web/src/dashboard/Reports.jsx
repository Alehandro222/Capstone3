import { useEffect, useState } from 'react'
import { api } from '../api'
import { AGE_GROUPS, SECTORS, SERVICES, SEXES } from '../registration.js'
import { todayInManila } from '../time.js'
import { END_REASON_LABEL, SOURCE_LABEL } from './format.js'
import { PERIODS, dayLabel, hourLabel, monthStart, periodRange } from './periods.js'
import ReportAssistant from './ReportAssistant.jsx'
import { Empty } from './ui.jsx'

const label = (options) => Object.fromEntries(options.map((o) => [o.value, o.label]))
const LABELS = {
  age: label(AGE_GROUPS),
  sex: label(SEXES),
  sector: label(SECTORS),
  service: label(SERVICES),
  source: SOURCE_LABEL,
  end: END_REASON_LABEL,
}

const fmt = (n) => n.toLocaleString('en-PH', { maximumFractionDigits: 1 })

// Reports: visits and PC use for a period, counted in Philippine time.
// Counts only on screen; the lists with names are in the admin exports.
export default function Reports({ isAdmin, refreshKey }) {
  const today = todayInManila()
  const [period, setPeriod] = useState('month')
  const [custom, setCustom] = useState({ from: monthStart(today), to: today })
  const [report, setReport] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState('') // which download is running

  const [from, to] = period === 'custom' ? [custom.from, custom.to] : periodRange(period, today)

  useEffect(() => {
    if (!from || !to) return undefined
    let current = true
    api
      .getReport(from, to)
      .then((r) => current && (setReport(r), setError('')))
      .catch((e) => current && setError(e.message))
    return () => {
      current = false
    }
  }, [from, to, refreshKey])

  async function download(key, params) {
    setBusy(key)
    setError('')
    try {
      await api.downloadReport({ from, to, ...params })
    } catch (e) {
      setError(e.message)
    } finally {
      setBusy('')
    }
  }

  const r = report
  const busiestHour = r && r.visitsByHour.reduce((a, b) => (b.count > a.count ? b : a))

  return (
    <section className="stack-lg reports">
      <div className="toolbar">
        <h2>Reports</h2>
        <span className="muted small">Philippine time · counts only, no names</span>
      </div>

      <div className="filters">
        <div className="segmented" role="group" aria-label="Period">
          {PERIODS.map((p) => (
            <button
              key={p.key}
              className={period === p.key ? 'seg seg-on' : 'seg'}
              aria-pressed={period === p.key}
              onClick={() => setPeriod(p.key)}
            >
              {p.label}
            </button>
          ))}
        </div>
        {period === 'custom' && (
          <>
            <label className="inline-field">
              <span>From</span>
              <input type="date" value={custom.from} max={today} onChange={(e) => setCustom({ ...custom, from: e.target.value })} />
            </label>
            <label className="inline-field">
              <span>To</span>
              <input type="date" value={custom.to} max={today} onChange={(e) => setCustom({ ...custom, to: e.target.value })} />
            </label>
          </>
        )}
      </div>

      {error && <p className="form-error" role="alert">{error}</p>}
      {!r && !error && <p className="muted">Loading…</p>}

      {r && (
        <>
          <p className="muted small m0">
            {r.from === r.to ? dayLabel(r.from) : `${dayLabel(r.from)} – ${dayLabel(r.to)}`}, {r.from.slice(0, 4)}
          </p>

          <div className="tiles">
            <Tile value={fmt(r.visits)} label="Visits" />
            <Tile value={fmt(r.sessions)} label="PC sessions" note={`${fmt(r.visitsWithComputer)} visitors used a PC`} />
            <Tile value={fmt(r.sessionHours)} label="PC hours" note={r.runningSessions ? `${r.runningSessions} still running` : null} />
            <Tile value={`${fmt(r.averageSessionMinutes)} min`} label="Average session" note={r.extensions ? `${r.extensions} extensions` : null} />
          </div>

          {r.visits === 0 && r.sessions === 0 ? (
            <Empty>No visits in this period.</Empty>
          ) : (
            <>
              {r.byDay.length > 1 && (
                <div className="report-grid two">
                  <Columns title="Visits per day" data={r.byDay.map((d) => ({ key: d.day, label: dayLabel(d.day), value: d.visits }))} />
                  <Columns title="PC sessions per day" data={r.byDay.map((d) => ({ key: d.day, label: dayLabel(d.day), value: d.sessions }))} />
                </div>
              )}

              <div className="report-grid two">
                <Columns
                  title="Visits by hour of the day"
                  note={busiestHour?.count ? `Busiest: ${hourLabel(busiestHour.key)}` : null}
                  data={openHours(r.visitsByHour).map((h) => ({ key: h.key, label: hourLabel(h.key), value: h.count }))}
                />
                <Columns
                  title="PC sessions started by hour"
                  data={openHours(r.sessionsByHour).map((h) => ({ key: h.key, label: hourLabel(h.key), value: h.count }))}
                />
              </div>

              <div className="report-grid three">
                <Bars title="Age" items={r.byAgeGroup} labels={LABELS.age} />
                <Bars title="Sex" items={r.bySex} labels={LABELS.sex} />
                <Bars title="Registered by" items={r.bySource} labels={LABELS.source} />
                <Bars title="Sector" note="A visitor can tick several" items={r.bySector} labels={LABELS.sector} />
                <Bars title="Purpose / services availed" note="A visitor can tick several" items={r.byService} labels={LABELS.service} />
                <Bars title="How PC sessions ended" items={r.sessionEndReasons} labels={LABELS.end} />
              </div>

              <div className="report-grid two">
                <Bars
                  title="Hours per PC"
                  unit="h"
                  items={r.byComputer.map((c) => ({ key: c.name, count: c.hours, note: `${c.sessions} session${c.sessions === 1 ? '' : 's'}` }))}
                />
                <section className="panel">
                  <h3>Waitlist</h3>
                  <dl className="details">
                    <dt>Joined</dt>
                    <dd>{fmt(r.waitlist.joined)}</dd>
                    <dt>Got a PC</dt>
                    <dd>{fmt(r.waitlist.gotAComputer)}</dd>
                    <dt>Missed their turn</dt>
                    <dd>{fmt(r.waitlist.missedTurn)}</dd>
                    <dt>Waited too long</dt>
                    <dd>{fmt(r.waitlist.expired)}</dd>
                    <dt>Left the line</dt>
                    <dd>{fmt(r.waitlist.left)}</dd>
                  </dl>
                </section>
              </div>
            </>
          )}

          {isAdmin && <ReportAssistant from={r.from} to={r.to} />}

          <section className="panel">
            <h3>Download</h3>
            <div className="actions">
              <button className="btn btn-primary" disabled={!!busy} onClick={() => download('xlsx', { format: 'xlsx' })}>
                {busy === 'xlsx' ? 'Preparing…' : 'Excel: summary and days'}
              </button>
              <button className="btn" disabled={!!busy} onClick={() => download('csv-summary', { format: 'csv', table: 'summary' })}>
                CSV: summary
              </button>
              <button className="btn" disabled={!!busy} onClick={() => download('csv-days', { format: 'csv', table: 'days' })}>
                CSV: by day
              </button>
            </div>
            {isAdmin && (
              <>
                <p className="muted small">
                  Admins: lists with visitors' names (Data Privacy Act: keep them secure; every download is in the audit log).
                </p>
                <div className="actions">
                  <button className="btn" disabled={!!busy} onClick={() => download('xlsx-names', { format: 'xlsx', names: true })}>
                    {busy === 'xlsx-names' ? 'Preparing…' : 'Excel: with visitor and session lists'}
                  </button>
                  <button className="btn" disabled={!!busy} onClick={() => download('csv-visits', { format: 'csv', table: 'visits' })}>
                    CSV: visitor list
                  </button>
                  <button className="btn" disabled={!!busy} onClick={() => download('csv-sessions', { format: 'csv', table: 'sessions' })}>
                    CSV: PC sessions
                  </button>
                </div>
              </>
            )}
          </section>
        </>
      )}
    </section>
  )
}

// The library's opening hours, roughly: always show 7 AM to 8 PM, plus any other hour with data.
function openHours(hours) {
  return hours.filter((h) => (Number(h.key) >= 7 && Number(h.key) <= 20) || h.count > 0)
}

function Tile({ value, label, note }) {
  return (
    <div className="tile">
      <span className="tile-value">{value}</span>
      <span className="tile-label">{label}</span>
      {note && <span className="muted small">{note}</span>}
    </div>
  )
}

// Horizontal bars with the number beside each (also readable as a table).
function Bars({ title, note, items, labels = {}, unit = '' }) {
  const max = Math.max(1, ...items.map((i) => i.count))
  return (
    <section className="panel">
      <h3>{title}</h3>
      {note && <p className="muted small m0">{note}</p>}
      <ul className="hbars">
        {items.map((i) => (
          <li key={i.key} className="hbar">
            <span className="hbar-label">{labels[i.key] ?? i.key}</span>
            <span className="hbar-track">
              {i.count > 0 && <span className="hbar-fill" style={{ width: `${(i.count / max) * 100}%` }} />}
            </span>
            <span className="hbar-value">
              {fmt(i.count)}
              {unit && ` ${unit}`}
              {i.note && <span className="muted small"> · {i.note}</span>}
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}

// Vertical columns over time (days or hours), with a hover tooltip and a table view.
function Columns({ title, note, data }) {
  const [showTable, setShowTable] = useState(false)
  const [hover, setHover] = useState(null)
  const max = Math.max(1, ...data.map((d) => d.value))
  const step = Math.ceil(data.length / 12) // label at most ~12 ticks

  return (
    <section className="panel">
      <div className="section-head">
        <h3 className="m0">
          {title}
          {note && <span className="h3-note"> · {note}</span>}
        </h3>
        <button className="link-button small" aria-pressed={showTable} onClick={() => setShowTable((v) => !v)}>
          {showTable ? 'Show chart' : 'Show table'}
        </button>
      </div>

      {showTable ? (
        <div className="table-wrap" tabIndex={0} role="region" aria-label={`${title} (table)`}>
          <table className="table">
            <tbody>
              {data.map((d) => (
                <tr key={d.key}>
                  <td>{d.label}</td>
                  <td className="num">{fmt(d.value)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div
          className="columns"
          role="img"
          aria-label={`${title}. Highest: ${fmt(max)}. Use "Show table" for every value.`}
          onMouseLeave={() => setHover(null)}
        >
          <span className="columns-max muted small">{fmt(max)}</span>
          <div className="columns-plot">
            {data.map((d, i) => (
              <div
                key={d.key}
                className={hover === i ? 'col col-on' : 'col'}
                onMouseEnter={() => setHover(i)}
                aria-hidden="true"
              >
                <span className="col-fill" style={{ height: `${(d.value / max) * 100}%` }} />
                {hover === i && (
                  <span className="col-tip" role="tooltip">
                    <strong>{fmt(d.value)}</strong> {d.label}
                  </span>
                )}
              </div>
            ))}
          </div>
          <div className="columns-axis" aria-hidden="true">
            {data.map((d, i) => (
              <span key={d.key} className="muted small">{i % step === 0 ? d.label : ''}</span>
            ))}
          </div>
        </div>
      )}
    </section>
  )
}
