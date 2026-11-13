// Re-fetching without wasting requests (src/polling.js). Run: npm test
import assert from 'node:assert/strict'
import { mock, test } from 'node:test'
import { startPolling } from '../src/polling.js'

// A stand-in for the browser's document: visible or hidden, with visibilitychange.
function fakeDocument(state = 'visible') {
  const listeners = new Set()
  return {
    visibilityState: state,
    addEventListener: (type, fn) => type === 'visibilitychange' && listeners.add(fn),
    removeEventListener: (type, fn) => listeners.delete(fn),
    set(next) {
      this.visibilityState = next
      listeners.forEach((fn) => fn())
    },
    listenerCount: () => listeners.size,
  }
}

const settle = () => new Promise((resolve) => setImmediate(resolve))

test('fetches now and then every interval while shown', async () => {
  mock.timers.enable({ apis: ['setInterval'] })
  try {
    const doc = fakeDocument()
    let calls = 0
    const stop = startPolling(async () => calls++, 5000, doc)
    assert.equal(calls, 1)
    await settle()
    mock.timers.tick(5000)
    await settle()
    mock.timers.tick(5000)
    await settle()
    assert.equal(calls, 3)
    stop()
    mock.timers.tick(20000)
    await settle()
    assert.equal(calls, 3) // stopped
    assert.equal(doc.listenerCount(), 0)
  } finally {
    mock.timers.reset()
  }
})

test('nothing is fetched while the page is hidden; it catches up when shown', async () => {
  mock.timers.enable({ apis: ['setInterval'] })
  try {
    const doc = fakeDocument('hidden')
    let calls = 0
    const stop = startPolling(async () => calls++, 5000, doc)
    mock.timers.tick(60000)
    await settle()
    assert.equal(calls, 0)

    doc.set('visible')
    assert.equal(calls, 1) // straight away, not after 5 seconds
    await settle()
    mock.timers.tick(5000)
    await settle()
    assert.equal(calls, 2)

    doc.set('hidden')
    mock.timers.tick(60000)
    await settle()
    assert.equal(calls, 2)
    stop()
  } finally {
    mock.timers.reset()
  }
})

test('a slow answer is not asked for again until it arrives', async () => {
  mock.timers.enable({ apis: ['setInterval'] })
  try {
    let calls = 0
    let finish
    const stop = startPolling(
      () => {
        calls++
        return new Promise((resolve) => (finish = resolve))
      },
      1000,
      fakeDocument(),
    )
    mock.timers.tick(5000) // the API is waking up: still waiting
    await settle()
    assert.equal(calls, 1)

    finish()
    await settle()
    mock.timers.tick(1000)
    await settle()
    assert.equal(calls, 2)
    stop()
  } finally {
    mock.timers.reset()
  }
})

test('a failed fetch does not stop the polling', async () => {
  mock.timers.enable({ apis: ['setInterval'] })
  try {
    let calls = 0
    const stop = startPolling(
      async () => {
        calls++
        throw new Error('offline')
      },
      1000,
      fakeDocument(),
    )
    await settle()
    mock.timers.tick(1000)
    await settle()
    assert.equal(calls, 2)
    stop()
  } finally {
    mock.timers.reset()
  }
})

test('works without a document (no browser)', () => {
  let calls = 0
  const stop = startPolling(() => calls++, 1000, undefined)
  assert.equal(calls, 1)
  stop()
})
