// Report periods (Reports tab). Run: npm test
import assert from 'node:assert/strict'
import { test } from 'node:test'
import { dayLabel, hourLabel, monthStart, periodRange, shiftDay, weekdayIndex } from '../src/dashboard/periods.js'

test('shifting days crosses months, years and leap days', () => {
  assert.equal(shiftDay('2026-11-30', 1), '2026-12-01')
  assert.equal(shiftDay('2027-01-01', -1), '2026-12-31')
  assert.equal(shiftDay('2028-03-01', -1), '2028-02-29') // leap year
  assert.equal(shiftDay('2026-11-10', 0), '2026-11-10')
})

test('weeks start on Monday', () => {
  assert.equal(weekdayIndex('2026-11-09'), 0) // Monday
  assert.equal(weekdayIndex('2026-11-15'), 6) // Sunday
})

test('each period button gives the right days', () => {
  const tue = '2026-11-10'
  assert.deepEqual(periodRange('today', tue), [tue, tue])
  assert.deepEqual(periodRange('yesterday', tue), ['2026-11-09', '2026-11-09'])
  assert.deepEqual(periodRange('week', tue), ['2026-11-09', tue])
  assert.deepEqual(periodRange('week', '2026-11-15'), ['2026-11-09', '2026-11-15']) // Sunday ends the week
  assert.deepEqual(periodRange('month', tue), ['2026-11-01', tue])
  assert.deepEqual(periodRange('lastMonth', tue), ['2026-10-01', '2026-10-31'])
})

test('last month in January is December of the year before', () => {
  assert.deepEqual(periodRange('lastMonth', '2027-01-05'), ['2026-12-01', '2026-12-31'])
  assert.deepEqual(periodRange('lastMonth', '2028-03-15'), ['2028-02-01', '2028-02-29'])
  assert.equal(monthStart('2026-11-30'), '2026-11-01')
})

test('labels', () => {
  assert.equal(dayLabel('2026-11-08'), 'Nov 8')
  assert.equal(dayLabel('2027-01-01'), 'Jan 1') // never shifted to Dec 31 by a time zone
  assert.deepEqual(['0', '9', '12', '13', '23'].map(hourLabel), ['12 AM', '9 AM', '12 PM', '1 PM', '11 PM'])
})
