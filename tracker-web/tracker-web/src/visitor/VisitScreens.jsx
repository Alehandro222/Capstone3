import { useState } from 'react'
import { QRCodeSVG } from 'qrcode.react'
import { AlertIcon, Button, CheckIcon, ClockIcon, ComputerIcon, ErrorAlert, Logo, Shell, SimpleHeader } from './ui.jsx'
import { formatClock, formatDuration, msUntil, useNow } from './useClock.js'

// Runs an action, showing its error message under the buttons.
function useAction() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  async function run(action) {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      await action()
    } catch (e) {
      setError(e instanceof TypeError ? 'Cannot reach the library system. Check the Wi-Fi.' : e.message)
    } finally {
      setBusy(false)
    }
  }
  return { busy, error, run }
}

// ---- Welcome ------------------------------------------------------------------

export function WelcomeScreen({ free, total, onStart, onBrowse }) {
  return (
    <Shell className="v-welcome">
      <div className="v-welcome__content">
        <div className="v-welcome__art" aria-hidden="true">
          <div className="v-welcome__ring" />
          <ComputerIcon />
          <span className="v-welcome__check"><CheckIcon /></span>
        </div>
        <Logo large />
        <h1>Log your visit.<br />Pick a computer.</h1>
        <p>Welcome to the DICT Digital Transformation Center in Baguio City.</p>
        {total > 0 && (
          <button className="v-availability-chip" type="button" onClick={onBrowse}>
            <i /><strong>{free} of {total}</strong> computers available <span>View PCs</span>
          </button>
        )}
      </div>
      <div className="v-actions">
        <Button onClick={onStart}>Log my visit</Button>
        <p className="v-small">No account needed. For help, ask the DTC staff on duty.</p>
      </div>
    </Shell>
  )
}

// ---- Registered, no computer (yet) ---------------------------------------------------

export function VisitLoggedScreen({ visit, expired, onChooseComputer, onDone, onRegisterAgain }) {
  return (
    <Shell>
      <SimpleHeader><span className="v-pill"><CheckIcon /> Logged in</span></SimpleHeader>
      <div className="v-body v-message">
        <div className="v-visual v-visual--ok"><CheckIcon /></div>
        <div>
          <p className="v-eyebrow">{expired ? 'Registration expired' : 'Visit logged'}</p>
          <h1>{expired ? 'Please log your visit again' : `Thank you, ${visit.firstName}!`}</h1>
          <p className="v-supporting">
            {expired
              ? 'Your registration is from an earlier visit.'
              : 'Your visit is recorded. If you would like to use a computer, pick one below.'}
          </p>
        </div>
        <div className="v-spacer" />
        <div className="v-actions">
          {expired ? (
            <Button onClick={onRegisterAgain}>Log my visit</Button>
          ) : (
            <>
              <Button onClick={onChooseComputer}>I need a computer</Button>
              <Button variant="secondary" onClick={onDone}>Done</Button>
            </>
          )}
        </div>
      </div>
    </Shell>
  )
}

// ---- The QR pass ------------------------------------------------------------------

export function PassScreen({ token, visit, onChooseAgain, onCancel }) {
  const now = useNow()
  const { busy, error, run } = useAction()
  const pass = visit.pass
  const left = msUntil(pass.expiresAt, now)

  return (
    <Shell className="v-pass-screen">
      <SimpleHeader><span className="v-pill"><CheckIcon /> QR pass ready</span></SimpleHeader>
      <div className="v-body">
        <div className="v-success">
          <span><CheckIcon /></span>
          <div>
            <p className="v-eyebrow">{pass.computerName} reserved</p>
            <h1>Show this at the help desk</h1>
          </div>
        </div>

        <div className="v-pass">
          <div className="v-pass__top">
            <div className="v-qr">
              {/* The QR holds the visit's secret token; staff scan it to check you in. */}
              <QRCodeSVG value={token} size={174} level="M" marginSize={2} title={`Pass for ${pass.computerName}`} />
            </div>
            <p className="v-pass__code">If the QR will not scan, say this code: <strong>{pass.code}</strong></p>
            <p>A staff member scans it to unlock <span className="v-nowrap">{pass.computerName}</span>.</p>
          </div>
          <div className="v-pass__details">
            <div><span>Computer</span><strong>{pass.computerName}</strong></div>
            <div><span>Name</span><strong>{visit.firstName}</strong></div>
            <div><span>Valid for</span><strong>{formatDuration(left)}</strong></div>
          </div>
        </div>

        <div className="v-steps">
          <span>1</span><p>Go to the help desk</p>
          <i /><div />
          <span>2</span><p>Let the staff scan your QR pass</p>
          <i /><div />
          <span>3</span><p>{pass.computerName} unlocks. This page will show your time.</p>
        </div>

        <ErrorAlert message={error} />
        <div className="v-spacer" />
        <div className="v-actions">
          <Button variant="secondary" onClick={onChooseAgain} disabled={busy}>Choose a different PC</Button>
          <button className="v-link" type="button" disabled={busy} onClick={() => run(onCancel)}>
            Cancel, I don't need a computer
          </button>
        </div>
      </div>
    </Shell>
  )
}

// ---- The session (time left) ----------------------------------------------------------

