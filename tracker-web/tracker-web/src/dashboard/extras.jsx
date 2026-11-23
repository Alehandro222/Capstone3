import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { countdown, msUntil } from './format.js'
import { useModal } from './useModal.js'

const WARN_MS = 10 * 60 * 1000 // the last 10 minutes of a session

// ---- Preferences (theme, row density, alert sound), remembered in this browser ----
const read = (key, fallback) => {
  try {
    return localStorage.getItem(key) ?? fallback
  } catch {
    return fallback
  }
}
const write = (key, value) => {
  try {
    localStorage.setItem(key, value)
  } catch {
    /* storage blocked: the choice just isn't remembered */
  }
}

export const THEMES = ['system', 'light', 'dark']

export function usePrefs() {
  const [theme, setTheme] = useState(() => (THEMES.includes(read('dash-theme')) ? read('dash-theme') : 'system'))
  const [density, setDensity] = useState(() => (read('dash-density') === 'compact' ? 'compact' : 'comfortable'))
  const [sound, setSound] = useState(() => read('dash-sound') === 'on')
  return {
    theme,
    density,
    sound,
    cycleTheme: () => setTheme((t) => {
      const next = THEMES[(THEMES.indexOf(t) + 1) % THEMES.length]
      write('dash-theme', next)
      return next
    }),
    toggleDensity: () => setDensity((d) => {
      const next = d === 'compact' ? 'comfortable' : 'compact'
      write('dash-density', next)
      return next
    }),
    toggleSound: () => setSound((s) => {
      write('dash-sound', s ? 'off' : 'on')
      return !s
    }),
  }
}

// ---- Things that need a person's attention ----
// shortcut: built from the board on every render, no history; fine for one help desk.
export function buildAlerts({ computers, sessions, tickets }, now) {
  const list = []
  for (const s of sessions) {
    const endsAt = s.scheduledEndAt
    const pc = computers.find((c) => c.id === s.computerId)
    if (!endsAt || !pc) continue
    const left = msUntil(endsAt, now)
    if (left <= WARN_MS) list.push({ key: `end-${s.id}`, tab: 'board', text: `${pc.name}: ${countdown(left)} left. Ask them to finish or extend.` })
  }
  for (const c of computers) {
    if (c.status === 'Offline') list.push({ key: `off-${c.id}`, tab: 'board', text: `${c.name} is offline. Is the PC on?` })
  }
  for (const t of tickets) {
    if (t.status === 'Open') list.push({ key: `help-${t.id}`, tab: 'help', text: `${t.computerName} is asking for help.` })
  }
  return list
}

function beep() {
  try {
    const ctx = new (window.AudioContext || window.webkitAudioContext)()
    const osc = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.frequency.value = 880
    gain.gain.value = 0.08
    osc.connect(gain)
    gain.connect(ctx.destination)
    osc.start()
    osc.stop(ctx.currentTime + 0.18)
    osc.onended = () => ctx.close()
  } catch {
    /* no audio available */
  }
}

// Plays a short tone when a new alert appears (only if the person turned sound on).
export function useChime(alerts, sound) {
  const seen = useRef(null)
  useEffect(() => {
    const keys = new Set(alerts.map((a) => a.key))
    if (seen.current && sound && [...keys].some((k) => !seen.current.has(k))) beep()
    seen.current = keys
  }, [alerts, sound])
}

export function AttentionTray({ alerts, sound, onToggleSound, onGo }) {
  const [open, setOpen] = useState(false)
  const panelId = useId()
  const box = useRef(null)

  useEffect(() => {
    if (!open) return undefined
    const onKey = (e) => e.key === 'Escape' && setOpen(false)
    const onDown = (e) => box.current && !box.current.contains(e.target) && setOpen(false)
    document.addEventListener('keydown', onKey)
    document.addEventListener('mousedown', onDown)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('mousedown', onDown)
    }
  }, [open])

  return (
    <div className="tray" ref={box}>
      <button className={alerts.length ? 'btn tray-btn tray-on' : 'btn tray-btn'} aria-expanded={open} aria-controls={panelId}
        onClick={() => setOpen((o) => !o)}>
        {alerts.length ? `Needs attention (${alerts.length})` : 'All clear'}
      </button>
      {open && (
        <div className="tray-panel" id={panelId}>
          {alerts.length === 0 ? (
            <p className="muted small m0">Nothing needs you right now.</p>
          ) : (
            <ul className="rows">
              {alerts.map((a) => (
                <li key={a.key} className="row">
                  <button className="link-button" onClick={() => { setOpen(false); onGo(a.tab) }}>{a.text}</button>
                </li>
              ))}
            </ul>
          )}
          <label className="choice">
            <input type="checkbox" checked={sound} onChange={onToggleSound} /> Play a sound for new alerts
          </label>
        </div>
      )}
    </div>
  )
}

// ---- Command palette (Ctrl+K) ----
// commands: [{ id, label, hint, run }]
export function CommandPalette({ commands, onClose }) {
  const ref = useModal(onClose)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const listId = useId()
  const shown = useMemo(() => {
    const q = query.trim().toLowerCase()
    return commands.filter((c) => !q || c.label.toLowerCase().includes(q) || c.hint?.toLowerCase().includes(q))
  }, [commands, query])
  const index = Math.min(active, Math.max(0, shown.length - 1))

  function choose(c) {
    onClose()
    c.run()
  }

  function onKeyDown(e) {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive(Math.min(index + 1, shown.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive(Math.max(index - 1, 0))
    } else if (e.key === 'Enter' && shown[index]) {
      e.preventDefault()
      choose(shown[index])
    }
  }

  return (
    <div className="overlay palette-overlay" onClick={onClose}>
      <div className="modal palette" role="dialog" aria-modal="true" aria-label="Quick search" ref={ref} tabIndex={-1}
        onClick={(e) => e.stopPropagation()}>
        <input
          className="palette-input"
          role="combobox"
          aria-expanded="true"
          aria-controls={shown.length ? listId : undefined}
          aria-activedescendant={shown[index] ? `${listId}-${shown[index].id}` : undefined}
          aria-label="Go to a screen, a PC or an action"
          placeholder="Go to a screen, a PC or an action"
          value={query}
          autoComplete="off"
          autoFocus
          onChange={(e) => { setQuery(e.target.value); setActive(0) }}
          onKeyDown={onKeyDown}
        />
        {shown.length > 0 && (
        <ul className="palette-list" id={listId} role="listbox" aria-label="Results">
          {shown.map((c, i) => (
            <li key={c.id} id={`${listId}-${c.id}`} role="option" aria-selected={i === index}
              className={i === index ? 'palette-item palette-on' : 'palette-item'}
              onMouseMove={() => setActive(i)} onClick={() => choose(c)}>
              <span>{c.label}</span>
              {c.hint && <span className="palette-hint">{c.hint}</span>}
            </li>
          ))}
        </ul>
        )}
        {shown.length === 0 && <p className="muted small m0" role="status">No match. Try a PC name such as PC-04.</p>}
      </div>
    </div>
  )
}
