import { useEffect, useState } from 'react'
import { startPolling } from '../polling.js'
import { formatTime, parseUtc, serverNow } from '../time.js'

// The current time, updated every second (for countdowns).
export function useNow() {
  const [now, setNow] = useState(serverNow) // the server's clock, so it matches the PC
  useEffect(() => {
    const timer = setInterval(() => setNow(serverNow()), 1000)
    return () => clearInterval(timer)
  }, [])
  return now
}

// Milliseconds left until an API time (never below 0).
export function msUntil(iso, now) {
  return iso ? Math.max(0, parseUtc(iso).getTime() - now) : 0
}

// 3725000 -> "1:02:05"; 125000 -> "2:05"
export function formatDuration(ms) {
  const total = Math.max(0, Math.round(ms / 1000))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const pad = (n) => String(n).padStart(2, '0')
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`
}

// "3:45 PM", Philippine time.
export const formatClock = formatTime

// Calls `load` now and then every `ms` milliseconds while the screen is shown
// (paused while the phone is locked or the page is in the background; see polling.js).
export function usePolling(load, ms) {
  useEffect(() => startPolling(load, ms), [load, ms])
}
