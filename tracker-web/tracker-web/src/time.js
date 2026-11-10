// Times for every page: read the API's UTC times, show them in Philippine time,
// and count down with the server's clock rather than the device's.

// DTC Baguio's time zone. Every time and every "day" the pages show uses it, whatever
// time zone the phone or PC is set to. (The Philippines has no daylight saving: always UTC+8.)
export const TIME_ZONE = 'Asia/Manila'
const UTC_OFFSET_HOURS = 8

// The API sends times in UTC ending in "Z". Older data may lack the "Z";
// add it back so the browser never reads the time as local time.
export function parseUtc(iso) {
  return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(iso) ? iso : `${iso}Z`)
}

// ---- The server's clock ----
// Every API answer has a Date header. If this device's clock is more than a few seconds
// off, countdowns use the server's time instead, so "time left" matches the PCs and the server.
let offsetMs = 0

export function noteServerDate(header) {
  const server = header ? Date.parse(header) : NaN
  if (Number.isNaN(server)) return
  const offset = server + 500 - Date.now() // the header has whole seconds only
  offsetMs = Math.abs(offset) < 3000 ? 0 : offset
}

export const serverNow = () => Date.now() + offsetMs

// ---- Showing times (Philippine time) ----
const timeFmt = new Intl.DateTimeFormat('en-PH', { timeZone: TIME_ZONE, hour: 'numeric', minute: '2-digit' })
const dateTimeFmt = new Intl.DateTimeFormat('en-PH', {
  timeZone: TIME_ZONE, month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit',
})
const fullFmt = new Intl.DateTimeFormat('en-PH', { timeZone: TIME_ZONE, dateStyle: 'medium', timeStyle: 'short' })

export const formatTime = (iso) => (iso ? timeFmt.format(parseUtc(iso)) : '')         // "3:45 PM"
export const formatDateTime = (iso) => (iso ? dateTimeFmt.format(parseUtc(iso)) : '') // "Nov 10, 3:45 PM"
export const formatFull = (iso) => (iso ? fullFmt.format(parseUtc(iso)) : '')         // "Nov 10, 2026, 3:45 PM"

// ---- Days (Philippine time) ----
// Today's date in the Philippines, "2026-11-10", for <input type="date">.
export function todayInManila() {
  const t = new Date(serverNow() + UTC_OFFSET_HOURS * 3600_000)
  return t.toISOString().slice(0, 10)
}

// Midnight in the Philippines at the start of a "2026-11-10" day (plus addDays), as a UTC ISO string.
export function manilaMidnightIso(day, addDays = 0) {
  if (!day) return ''
  const [y, m, d] = day.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + addDays, -UTC_OFFSET_HOURS)).toISOString()
}
