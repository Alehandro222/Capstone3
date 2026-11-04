// All calls to the C# API live here, so components never build URLs themselves.
const BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5080'

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
  getComputers: () => request('/api/computers'),
  getComputer: (id) => request(`/api/computers/${id}`),
  getActiveSessions: () => request('/api/sessions/active'),
  startSession: (computerId, userName, purpose) =>
    send('POST', '/api/sessions/start', { computerId, userName, purpose }),
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
