// Small building blocks of the visitor phone app (from the Figma Make design).

export function ArrowLeftIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m15 18-6-6 6-6" /></svg>
}

export function ComputerIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <rect x="3" y="4" width="18" height="13" rx="2" />
      <path d="M8 21h8M12 17v4" />
    </svg>
  )
}

export function ClockIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </svg>
  )
}

export function CheckIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6" /></svg>
}

export function AlertIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v6M12 17h.01" />
    </svg>
  )
}

export function Logo({ large = false }) {
  return (
    <div className={`v-logo ${large ? 'v-logo--large' : ''}`} role="img" aria-label="MISA">
      <span className="v-logo__mark">
        <span className="v-logo__screen" />
        <span className="v-logo__book" />
      </span>
      <span>MISA</span>
    </div>
  )
}

export function Button({ children, onClick, variant = 'primary', disabled = false, type = 'button' }) {
  return (
    <button className={`v-button v-button--${variant}`} disabled={disabled} onClick={onClick} type={type}>
      {children}
    </button>
  )
}

export function Shell({ children, className = '' }) {
  return <main className={`v-phone ${className}`}>{children}</main>
}

// Top bar with a back button, a title and the step.
export function Header({ title, step, onBack }) {
  return (
    <header className="v-topbar">
      {onBack ? (
        <button className="v-icon-button" onClick={onBack} aria-label="Go back">
          <ArrowLeftIcon />
        </button>
      ) : (
        <span style={{ width: 44 }} />
      )}
      <div className="v-topbar__title">
        <strong>{title}</strong>
        {step && <span>{step}</span>}
      </div>
      <Logo />
    </header>
  )
}

// Logo on the left, a status pill on the right.
export function SimpleHeader({ children }) {
  return (
    <header className="v-header">
      <Logo />
      {children}
    </header>
  )
}

export function ErrorAlert({ message }) {
  if (!message) return null
  return (
    <p className="v-alert v-alert--error" role="alert">
      {message}
    </p>
  )
}
