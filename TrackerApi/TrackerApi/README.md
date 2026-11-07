# Computer System Tracker: API (C# / ASP.NET Core)

Backend for the library's public computers: visitors register on their phone and pick a PC, staff scan their pass at the help desk, and the session runs on the PC with a time limit.

## How a visit works

1. **Phone:** the visitor fills in the form and picks an available PC → `POST /api/passes`. The PC becomes **Reserved** and the phone gets a pass: a QR token plus a 6-digit backup code, valid for 10 minutes.
2. **Help desk:** staff scan the QR (or type the code) → `POST /api/passes/check-in`. The server checks the pass exists, is not expired or used, and the PC is not offline, then starts a **3-hour session**.
3. **PC:** the kiosk app sees the session and unlocks. During the last 10 minutes the visitor can extend by 1 hour (at most twice), but only if nobody is on the waitlist.
4. **End:** the visitor ends it (phone or PC), staff end it, the kiosk logs out after 20 minutes idle, or the cleanup job ends it at the time limit.

## PC status

Status is **never stored**. The server works it out on every request, so it can't be out of date. The first rule that matches wins:

| Status | When |
|---|---|
| `Offline` | The kiosk app hasn't sent a heartbeat for 30 seconds |
| `InUse` | A session is running |
| `Maintenance` | There is an open problem report |
| `Reserved` | Someone holds an unscanned pass for it |
| `Available` | None of the above |

## Run it

```bash
dotnet --version   # the project targets .NET 8 (net8.0); the .NET 8 runtime must be installed
dotnet tool restore
dotnet run
```

Open http://localhost:5080/swagger to try the endpoints. Most of them need a staff sign-in: call `POST /api/auth/login` in Swagger first, and the browser keeps the cookie.

### The first admin account

No accounts exist at first. Create the first admin from the command line (it asks for the password without showing it):

```bash
dotnet run -- create-admin maria "Maria Santos"
```

Then sign in to the dashboard and create everyone else from there (or with `POST /api/staff`).

The SQLite file `tracker.db` is created on first run with 12 PCs (PC-01 to PC-12), PC-03 under maintenance, and a few peripherals. To reset the data, stop the API and delete `tracker.db`, `tracker.db-shm` and `tracker.db-wal`.

> **Upgrading from the old version:** a `tracker.db` made before migrations were added can't be upgraded. The API stops with a message saying so. Delete the file and start again.

### Heartbeats while developing

In the library, a PC shows as Offline unless its kiosk app is running. That makes testing without the kiosk app awkward, so `appsettings.Development.json` sets `RequireHeartbeat` to `false`: no PC is ever Offline while developing. **The library server must run with `RequireHeartbeat: true`** (the default in `appsettings.json`), for example with `ASPNETCORE_ENVIRONMENT=Production`.

## Who may call what

| Caller | How it proves who it is | What it can use |
|---|---|---|
| Visitor's phone | Nothing to sign in to. Its pass/waitlist **token** is the proof | Public availability, its own pass, the waitlist |
| Kiosk app on a PC | The shared **kiosk key** in the `X-Kiosk-Key` header | Its PC's status, heartbeat, starting/ending/extending sessions, help requests |
| Staff | **Sign-in** with username and password → an HttpOnly cookie | Everything else |
| Admin | Same, with the Admin role | Also staff accounts, adding PCs and the audit log |

Every endpoint needs a staff sign-in **unless it is marked otherwise**, so a new endpoint is protected by default.

- **Passwords** are stored as salted PBKDF2 hashes, never as text. Minimum 8 characters.
- **5 wrong passwords** lock the account for 15 minutes (an admin can unlock it sooner).
- **Deactivating** an account or changing its role signs that person out straight away.
- There must **always be one active admin**: the API refuses to deactivate or demote the last one.
- The **kiosk key** is `Kiosk:ApiKey` in the API settings and `kioskKey` in each PC's `kiosk.settings.json`; they must match. Development uses `dev-kiosk-key`. **The library server needs its own key**, set as the environment variable `Kiosk__ApiKey`.

### Audit log

Every staff and kiosk action is recorded in the `AuditLog` table: who, what, which record, when, and from which IP address. Admins read it with `GET /api/audit`. Rows are never changed or deleted. Recorded actions:

- `auth.*`: sign-ins, failed sign-ins, sign-outs, password changes
- `staff.*`: account changes
- `pass.lookup`: staff **viewed** a visitor's details (Data Privacy Act)
- `pass.check_in`, `pass.walk_in`
- `session.start`, `session.end`, `session.extend`
- `problem.*`, `computer.*`, `peripheral.*`, `assist.status`

## Settings (`appsettings.json`, section `Tracker`)

| Setting | Meaning | Default |
|---|---|---|
| `PassLifetimeMinutes` | Time to get the pass scanned | `10` |
| `SessionMinutes` | Session length | `180` |
| `ExtensionMinutes` | Time added per extension | `60` |
| `MaxExtensions` | Extensions allowed per session | `2` |
| `ExtendWindowMinutes` | Extending is allowed only in the last N minutes | `10` |
| `OfflineAfterSeconds` | No heartbeat for this long → Offline | `30` |
| `RequireHeartbeat` | `false` = never Offline (development only) | `true` |
| `WaitlistLifetimeMinutes` | Waitlist entries are dropped after this long | `120` |
| `CleanupIntervalSeconds` | How often passes expire and overdue sessions end | `15` |

The 20-minute idle logout is set in the kiosk app (`idleTimeoutSeconds` in `kiosk.settings.json`), because only the PC knows when its keyboard and mouse were last used.

## Endpoints

