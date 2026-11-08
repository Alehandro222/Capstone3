import { useState } from 'react'
import {
  AGE_GROUPS,
  PRIVACY_STATEMENT,
  SECTORS,
  SERVICES,
  SEXES,
  emptyRegistration,
  registrationProblem,
} from '../registration.js'
import { Button, CheckIcon, ErrorAlert, Header, Shell } from './ui.jsx'

// The DTC digital login form, on the visitor's phone.
export default function RegisterScreen({ onBack, onSubmit }) {
  const [form, setForm] = useState(emptyRegistration)
  const [tried, setTried] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const problem = registrationProblem(form)
  const set = (field, value) => setForm((f) => ({ ...f, [field]: value }))
  const toggle = (field, value) =>
    setForm((f) => ({
      ...f,
      [field]: f[field].includes(value) ? f[field].filter((v) => v !== value) : [...f[field], value],
    }))

  async function submit(event) {
    event.preventDefault()
    setTried(true)
    if (problem || busy) return
    setBusy(true)
    setError('')
    try {
      await onSubmit({ ...form, firstName: form.firstName.trim(), lastName: form.lastName.trim() })
    } catch (e) {
      setError(e instanceof TypeError ? 'Cannot reach the library system. Check the Wi-Fi and try again.' : e.message)
      setBusy(false)
    }
  }

  const missing = (field) => tried && (Array.isArray(form[field]) ? form[field].length === 0 : !form[field].trim?.() && !form[field])

  return (
    <Shell>
      <Header title="Your details" step="Digital login" onBack={onBack} />
      <form className="v-body" onSubmit={submit} noValidate>
        <div>
          <p className="v-eyebrow">Welcome to DTC Baguio</p>
          <h1>Log your visit</h1>
          <p className="v-supporting">Please fill in every part. Log in each time you visit.</p>
        </div>

        <label className={`v-field ${missing('firstName') ? 'v-field--error' : ''}`}>
          <span>First name</span>
          <input value={form.firstName} onChange={(e) => set('firstName', e.target.value)}
            placeholder="First name" autoComplete="given-name" maxLength={100} />
        </label>
        <label className={`v-field ${missing('lastName') ? 'v-field--error' : ''}`}>
          <span>Last name</span>
          <input value={form.lastName} onChange={(e) => set('lastName', e.target.value)}
            placeholder="Last name" autoComplete="family-name" maxLength={100} />
        </label>

        <Options legend="Age" type="radio" name="age" options={AGE_GROUPS}
          isChecked={(v) => form.ageGroup === v} onChange={(v) => set('ageGroup', v)} error={missing('ageGroup')} />
        <Options legend="Sex" type="radio" name="sex" options={SEXES} twoColumns
          isChecked={(v) => form.sex === v} onChange={(v) => set('sex', v)} error={missing('sex')} />
        <Options legend="Sector" hint="Select all that apply" type="checkbox" name="sector" options={SECTORS}
          isChecked={(v) => form.sectors.includes(v)} onChange={(v) => toggle('sectors', v)} error={missing('sectors')} />
        <Options legend="Purpose / services availed" hint="Select all that apply" type="checkbox" name="service"
          options={SERVICES} isChecked={(v) => form.services.includes(v)} onChange={(v) => toggle('services', v)}
          error={missing('services')} />

        <div className="v-privacy">
          <span><CheckIcon /></span>
          <div>
            {/* The form's consent line says "the above statement"; on the phone the statement is folded below. */}
            <strong>Your privacy matters.</strong> By submitting this form, you agree to the DICT-CAR privacy
            statement and consent to the use and disclosure of your information as described.
            <details>
              <summary>Read the privacy statement</summary>
              <p>{PRIVACY_STATEMENT}</p>
            </details>
          </div>
        </div>

        {tried && problem && <p className="v-alert v-alert--error">{problem}</p>}
        <ErrorAlert message={error} />

        <div className="v-spacer" />
        <Button type="submit" disabled={busy}>{busy ? 'Submitting…' : 'Submit'}</Button>
      </form>
    </Shell>
  )
}

function Options({ legend, hint, type, name, options, isChecked, onChange, error, twoColumns }) {
  return (
    <fieldset className={`v-options ${error ? 'v-options--error' : ''}`}>
      <legend>
        {legend}
        {hint && <em>{hint}</em>}
      </legend>
      <div className={`v-options__list ${twoColumns ? 'v-options__list--two' : ''}`}>
        {options.map((o) => (
          <label key={o.value}>
            <input type={type} name={name} checked={isChecked(o.value)} onChange={() => onChange(o.value)} />
            <span>{o.label}</span>
          </label>
        ))}
      </div>
    </fieldset>
  )
}
