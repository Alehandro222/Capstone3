import { useEffect, useState } from 'react'
import { api } from '../api'
import { startPolling } from '../polling.js'
import { todayInManila } from '../time.js'
import { dateTime } from './format.js'
import { dayLabel, hourLabel } from './periods.js'
import { LABELS, fmt } from './reportLabels.js'
import { ConfirmDialog } from './ui.jsx'

const KINDS = [
  { value: 'Month', label: 'Monthly', dateLabel: 'Any day in the month' },
  { value: 'Week', label: 'Weekly', dateLabel: 'Any day in the week (Monday to Sunday)' },
  { value: 'Day', label: 'Daily', dateLabel: 'Day' },
]
const running = (run) => run?.status === 'Queued' || run?.status === 'Running'

// "November 2026", "Nov 2 – 8, 2026", "Nov 7, 2026"
export function periodTitle(kind, from, to) {
  const year = from.slice(0, 4)
  if (kind === 'Month') {
    const month = new Date(`${from}T00:00:00Z`).toLocaleDateString('en-PH', { timeZone: 'UTC', month: 'long' })
    return `${month} ${year}`
  }
  return from === to ? `${dayLabel(from)}, ${year}` : `${dayLabel(from)} – ${dayLabel(to)}, ${year}`
}

// Admins: make a day / week / month report. The server does the work in the background (reads
// the counts, asks Google Gemini for the text, checks every number in it against the data);
// this page starts it and checks on it every few seconds while it is open and visible.
// Only counts are sent to Google; the report shows exactly what was sent.
export default function ReportRuns() {
  const [status, setStatus] = useState(null) // { configured, model }
  const [kind, setKind] = useState('Month')
  const [date, setDate] = useState(todayInManila())
  const [runs, setRuns] = useState([])
  const [run, setRun] = useState(null)
  const [error, setError] = useState('')
  const [starting, setStarting] = useState(false)
  const [deleting, setDeleting] = useState(null)

  const loadRuns = () => api.getReportRuns().then(setRuns).catch(() => {})

  useEffect(() => {
    api.getAssistantStatus().then(setStatus).catch(() => setStatus({ configured: false, model: '' }))
    loadRuns()
  }, [])

  // Check on a report that is being made (paused while the page is hidden).
  const runId = run?.id
  const isRunning = running(run)
  useEffect(() => {
    if (!isRunning) return undefined
    return startPolling(async () => {
      try {
        const latest = await api.getReportRun(runId)
        setRun(latest)
        if (!running(latest)) loadRuns()
      } catch (e) {
        setError(e.message)
      }
    }, 4000)
  }, [runId, isRunning])

  async function start() {
    setStarting(true)
    setError('')
    try {
      setRun(await api.startReportRun(kind, date))
      loadRuns()
    } catch (e) {
      setError(e.message)
    } finally {
      setStarting(false)
    }
  }

  async function open(id) {
    setError('')
    try {
      setRun(await api.getReportRun(id))
    } catch (e) {
      setError(e.message)
    }
  }

  async function remove() {
    try {
      await api.deleteReportRun(deleting.id)
      if (run?.id === deleting.id) setRun(null)
      loadRuns()
    } catch (e) {
      setError(e.message)
    }
    setDeleting(null)
  }

  const dateLabel = KINDS.find((k) => k.value === kind).dateLabel

  return (
    <section className="panel assistant">
      <div className="section-head">
        <h3 className="m0">Report for DICT-CAR</h3>
        {status?.configured && <span className="muted small">{status.model}</span>}
      </div>
      <p className="muted small m0">
        Makes a finished daily, weekly or monthly report: the figures and tables come straight from the data, and
        Google Gemini writes the text from the counts only (no names, no individual visits). Every number in the text is
        checked against the data first. Google may use what its free tier receives. Reports are kept, counts only.
      </p>

      {status && !status.configured && (
        <p className="form-warning">
          Gemini is not set up yet, so reports will have the figures and tables but no written text. The server needs a
          Gemini API key (free from Google AI Studio): see "Report assistant" in the API README.
        </p>
      )}

      <form
        className="actions"
        onSubmit={(e) => {
          e.preventDefault()
          start()
        }}
      >
        <label>
          Report
          <select value={kind} onChange={(e) => setKind(e.target.value)}>
            {KINDS.map((k) => (
              <option key={k.value} value={k.value}>{k.label}</option>
            ))}
          </select>
        </label>
        <label>
          {dateLabel}
          <input type="date" value={date} max={todayInManila()} required onChange={(e) => setDate(e.target.value)} />
        </label>
        <button className="btn btn-primary" type="submit" disabled={starting || isRunning || !date}>
          {isRunning ? 'Making the report…' : 'Make the report'}
        </button>
      </form>

      {isRunning && (
        <p className="muted" role="status">
          {run.step}… This takes up to a minute; you can leave this page open.
        </p>
      )}
      {error && <p className="form-error" role="alert">{error}</p>}
      {run?.status === 'Failed' && <p className="form-error" role="alert">{run.note}</p>}

      {runs.length > 0 && (
        <details className="sent">
          <summary>Saved reports ({runs.length})</summary>
          <ul className="saved-runs">
            {runs.map((r) => (
              <li key={r.id}>
                <button className="btn btn-small" onClick={() => open(r.id)} aria-label={`Open ${KINDS.find((k) => k.value === r.kind).label.toLowerCase()} report, ${periodTitle(r.kind, r.from, r.to)}`}>
                  {KINDS.find((k) => k.value === r.kind).label}: {periodTitle(r.kind, r.from, r.to)}
                </button>
                <span className="muted small">
                  {r.status === 'Done' ? `made ${dateTime(r.createdAt)}` : r.status === 'Failed' ? 'failed' : 'in progress'}
                </span>
                {!running(r) && (
                  <button className="btn btn-small" onClick={() => setDeleting(r)} aria-label={`Delete the saved report for ${periodTitle(r.kind, r.from, r.to)}`}>
                    Delete
                  </button>
                )}
              </li>
            ))}
          </ul>
        </details>
      )}

      {run?.status === 'Done' && <ReportDocument run={run} />}

      {deleting && (
        <ConfirmDialog
          title="Delete this saved report?"
          message={`The ${periodTitle(deleting.kind, deleting.from, deleting.to)} report will be removed. You can make it again only while the visit records for that period are still kept (1 year).`}
          confirmLabel="Delete"
          onConfirm={remove}
          onCancel={() => setDeleting(null)}
        />
      )}
    </section>
  )
}

