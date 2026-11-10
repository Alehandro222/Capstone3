import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { parseUtc, serverNow } from '../time.js'
import { dateTime } from './format.js'
import { Badge, FormDialog } from './ui.jsx'

const MIN_PASSWORD = 8

// Admin: create staff accounts, change role / active, set a new password, clear a lockout.
// Accounts are never deleted (the audit log must keep saying who did what): deactivate instead.
export default function StaffAccounts({ me, run }) {
  const [accounts, setAccounts] = useState(null)
  const [error, setError] = useState('')
  const [dialog, setDialog] = useState(null) // { type: 'create' | 'edit' | 'password', account }

  const load = useCallback(async () => {
    try {
      setAccounts(await api.getStaff())
      setError('')
    } catch (e) {
      setError(e.message)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  // run() refreshes the board; reload the list too.
  const act = async (action, message) => {
    const ok = await run(action, message)
    if (ok) await load()
    return ok
  }

  const locked = (a) => a.lockedUntil && parseUtc(a.lockedUntil).getTime() > serverNow()

  return (
    <section className="stack">
      <div className="toolbar">
        <h2>Staff accounts</h2>
        <button className="btn btn-primary" onClick={() => setDialog({ type: 'create' })}>
          Add staff account
        </button>
      </div>
      {error && <p className="form-error">{error}</p>}
      {accounts === null ? (
        <p className="muted">Loading…</p>
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Username</th>
                <th>Role</th>
                <th>Status</th>
                <th>Last sign-in</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {accounts.map((a) => (
                <tr key={a.id} className={a.isActive ? '' : 'row-dim'}>
                  <td>
                    {a.fullName}
                    {a.id === me.id && <span className="muted small"> (you)</span>}
                    {(a.email || a.mobile) && (
                      <div className="muted small">{[a.email, a.mobile].filter(Boolean).join(' · ')}</div>
                    )}
                  </td>
                  <td>{a.username}</td>
                  <td>{a.role}</td>
                  <td>
                    {!a.isActive ? (
                      <Badge tone="offline">Deactivated</Badge>
                    ) : locked(a) ? (
                      <Badge tone="broken">Locked</Badge>
                    ) : (
                      <Badge tone="available">Active</Badge>
                    )}
                  </td>
                  <td>{a.lastLoginAt ? dateTime(a.lastLoginAt) : 'Never'}</td>
                  <td className="cell-actions">
                    <button className="btn btn-small" onClick={() => setDialog({ type: 'edit', account: a })}>
                      Edit
                    </button>
                    <button className="btn btn-small" onClick={() => setDialog({ type: 'password', account: a })}>
                      Set password
                    </button>
                    {locked(a) && (
                      <button className="btn btn-small" onClick={() => act(() => api.unlockStaff(a.id), `${a.username} unlocked`)}>
                        Unlock
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {dialog?.type === 'create' && (
        <CreateDialog
          onCancel={() => setDialog(null)}
          onSave={async (account) => {
            if (await act(() => api.createStaff(account), `Account ${account.username} created`)) setDialog(null)
          }}
        />
      )}
      {dialog?.type === 'edit' && (
        <EditDialog
          account={dialog.account}
          isMe={dialog.account.id === me.id}
          onCancel={() => setDialog(null)}
          onSave={async (changes) => {
            if (await act(() => api.updateStaff(dialog.account.id, changes), `${dialog.account.username} saved`)) setDialog(null)
          }}
        />
      )}
      {dialog?.type === 'password' && (
        <PasswordDialog
          account={dialog.account}
          onCancel={() => setDialog(null)}
          onSave={async (password) => {
            if (await act(() => api.setStaffPassword(dialog.account.id, password), `New password set for ${dialog.account.username}`))
              setDialog(null)
          }}
        />
      )}
    </section>
  )
}

function Field({ label, children, hint }) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
      {hint && <span className="muted small">{hint}</span>}
    </label>
  )
}

function CreateDialog({ onSave, onCancel }) {
  const [f, setF] = useState({ username: '', fullName: '', role: 'Staff', password: '', email: '', mobile: '' })
  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  const ok = f.username.trim() && f.fullName.trim() && f.password.length >= MIN_PASSWORD

  return (
    <FormDialog title="Add staff account" submitLabel="Create account" canSubmit={ok} onCancel={onCancel}
      onSubmit={() => onSave({ ...f, email: f.email || null, mobile: f.mobile || null })}>
      <div className="form-row">
        <Field label="Full name">
          <input value={f.fullName} onChange={set('fullName')} autoFocus />
        </Field>
        <Field label="Username">
          <input value={f.username} onChange={set('username')} autoComplete="off" />
        </Field>
      </div>
      <Field label="Role" hint="Admins also manage accounts and PCs, and read the audit log.">
        <select value={f.role} onChange={set('role')}>
          <option value="Staff">Staff</option>
          <option value="Admin">Admin</option>
        </select>
      </Field>
      <Field label="First password" hint={`At least ${MIN_PASSWORD} characters. Give it to them in person.`}>
        <input type="password" value={f.password} onChange={set('password')} autoComplete="new-password" />
      </Field>
      <div className="form-row">
        <Field label="Email (optional)">
          <input type="email" value={f.email} onChange={set('email')} />
        </Field>
        <Field label="Mobile (optional)">
          <input value={f.mobile} onChange={set('mobile')} />
        </Field>
      </div>
    </FormDialog>
  )
}

function EditDialog({ account, isMe, onSave, onCancel }) {
  const [f, setF] = useState({
    fullName: account.fullName,
    role: account.role,
    isActive: account.isActive,
    email: account.email ?? '',
    mobile: account.mobile ?? '',
  })
  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })

  return (
    <FormDialog title={`Edit ${account.username}`} submitLabel="Save" canSubmit={!!f.fullName.trim()} onCancel={onCancel}
      description="Changing the role or deactivating signs them out straight away."
      onSubmit={() => onSave({ ...f, email: f.email || null, mobile: f.mobile || null })}>
      <Field label="Full name">
        <input value={f.fullName} onChange={set('fullName')} autoFocus />
      </Field>
      <Field label="Role">
        <select value={f.role} onChange={set('role')}>
          <option value="Staff">Staff</option>
          <option value="Admin">Admin</option>
        </select>
      </Field>
      <label className="choice">
        <input type="checkbox" checked={f.isActive} onChange={(e) => setF({ ...f, isActive: e.target.checked })} />
        <span>Active (can sign in)</span>
      </label>
      {isMe && (!f.isActive || f.role !== 'Admin') && (
        <p className="form-error">This is your own account: you will be signed out.</p>
      )}
      <div className="form-row">
        <Field label="Email">
          <input type="email" value={f.email} onChange={set('email')} />
        </Field>
        <Field label="Mobile">
          <input value={f.mobile} onChange={set('mobile')} />
        </Field>
      </div>
    </FormDialog>
  )
}

function PasswordDialog({ account, onSave, onCancel }) {
  const [password, setPassword] = useState('')
  return (
    <FormDialog title={`New password for ${account.username}`} submitLabel="Set password"
      canSubmit={password.length >= MIN_PASSWORD} onCancel={onCancel} onSubmit={() => onSave(password)}
      description="For a forgotten password. Give it to them in person; they can change it after signing in.">
      <Field label="New password" hint={`At least ${MIN_PASSWORD} characters.`}>
        <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" autoFocus />
      </Field>
    </FormDialog>
  )
}