Errors always come back as `{ "message": "..." }`, written so the frontend can show them as they are.

### Visitor's phone

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/public/computers` | Live availability (no names or personal data) |
| POST | `/api/passes` | Register and reserve a PC → returns `token` (keep it!) and the pass |
| GET | `/api/passes/{token}` | Pass status; includes the session once checked in (poll this) |
| POST | `/api/passes/{token}/cancel` | Choose a different PC |
| POST | `/api/passes/{token}/extend` | Extend the session |
| POST | `/api/passes/{token}/end` | End the session |
| POST | `/api/waitlist` | Join the waitlist (only when no PC is free) → returns `token` |
| GET | `/api/waitlist/{token}` | Place in line, and how many PCs are free |
| DELETE | `/api/waitlist/{token}` | Leave the line |

### Staff

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/computers` | Dashboard: status, current user, session end time, open problem, peripherals |
| GET | `/api/computers/{id}/sessions` | Session history for one PC |
| POST | `/api/computers` | Add a PC (admin) |
| PUT | `/api/computers/{id}/status` | `Maintenance` opens a problem report, `Available` resolves them (for the current dashboard) |
| POST | `/api/passes/lookup` | `{ token }` or `{ code }` → who the pass belongs to |
| POST | `/api/passes/check-in` | `{ token }` or `{ code }` → validates and starts the session |
| POST | `/api/passes/walk-in` | Visitor without a phone: register and start right away |
| GET | `/api/passes` | Recent passes (`?status=Pending`) |
| GET | `/api/sessions/active` | Running sessions |
| GET | `/api/sessions` | History (`?computerId=`, `?from=`, `?to=`, `?limit=`) |
| POST | `/api/sessions/{id}/end` | End a session; optional body `{ "reason": "Staff" }` |
| POST | `/api/sessions/{id}/extend` | Extend a session |
| GET / POST | `/api/problems` | List (`?status=Open&computerId=`) / open a problem report |
| PUT | `/api/problems/{id}/resolve` | Resolve it, with optional `{ "notes": "..." }` |
| GET | `/api/waitlist` | Everyone waiting, in order |
| GET / POST / PUT | `/api/assist`, `/api/peripherals` | Help requests and peripherals (unchanged) |

### Kiosk app on each PC (kiosk key; staff may call these too)

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/computers/{id}/heartbeat` | "This PC is on" (every few seconds) |
| GET | `/api/computers`, `/api/computers/{id}`, `/api/sessions/active` | Its own status, and the setup screen's PC list |
| POST | `/api/sessions/start` | The kiosk's own login form (name + purpose) |
| POST | `/api/sessions/{id}/end`, `/api/sessions/{id}/extend` | Log out / extend. Without a reason, the kiosk's end counts as `Visitor` and staff's as `Staff` |
| POST | `/api/assist` | "Need help?" |

### Sign-in and admin

| Method | Route | Who | Purpose |
|---|---|---|---|
| POST | `/api/auth/login` | anyone | `{ username, password }` → sets the sign-in cookie |
| GET | `/api/auth/me` | staff | Who is signed in |
| POST | `/api/auth/logout` | staff | Sign out |
| POST | `/api/auth/password` | staff | `{ currentPassword, newPassword }` |
| GET / POST | `/api/staff` | admin | List / create staff accounts |
| PUT | `/api/staff/{id}` | admin | Change name, role, active, contact email/mobile |
| POST | `/api/staff/{id}/password` | admin | Set a new password |
| POST | `/api/staff/{id}/unlock` | admin | Clear a lockout |
| GET | `/api/audit` | admin | Search the audit log (`?staffId=&action=pass.&targetType=&targetId=&from=&to=&limit=`) |

## Database rules

- **Foreign keys:** every pass, session and problem report must point to a real PC. A PC with history can't be deleted.
- **One active session per PC** and **one pending pass per PC**, enforced by unique indexes in the database itself, not just by the code.
- **Transactions:** "check the PC is free, then reserve it" and "check the pass, then start the session" each run as one all-or-nothing step, so two people can't grab the same PC at the same moment.
- **Server-clock timestamps:** every time comes from the server, stored in UTC (JSON times end in `Z`).
- **Secrets are hashed:** pass and waitlist tokens are stored as SHA-256 hashes, so a copy of the database can't be used to fake passes.
- **Enums are stored as text** (`"Pending"`, not `0`), so the database is readable in any SQLite viewer.

## Changing the database (migrations)

The tables come from the classes in `Models/` and the rules in `Data/TrackerDbContext.cs`. After changing them:

```bash
dotnet tool restore                          # once; installs dotnet-ef for this repo
dotnet dotnet-ef migrations add DescribeYourChange
dotnet run                                   # applies it on startup
```

Commit the new files in `Migrations/`. Never edit a migration that teammates have already applied; add a new one instead.

## Tests

```bash
cd ../TrackerApi.Tests
dotnet test
```

The tests start the whole API in memory, with a fresh database and a fake clock. That lets them check things like "10 minutes later the pass has expired" without waiting.

## Project layout

```
TrackerApi/
  Program.cs               entry point + service setup
  appsettings.json         database connection + time limits
  Models/                  entities (tables) + DTOs (request/response shapes)
  Data/                    TrackerDbContext (tables, rules, seed data), DatabaseSetup
  Migrations/              generated database changes
  Services/                the rules: status, passes, sessions, waitlist, problems, staff, audit, cleanup
  Auth/                    who may call what: cookies, kiosk key, create-admin command
  Controllers/             the HTTP endpoints (thin: they call the services)
TrackerApi.Tests/          automated tests
```