// The finished report: the same on screen and on paper ("Print" → save as PDF).
function ReportDocument({ run }) {
  const doc = run.document
  const r = doc.current
  const title = `${KINDS.find((k) => k.value === doc.kind).label} report: ${periodTitle(doc.kind, run.from, run.to)}`
  const days = r.byDay.length

  return (
    <>
      <div className="actions no-print">
        <button className="btn" onClick={() => window.print()}>Print / save as PDF</button>
      </div>
      <article className="report-doc" aria-label={title}>
        <header>
          <p className="report-doc__org m0">DICT Digital Transformation Center, Baguio City</p>
          <h4 className="report-doc__title m0">{title}</h4>
          <p className="muted small m0">
            {dayLabel(run.from)} – {dayLabel(run.to)}, {run.from.slice(0, 4)} ({days} day{days === 1 ? '' : 's'}, Philippine time)
            {doc.partial ? '. The period is not over yet: counted up to this day.' : ''}
          </p>
          <p className="muted small m0">Made {dateTime(run.createdAt)}</p>
        </header>

        {run.note && <p className="form-warning">{run.note}</p>}
        {doc.narrative && (
          <div className="draft">
            <Markdown text={doc.narrative} />
            <p className="muted small m0">
              {doc.kind === 'Day'
                ? 'Written by the system from the counts.'
                : 'Written by Gemini; every number in the text was checked against the data.'}
            </p>
          </div>
        )}

        <h4>Key figures</h4>
        <Table
          label="Key figures"
          head={['Measure', 'Value']}
          rows={[
            ['Visits', fmt(r.visits)],
            ['Visitors who used a PC', fmt(r.visitsWithComputer)],
            ['PC sessions', fmt(r.sessions)],
            ['PC hours', fmt(r.sessionHours)],
            ['Average session (minutes)', fmt(r.averageSessionMinutes)],
            ['Session extensions', fmt(r.extensions)],
          ]}
        />

        {doc.previous && (
          <>
            <h4>
              Compared with {dayLabel(doc.previous.from)}
              {doc.previous.from === doc.previous.to ? '' : ` – ${dayLabel(doc.previous.to)}`}, {doc.previous.from.slice(0, 4)}
            </h4>
            <Table
              label="Comparison with the previous period"
              head={['Measure', 'Now', 'Before', 'Change', 'Change %']}
              rows={doc.comparison.map((c) => [
                c.measure,
                fmt(c.now),
                fmt(c.before),
                `${c.change > 0 ? '+' : ''}${fmt(c.change)}`,
                c.changePercent == null ? '–' : `${c.changePercent > 0 ? '+' : ''}${fmt(c.changePercent)}%`,
              ])}
            />
          </>
        )}

        {days > 1 && (
          <>
            <h4>By day</h4>
            <Table
              label="By day"
              head={['Day', 'Visits', 'PC sessions', 'PC hours']}
              rows={r.byDay.map((d) => [dayLabel(d.day), fmt(d.visits), fmt(d.sessions), fmt(d.sessionHours)])}
            />
          </>
        )}

        <Counts title="Visits by hour of the day" items={r.visitsByHour.filter((h) => h.count > 0)} label={hourLabel} />
        <Counts title="Age" items={r.byAgeGroup} labels={LABELS.age} />
        <Counts title="Sex" items={r.bySex} labels={LABELS.sex} />
        <Counts title="Sector (a visitor can tick several)" items={r.bySector} labels={LABELS.sector} />
        <Counts title="Purpose / services availed (a visitor can tick several)" items={r.byService} labels={LABELS.service} />
        <Counts title="Registered by" items={r.bySource} labels={LABELS.source} />
        <Counts title="How PC sessions ended" items={r.sessionEndReasons} labels={LABELS.end} />

        <h4>PC use</h4>
        <Table
          label="PC use"
          head={['PC', 'Sessions', 'Hours']}
          rows={r.byComputer.map((c) => [c.name, fmt(c.sessions), fmt(c.hours)])}
        />

        <h4>Waitlist</h4>
        <Table
          label="Waitlist"
          head={['Measure', 'Value']}
          rows={[
            ['Joined', fmt(r.waitlist.joined)],
            ['Got a PC', fmt(r.waitlist.gotAComputer)],
            ['Missed their turn', fmt(r.waitlist.missedTurn)],
            ['Waited too long', fmt(r.waitlist.expired)],
            ['Left the line', fmt(r.waitlist.left)],
          ]}
        />
      </article>

      {run.sentToGemini && (
        <details className="sent no-print">
          <summary>What was sent to Google</summary>
          <pre>{run.sentToGemini}</pre>
        </details>
      )}
    </>
  )
}