export function SessionScreen({ visit, onExtend, onEnd }) {
  const now = useNow()
  const { busy, error, run } = useAction()
  const [confirmEnd, setConfirmEnd] = useState(false)
  const session = visit.session
  const computer = visit.pass?.computerName ?? `Computer ${session.computerId}`

  const left = msUntil(session.scheduledEndAt, now)
  const total = Math.max(1, msUntil(session.scheduledEndAt, 0) - msUntil(session.startedAt, 0))
  const percent = Math.round((left / total) * 100)
  const endingSoon = left <= 10 * 60 * 1000

  return (
    <Shell className="v-session-screen">
      <SimpleHeader><span className="v-pill"><i /> Active session</span></SimpleHeader>
      <div className="v-body v-center">
        <div>
          <p className="v-eyebrow">{computer}</p>
          <h1>Time left</h1>
        </div>

        <div className="v-ring"
          style={{ background: `conic-gradient(var(--v-blue-600) ${percent}%, var(--v-blue-100) 0)` }}>
          <div className="v-ring__inner">
            <strong>{formatDuration(left)}</strong>
            <span>until {formatClock(session.scheduledEndAt)}</span>
          </div>
        </div>

        {endingSoon && (
          <p className="v-alert v-alert--info">
            Your session ends soon. Save your work.
            {!session.canExtend && session.cannotExtendReason && ` ${session.cannotExtendReason}`}
          </p>
        )}

        <div className="v-info">
          <div><span>Computer</span><strong>{computer}</strong></div>
          <div><span>Started</span><strong>{formatClock(session.startedAt)}</strong></div>
        </div>

        <ErrorAlert message={error} />
        <div className="v-spacer" />
        <div className="v-actions">
          {session.canExtend && (
            <Button onClick={() => run(onExtend)} disabled={busy}>Extend by 1 hour</Button>
          )}
          {confirmEnd ? (
            <>
              <p className="v-small">End your session? Save your work first.</p>
              <Button variant="danger" onClick={() => run(onEnd)} disabled={busy}>Yes, end my session</Button>
              <Button variant="secondary" onClick={() => setConfirmEnd(false)}>Keep using it</Button>
            </>
          ) : (
            <Button variant="danger" onClick={() => setConfirmEnd(true)}>End session</Button>
          )}
        </div>
      </div>
    </Shell>
  )
}

// ---- Waitlist ---------------------------------------------------------------------------

export function WaitlistScreen({ visit, onChooseComputer, onLeave }) {
  const now = useNow()
  const { busy, error, run } = useAction()
  const waitlist = visit.waitlist
  const readyLeft = msUntil(waitlist.computerReadyUntil, now)
  const ready = waitlist.computerReadyUntil && readyLeft > 0

  return (
    <Shell>
      <SimpleHeader>
        <span className={`v-pill ${ready ? '' : 'v-pill--orange'}`}>{ready ? 'Your turn' : 'On the waitlist'}</span>
      </SimpleHeader>
      <div className="v-body v-message">
        <div className={`v-visual ${ready ? 'v-visual--ok' : 'v-visual--blue'}`}>{ready ? <CheckIcon /> : <ClockIcon />}</div>
        <div>
          <p className="v-eyebrow">{ready ? 'A computer is ready' : 'Waitlist'}</p>
          <h1>{ready ? `It's your turn, ${visit.firstName}!` : `You are number ${waitlist.position} in line`}</h1>
          <p className="v-supporting">
            {ready
              ? 'A computer is being held for you. Pick it before the time runs out.'
              : 'Keep this page open. It will tell you when a computer is ready for you.'}
          </p>
        </div>

        {ready ? (
          <div className="v-card v-card--ready">
            <ClockIcon />
            <div><span>Pick a computer within</span><strong>{formatDuration(readyLeft)}</strong></div>
          </div>
        ) : (
          <div className="v-card">
            <ClockIcon />
            <div><span>You can wait until</span><strong>{formatClock(waitlist.expiresAt)}</strong></div>
          </div>
        )}

        <ErrorAlert message={error} />
        <div className="v-spacer" />
        <div className="v-actions">
          {ready && <Button onClick={onChooseComputer}>Pick my computer</Button>}
          <Button variant="secondary" onClick={() => run(onLeave)} disabled={busy}>Leave the waitlist</Button>
        </div>
      </div>
    </Shell>
  )
}

// ---- Short messages (session ended, pass expired, missed turn) ----------------------------

const ENDED_TEXT = {
  Visitor: 'You ended your session.',
  Staff: 'A staff member ended your session.',
  Idle: 'You were logged out because the computer was not used for a while.',
  TimeLimit: 'Your time is up.',
  System: 'Your session has ended.',
}

export function EndedScreen({ visit, onDone }) {
  return (
    <MessageScreen
      ok
      eyebrow="Session ended"
      title="Thanks for using the DTC computers"
      text={`${ENDED_TEXT[visit.session.endReason] ?? ENDED_TEXT.System} Your visit has been recorded.`}
      action="Done"
      onAction={onDone}
    />
  )
}

export function MessageScreen({ ok, eyebrow, title, text, action, onAction, secondary, onSecondary }) {
  return (
    <Shell>
      <SimpleHeader><span className="v-small">DTC Baguio</span></SimpleHeader>
      <div className="v-body v-message">
        <div className={`v-visual ${ok ? 'v-visual--ok' : ''}`}>{ok ? <CheckIcon /> : <AlertIcon />}</div>
        <div>
          <p className="v-eyebrow">{eyebrow}</p>
          <h1>{title}</h1>
          <p className="v-supporting">{text}</p>
        </div>
        <div className="v-spacer" />
        <div className="v-actions">
          <Button onClick={onAction}>{action}</Button>
          {secondary && <Button variant="secondary" onClick={onSecondary}>{secondary}</Button>}
        </div>
      </div>
    </Shell>
  )
}
