import { noteServerDate } from './time.js'

// All calls to the C# API live here, so components never build URLs themselves.
// Empty = same address as the page (the Vite dev server forwards /api to the API).
const BASE = import.meta.env.VITE_API_URL || ''

async function request(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })
  noteServerDate(res.headers.get('Date')) // keeps countdowns on the server's clock

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
    // 502 comes from the dev server when it cannot reach the API (see vite.config.js).
    error.unreachable = res.status === 502
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
  changePassword: (currentPassword, newPassword) =>
    send('POST', '/api/auth/password', { currentPassword, newPassword }),

  getComputers: () => request('/api/computers'),
  getComputerSessions: (computerId, limit = 50) =>
    request(`/api/computers/${computerId}/sessions?limit=${limit}`),
  getActiveSessions: () => request('/api/sessions/active'),
  getSessions: (params) => request(`/api/sessions${query(params)}`),
  endSession: (sessionId) => send('POST', `/api/sessions/${sessionId}/end`),
  extendSession: (sessionId) => send('POST', `/api/sessions/${sessionId}/extend`),

  // Help desk: the QR holds the visit token; the 6-digit code is the backup.
  lookupPass: (pass) => send('POST', '/api/passes/lookup', pass),
  checkIn: (pass) => send('POST', '/api/passes/check-in', pass),
  getPasses: (status) => request(`/api/passes${query({ status })}`),

  // Staff register a visitor at the desk. With computerId the session starts
  // right away; without it the visit is only logged.
  walkIn: (registration) => send('POST', '/api/visits/walk-in', registration),
  getVisits: (params) => request(`/api/visits${query(params)}`),
  getWaitlist: () => request('/api/waitlist'),

  getProblems: (params) => request(`/api/problems${query(params)}`),
  openProblem: (computerId, description) => send('POST', '/api/problems', { computerId, description }),
  resolveProblem: (problemId, notes) => send('PUT', `/api/problems/${problemId}/resolve`, { notes }),

  setPeripheralInUse: (peripheralId, inUse) =>
    send('PUT', `/api/peripherals/${peripheralId}/use`, { inUse }),
  createAssist: (computerId, message) => send('POST', '/api/assist', { computerId, message }),
  getAssist: () => request('/api/assist'),
  setAssistStatus: (ticketId, status) =>
    send('PUT', `/api/assist/${ticketId}/status`, { status }),

  // Reports: counts for a range of Philippine days (YYYY-MM-DD, both included).
  getReport: (from, to) => request(`/api/reports/summary${query({ from, to })}`),
  // Downloads an export (xlsx or csv) and hands it to the browser as a file.
  downloadReport: (params) => download(`/api/reports/export${query(params)}`),

  // Monthly report draft by Gemini (admin). Only the period's counts are sent to Google.
  getAssistantStatus: () => request('/api/reports/assistant'),
  draftReport: (from, to) => send('POST', '/api/reports/assistant', { from, to }),

  // ---- Admin only ----
  getStaff: () => request('/api/staff'),
  createStaff: (account) => send('POST', '/api/staff', account),
  updateStaff: (staffId, changes) => send('PUT', `/api/staff/${staffId}`, changes),
  setStaffPassword: (staffId, password) => send('POST', `/api/staff/${staffId}/password`, { password }),
  unlockStaff: (staffId) => send('POST', `/api/staff/${staffId}/unlock`),
  getAudit: (params) => request(`/api/audit${query(params)}`),
  addComputer: (name) => send('POST', '/api/computers', { name }),
  renameComputer: (computerId, name) => send('PUT', `/api/computers/${computerId}`, { name }),
  removeComputer: (computerId) => send('DELETE', `/api/computers/${computerId}`),
  createPairingCode: (computerId) => send('POST', `/api/computers/${computerId}/pairing-code`),
  unpairComputer: (computerId) => send('POST', `/api/computers/${computerId}/unpair`),
}

// Fetches a file (sign-in cookie included) and saves it under the name the API gives it.
async function download(path) {
  const res = await fetch(`${BASE}${path}`)
  noteServerDate(res.headers.get('Date'))
  if (!res.ok) {
    let message = `Download failed (${res.status})`
    try {
      message = (await res.json()).message ?? message
    } catch {
      /* no JSON body */
    }
    const error = new Error(message)
    error.status = res.status
    throw error
  }
  const name = /filename="?([^";]+)"?/.exec(res.headers.get('Content-Disposition') ?? '')?.[1] ?? 'report'
  const url = URL.createObjectURL(await res.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = name
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10000)
}

// True when the request never reached the API (it is down or still starting).
export const isUnreachable = (e) => e instanceof TypeError || e?.unreachable === true

// { a: 1, b: '', c: null } -> "?a=1" (empty values are left out)
function query(params = {}) {
  const q = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') q.set(k, v)
  const text = q.toString()
  return text ? `?${text}` : ''
}
