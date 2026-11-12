// Report periods, in Philippine days written "YYYY-MM-DD". Pure date arithmetic on the
// calendar date (no clocks, no time zones), so the result is the same on every device.

// "2026-11-10" plus 3 days -> "2026-11-13" (crosses months and years correctly).
export function shiftDay(day, days) {
  const d = new Date(`${day}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + days)
  return d.toISOString().slice(0, 10)
}

// Monday = 0 ... Sunday = 6 (weeks start on Monday, as on a Philippine office calendar).
export const weekdayIndex = (day) => (new Date(`${day}T00:00:00Z`).getUTCDay() + 6) % 7

export const monthStart = (day) => `${day.slice(0, 8)}01`

// The period buttons on the Reports tab. range(today) -> [from, to], both days included.
export const PERIODS = [
  { key: 'today', label: 'Today', range: (t) => [t, t] },
  { key: 'yesterday', label: 'Yesterday', range: (t) => [shiftDay(t, -1), shiftDay(t, -1)] },
  { key: 'week', label: 'This week', range: (t) => [shiftDay(t, -weekdayIndex(t)), t] },
  { key: 'month', label: 'This month', range: (t) => [monthStart(t), t] },
  {
    key: 'lastMonth',
    label: 'Last month',
    range: (t) => {
      const end = shiftDay(monthStart(t), -1)
      return [monthStart(end), end]
    },
  },
  { key: 'custom', label: 'Custom' },
]

export const periodRange = (key, today) => PERIODS.find((p) => p.key === key).range(today)

// "2026-11-08" -> "Nov 8" (the date itself, never shifted by the device's time zone).
export const dayLabel = (day) =>
  new Date(`${day}T00:00:00Z`).toLocaleDateString('en-PH', { timeZone: 'UTC', month: 'short', day: 'numeric' })

// "0" -> "12 AM", "13" -> "1 PM"
export function hourLabel(hour) {
  const n = Number(hour)
  return `${n % 12 === 0 ? 12 : n % 12} ${n < 12 ? 'AM' : 'PM'}`
}
