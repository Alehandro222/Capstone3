// Philippine time and the server's clock (src/time.js). Run: npm test
import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  formatDateTime,
  formatTime,
  manilaMidnightIso,
  noteServerDate,
  parseUtc,
  serverNow,
  todayInManila,
} from '../src/time.js'

test('API times without a Z are still read as UTC', () => {
  assert.equal(parseUtc('2026-11-10T01:30:00').toISOString(), '2026-11-10T01:30:00.000Z')
  assert.equal(parseUtc('2026-11-10T01:30:00Z').toISOString(), '2026-11-10T01:30:00.000Z')
  assert.equal(parseUtc('2026-11-10T09:30:00+08:00').toISOString(), '2026-11-10T01:30:00.000Z')
})

test('times are shown in Philippine time (UTC+8), whatever the device is set to', () => {
  assert.equal(formatTime('2026-11-10T01:30:00Z'), '9:30 AM')
  assert.equal(formatTime('2026-11-10T16:05:00Z'), '12:05 AM') // already the next day in Baguio
  assert.equal(formatDateTime('2026-11-09T17:05:00Z'), 'Nov 10, 1:05 AM')
  assert.equal(formatTime(null), '')
})

test('a Philippine day starts at 16:00 UTC the day before', () => {
  assert.equal(manilaMidnightIso('2026-11-10'), '2026-11-09T16:00:00.000Z')
  assert.equal(manilaMidnightIso('2026-11-10', 1), '2026-11-10T16:00:00.000Z')
  assert.equal(manilaMidnightIso('2027-01-01'), '2026-12-31T16:00:00.000Z')
  assert.equal(manilaMidnightIso(''), '')
})

test('countdowns follow the server clock when the device clock is off', () => {
  noteServerDate(new Date(Date.now() + 5 * 60_000).toUTCString()) // this device is 5 minutes slow
  assert.ok(Math.abs(serverNow() - Date.now() - 5 * 60_000) < 2000)

  noteServerDate(new Date(Date.now() + 1000).toUTCString()) // within rounding: ignored
  assert.equal(Math.round((serverNow() - Date.now()) / 1000), 0)

  noteServerDate(null) // no header: keep what we had
  noteServerDate('not a date')
  assert.equal(Math.round((serverNow() - Date.now()) / 1000), 0)
})

test('today in Manila is a YYYY-MM-DD date', () => {
  assert.match(todayInManila(), /^\d{4}-\d{2}-\d{2}$/)
})