function Table({ label, head, rows }) {
  return (
    <div className="table-wrap" tabIndex={0} role="region" aria-label={`${label} (table)`}>
      <table className="table">
        <thead>
          <tr>
            {head.map((h, i) => (
              <th key={h} scope="col" className={i > 0 ? 'num' : undefined}>{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row[0]}>
              {row.map((cell, i) => (i === 0 ? <th key={i} scope="row">{cell}</th> : <td key={i} className="num">{cell}</td>))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function Counts({ title, items, labels, label }) {
  if (items.length === 0) return null
  return (
    <>
      <h4>{title}</h4>
      <Table label={title} head={['Answer', 'Count']} rows={items.map((i) => [label ? label(i.key) : labels?.[i.key] ?? i.key, fmt(i.count)])} />
    </>
  )
}

// The small Markdown subset the report text uses: "## " headings, "- " bullets, paragraphs.
// Built as React elements (never as HTML), so nothing in the reply can run.
function Markdown({ text }) {
  const blocks = []
  let list = null
  let para = []

  const flushPara = () => {
    if (para.length) blocks.push(<p key={blocks.length}>{inline(para.join(' '))}</p>)
    para = []
  }
  const flushList = () => {
    if (list) blocks.push(<ul key={blocks.length}>{list.map((item, i) => <li key={i}>{inline(item)}</li>)}</ul>)
    list = null
  }

  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim()
    const heading = /^#{1,6}\s+(.*)$/.exec(line)
    const bullet = /^[-*]\s+(.*)$/.exec(line)
    if (!line) {
      flushPara()
      flushList()
    } else if (heading) {
      flushPara()
      flushList()
      blocks.push(<h5 key={blocks.length}>{inline(heading[1])}</h5>)
    } else if (bullet) {
      flushPara()
      ;(list ??= []).push(bullet[1])
    } else {
      flushList()
      para.push(line)
    }
  }
  flushPara()
  flushList()
  return blocks
}

// **bold** -> <strong>; everything else stays plain text.
function inline(text) {
  return text.split(/(\*\*[^*]+\*\*)/g).map((part, i) =>
    part.startsWith('**') && part.endsWith('**') && part.length > 4 ? <strong key={i}>{part.slice(2, -2)}</strong> : part,
  )
}
