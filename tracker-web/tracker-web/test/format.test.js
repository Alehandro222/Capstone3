// Countdowns and the waitlist hold on the dashboard (src/dashboard/format.js). Run: npm test
import assert from 'node:assert/strict'
import { test } from 'node:test'
import { countdown, heldForWaitlist, minutesText, msUntil } from '../src/dashboard/format.js'

test('countdowns', () => {
  assert.equal(countdown(3 * 3600_000), '3:00:00')
  assert.equal(countdown(125_000), '2:05')
  assert.equal(countdown(-5000), '0:00') // never negative
  assert.equal(minutesText(95), '1 h 35 min')
  assert.equal(minutesText(9.6), '10 min')
})

test('time left until an API time', () => {
  const now = Date.parse('2026-11-10T01:00:00Z')
  assert.equal(msUntil('2026-11-10T01:10:00Z', now), 10 * 60_000)
  assert.equal(msUntil('2026-11-10T01:10:00', now), 10 * 60_000) // no Z: still UTC
})

test('free PCs are held only for people with an open offer', () => {
  const now = Date.parse('2026-11-10T01:00:00Z')
  const waitlist = [
    { firstName: 'Hana', computerReadyUntil: '2026-11-10T01:04:00Z' }, // offer open
    { firstName: 'Ivan', computerReadyUntil: null },                   // just waiting
    { firstName: 'Jo', computerReadyUntil: '2026-11-10T00:59:00Z' },   // offer ran out
  ]
  assert.deepEqual(heldForWaitlist(waitlist, now).map((w) => w.firstName), ['Hana'])
})
