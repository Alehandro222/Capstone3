// Labels and time formatting shared by the dashboard screens.
import { useEffect, useState } from 'react'
import { AGE_GROUPS, SECTORS, SERVICES, SEXES } from '../registration.js'
import { parseUtc } from '../time.js'

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

// The current time, updated every `ms` milliseconds (for countdowns).
export function useNow(ms = 1000) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), ms)
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

export const msUntil = (iso, now = Date.now()) => parseUtc(iso).getTime() - now

export function ago(iso, now = Date.now()) {
  const minutes = Math.max(0, Math.floor((now - parseUtc(iso).getTime()) / 60000))
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} h ${minutes % 60} min ago`
  return `${Math.floor(hours / 24)} d ago`
}

const timeFmt = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' })
const dateTimeFmt = new Intl.DateTimeFormat(undefined, {
  month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit',
})
export const clock = (iso) => (iso ? timeFmt.format(parseUtc(iso)) : '')
export const dateTime = (iso) => (iso ? dateTimeFmt.format(parseUtc(iso)) : '')

// "2026-11-08" (local) for <input type="date">
export function localDateInput(date = new Date()) {
  const pad = (n) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

// Local midnight of an <input type="date"> value, as an ISO string the API understands.
export function startOfDayIso(dateInput, addDays = 0) {
  if (!dateInput) return ''
  const [y, m, d] = dateInput.split('-').map(Number)
  return new Date(y, m - 1, d + addDays).toISOString()
}
