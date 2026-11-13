// Re-fetching data every few seconds, without wasting requests.
//
// Hosted, every request goes through Netlify, whose free plan counts them. So:
//  - nothing is fetched while the page is hidden (another tab, a minimised window, a locked
//    phone); it fetches at once when the page is shown again;
//  - a new fetch never starts while the last one is still waiting (a slow or waking API).
//
// Returns a function that stops it. `doc` is only passed by tests.
export function startPolling(load, ms, doc = globalThis.document) {
  let timer = null
  let busy = false
  const hidden = () => doc?.visibilityState === 'hidden'

  async function tick() {
    if (busy || hidden()) return
    busy = true
    try {
      await load()
    } catch {
      /* the screens show their own errors */
    } finally {
      busy = false
    }
  }

  function start() {
    if (timer === null) timer = setInterval(tick, ms)
  }
  function stop() {
    clearInterval(timer)
    timer = null
  }
  function onVisibility() {
    if (hidden()) stop()
    else {
      tick()
      start()
    }
  }

  doc?.addEventListener?.('visibilitychange', onVisibility)
  if (!hidden()) {
    tick()
    start()
  }
  return () => {
    stop()
    doc?.removeEventListener?.('visibilitychange', onVisibility)
  }
}
