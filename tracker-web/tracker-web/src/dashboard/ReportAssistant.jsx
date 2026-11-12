import { useEffect, useState } from 'react'
import { api } from '../api'
import { dayLabel } from './periods.js'

// Admins: ask Google Gemini to draft the report for the period shown on the Reports tab.
// Only the counts are sent (no names, no individual visits, no typed text); the panel
// shows exactly what was sent. The draft is a starting point: staff check and edit it.
export default function ReportAssistant({ from, to }) {
  const [status, setStatus] = useState(null) // { configured, model }
  const [result, setResult] = useState(null) // { draft, sentToGemini, model, from, to }
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    api.getAssistantStatus().then(setStatus).catch(() => setStatus({ configured: false, model: '' }))
  }, [])

  // A draft belongs to its period: changing the period clears it.
  useEffect(() => {
    setResult(null)
    setError('')
  }, [from, to])

  async function write() {
    setBusy(true)
    setError('')
    try {
      setResult(await api.draftReport(from, to))
    } catch (e) {
      setError(e.message)
    } finally {
      setBusy(false)
    }
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(result.draft)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      setError('Copying is blocked in this browser. Select the text and copy it instead.')
    }
  }

  function download() {
    const blob = new Blob([result.draft], { type: 'text/markdown;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `misa-report-draft_${result.from}${result.from === result.to ? '' : `_to_${result.to}`}.md`
    link.click()
    setTimeout(() => URL.revokeObjectURL(url), 10000)
  }

  const period = from === to ? dayLabel(from) : `${dayLabel(from)} – ${dayLabel(to)}`

  return (
    <section className="panel assistant">
      <div className="section-head">
        <h3 className="m0">Report draft by Gemini</h3>
        {status?.configured && <span className="muted small">{status.model}</span>}
      </div>
      <p className="muted small m0">
        Sends only the counts on this page to Google Gemini: no names, no individual visits. Google may use what its
        free tier receives. The draft is a starting point: check every number against the figures above before you
        send it.
      </p>

      {status && !status.configured && (
        <p className="form-warning">
          Not set up yet: the server needs a Gemini API key (free from Google AI Studio). See "Report assistant" in the
          API README.
        </p>
      )}

      {status?.configured && (
        <div className="actions">
          <button className="btn btn-primary" disabled={busy} onClick={write}>
            {busy ? 'Gemini is writing… (up to a minute)' : result ? `Write again for ${period}` : `Write a draft for ${period}`}
          </button>
          {result && (
            <>
              <button className="btn" onClick={copy}>{copied ? 'Copied' : 'Copy text'}</button>
              <button className="btn" onClick={download}>Download (.md)</button>
            </>
          )}
        </div>
      )}

      {error && <p className="form-error" role="alert">{error}</p>}

      {result && (
        <>
          <article className="draft">
            <Markdown text={result.draft} />
          </article>
          <details className="sent">
            <summary>What was sent to Google</summary>
            <pre>{result.sentToGemini}</pre>
          </details>
        </>
      )}
    </section>
  )
}

// The small Markdown subset the assistant is asked to use: "## " headings, "- " bullets,
// paragraphs. Built as React elements (never as HTML), so nothing in the reply can run.
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
      blocks.push(<h4 key={blocks.length}>{inline(heading[1])}</h4>)
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
