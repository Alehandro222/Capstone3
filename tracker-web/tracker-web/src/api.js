// All calls to the C# API live here, so components never build URLs themselves.
// Empty = same address as the page (the Vite dev server forwards /api to the API).
const BASE = import.meta.env.VITE_API_URL || ''

async function request(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })

  if (!res.ok) {
    // The API returns { message: "..." } for validation errors.
    let message = `Request failed (${res.status})`
    try {
      const body = await res.json()
      if (body?.message) message = body.message
    } catch {
      /* response had no JSON body */
    }
    const error = new Error(message)
    error.status = res.status // lets callers tell a 404 from a 500
    throw error
  }

  // 204 No Content (e.g. status update) has no body.
  return res.status === 204 ? null : res.json()
}

const send = (method, path, body) =>
  request(path, { method, body: body === undefined ? undefined : JSON.stringify(body) })

export const api = {
  // ---- Visitor phone app (no sign-in; the visit token is the proof) ----
  publicComputers: () => request('/api/public/computers'),
  register: (registration) => send('POST', '/api/visits', registration),
  getVisit: (token) => request(`/api/visits/${encodeURIComponent(token)}`),
  pickComputer: (token, computerId) =>
    send('POST', `/api/visits/${encodeURIComponent(token)}/pass`, { computerId }),
  cancelPass: (token) => send('POST', `/api/visits/${encodeURIComponent(token)}/pass/cancel`),
  endMySession: (token) => send('POST', `/api/visits/${encodeURIComponent(token)}/session/end`),
  extendMySession: (token) => send('POST', `/api/visits/${encodeURIComponent(token)}/session/extend`),
  joinWaitlist: (token) => send('POST', `/api/visits/${encodeURIComponent(token)}/waitlist`),
  leaveWaitlist: (token) => send('DELETE', `/api/visits/${encodeURIComponent(token)}/waitlist`),

  // ---- Staff dashboard ----
  // Staff sign-in. The API answers with an HttpOnly cookie the browser keeps.
  me: () => request('/api/auth/me'),
  login: (username, password) => send('POST', '/api/auth/login', { username, password }),
  logout: () => send('POST', '/api/auth/logout'),

  getComputers: () => request('/api/computers'),
  getActiveSessions: () => request('/api/sessions/active'),
  // Staff register a visitor at the desk. With computerId the session starts
  // right away; without it the visit is only logged.
  walkIn: (registration) => send('POST', '/api/visits/walk-in', registration),
  endSession: (sessionId) => send('POST', `/api/sessions/${sessionId}/end`),
  setComputerStatus: (computerId, status) =>
    send('PUT', `/api/computers/${computerId}/status`, { status }),
  setPeripheralInUse: (peripheralId, inUse) =>
    send('PUT', `/api/peripherals/${peripheralId}/use`, { inUse }),
  createAssist: (computerId, message) => send('POST', '/api/assist', { computerId, message }),
  getAssist: () => request('/api/assist'),
  setAssistStatus: (ticketId, status) =>
    send('PUT', `/api/assist/${ticketId}/status`, { status }),
}
