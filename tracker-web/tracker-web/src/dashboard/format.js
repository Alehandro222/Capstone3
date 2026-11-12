// Labels and time formatting shared by the dashboard screens.
import { useEffect, useState } from 'react'
import { AGE_GROUPS, SECTORS, SERVICES, SEXES } from '../registration.js'
import { formatDateTime, formatTime, manilaMidnightIso, parseUtc, serverNow, todayInManila } from '../time.js'

const lookup = (options) => Object.fromEntries(options.map((o) => [o.value, o.label]))
const AGE = lookup(AGE_GROUPS)
const SEX = lookup(SEXES)
const SECTOR = lookup(SECTORS)
const SERVICE = lookup(SERVICES)

export const ageLabel = (v) => AGE[v] ?? v
export const sexLabel = (v) => SEX[v] ?? v
export const sectorsLabel = (list) => (list ?? []).map((v) => SECTOR[v] ?? v).join(', ')
export const servicesLabel = (list) => (list ?? []).map((v) => SERVICE[v] ?? v).join(', ')

export const SOURCE_LABEL = { Phone: 'Own phone', Desk: 'Help desk', Kiosk: 'Staff, on the PC' }

export const STATUS_LABEL = {
  Available: 'Available',
  Reserved: 'Reserved',
  InUse: 'In use',
  Maintenance: 'Maintenance',
  Offline: 'Offline',
}

// Colour family per PC status (classes in dashboard.css).
export const STATUS_TONE = {
  Available: 'available',
  Reserved: 'reserved',
  InUse: 'inuse',
  Maintenance: 'broken',
  Offline: 'offline',
}

export const END_REASON_LABEL = {
  Visitor: 'Visitor ended it',
  Staff: 'Ended by staff',
  Idle: 'Idle logout',
  TimeLimit: 'Time limit',
  System: 'System',
}

export const PASS_STATUS_LABEL = {
  Pending: 'Waiting to be scanned',
  Used: 'Already used',
  Expired: 'Expired',
  Cancelled: 'Cancelled',
}

// The waitlist holds free PCs by number, not particular PCs: with 2 offers open, any 2 of the
// free PCs are kept for those people. Returns the open offers (who, and until when).
export function heldForWaitlist(waitlist, now = serverNow()) {
  return waitlist.filter((w) => w.computerReadyUntil && msUntil(w.computerReadyUntil, now) > 0)
}

// "Dina (4:12 left), Eli (2:03 left)"
export const heldNames = (held, now = serverNow()) =>
  held.map((w) => `${w.firstName} (${countdown(msUntil(w.computerReadyUntil, now))} left)`).join(', ')

// The current time, updated every `ms` milliseconds (for countdowns).
export function useNow(ms = 1000) {
  const [now, setNow] = useState(serverNow)
  useEffect(() => {
    const timer = setInterval(() => setNow(serverNow()), ms)
    return () => clearInterval(timer)
  }, [ms])
  return now
}

// 5400000 -> "1:30:00"; 125000 -> "2:05". Negative values count as 0.
export function countdown(ms) {
  const total = Math.max(0, Math.floor(ms / 1000))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const pad = (n) => String(n).padStart(2, '0')
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`
}

// 95 -> "1 h 35 min"
export function minutesText(minutes) {
  const m = Math.max(0, Math.round(minutes))
  if (m < 60) return `${m} min`
  return `${Math.floor(m / 60)} h ${m % 60} min`
}

export const msUntil = (iso, now = serverNow()) => parseUtc(iso).getTime() - now

export function ago(iso, now = serverNow()) {
  const minutes = Math.max(0, Math.floor((now - parseUtc(iso).getTime()) / 60000))
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} h ${minutes % 60} min ago`
  return `${Math.floor(hours / 24)} d ago`
}

// Times and days are shown in Philippine time (see time.js).
export const clock = formatTime
export const dateTime = formatDateTime

// Today in the Philippines, "2026-11-10", for <input type="date">.
export const localDateInput = () => todayInManila()

// Midnight (Philippine time) at the start of a day from <input type="date">, for the API.
export const startOfDayIso = (dateInput, addDays = 0) => manilaMidnightIso(dateInput, addDays)
