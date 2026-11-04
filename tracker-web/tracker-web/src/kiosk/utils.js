// Key used to remember which computer a browser is the kiosk for.
export const STORAGE_KEY = 'kioskComputerId'

// The API sends UTC times but SQLite can drop the trailing "Z".
// Add it back so the browser doesn't read the time as local.
export function parseUtc(iso) {
  return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(iso) ? iso : `${iso}Z`)
}

// 3725000 ms -> "1:02:05"
export function formatElapsed(ms) {
  const total = Math.max(0, Math.floor(ms / 1000))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const pad = (n) => String(n).padStart(2, '0')
  return `${h}:${pad(m)}:${pad(s)}`
}
