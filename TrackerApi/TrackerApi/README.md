# Computer System Tracker: API (C# / ASP.NET Core)

Backend for the DTC Baguio digital login and its public computers. Visitors log their visit on their phone (the same form as the DTC Google Form) and, if they want a computer, pick one. Staff scan their pass at the help desk, and the session runs on the PC with a time limit.

## How a visit works

1. **Register (log the visit):** the visitor scans the entrance QR and fills in the form: first and last name, age group, sex, sectors and purpose / services availed → `POST /api/visits`. The phone gets a **token** to keep. Many visitors stop here (Wi-Fi, co-working space, eGov help); their visit is still logged for the reports.
2. **Pick a PC (optional):** `POST /api/visits/{token}/pass`. The PC becomes **Reserved**, and the phone shows a pass: a QR code (the visit's token) plus a 6-digit backup code, valid for 10 minutes. If no PC is free, the visitor can join the waitlist.
3. **Help desk:** staff scan the QR (or type the code) → `POST /api/passes/check-in`. The server checks the pass exists, is not expired or used, and the PC is not offline, then starts a **3-hour session**.
4. **PC:** the kiosk app sees the session and unlocks. During the last 10 minutes the visitor can extend by 1 hour (at most twice), but only if nobody is on the waitlist.
5. **End:** the visitor ends it (phone or PC), staff end it, the kiosk logs out after 20 minutes idle, or the cleanup job ends it at the time limit. A session that runs out is recorded as ending **at its time limit**, even if the server was off then and ends it later, so reports count the real time used.

**The waitlist is first come, first served.** When a PC frees up, it is held for the person who has waited longest. Their phone says a computer is ready, and they have 5 minutes to pick it; nobody else can take it meanwhile. If they don't pick in time, they are marked `Missed` and the next person gets it. (Staff can still give a PC to a walk-in at the desk.)

**Using a PC counts as "Use of ICT Equipment"**: it is added to the visit automatically when the session starts, even if the visitor didn't tick it.

**Visitors without a phone** are registered by staff: at the desk (`POST /api/visits/walk-in`, with or without a PC), or on the PC itself through the kiosk app's staff-only form (behind the staff PIN). Every session belongs to a registered visit, so every session has the full registration data for the reports.

### The registration form

| Field | Answers |
|---|---|
| First name, last name | Text, required |
| Age | `Age17AndUnder`, `Age18To30`, `Age31To59`, `Age60AndAbove` (the DTC form's groups, with the overlap at 17 fixed) |
| Sex | `Male`, `Female` |
| Sector (one or more) | `Nga`, `Lgu`, `Academe`, `Youth`, `SeniorCitizen`, `Msme`, `IndigenousPeople`, `Pwd`, `InformalSector`, `Others` |
| Purpose / services availed (one or more) | `Connectivity`, `IctEquipment` (use of ICT equipment), `CoWorkingSpace`, `EgovServices`, `Others` |

The forms show DICT-CAR's privacy statement. Submitting the form means agreeing to it, as with the Google Form.

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

> **Upgrading from an older development version:** the migrations were merged into one before the first release. A `tracker.db` made by an older version can't be upgraded, and the API stops with a message saying so. Delete the file and start again.

### Heartbeats while developing

In the library, a PC shows as Offline unless its kiosk app is running. That makes testing without the kiosk app awkward, so `appsettings.Development.json` sets `RequireHeartbeat` to `false`: no PC is ever Offline while developing. **The library server must run with `RequireHeartbeat: true`** (the default in `appsettings.json`), for example with `ASPNETCORE_ENVIRONMENT=Production`.

## Who may call what

| Caller | How it proves who it is | What it can use |
|---|---|---|
| Visitor's phone | Nothing to sign in to. Its visit **token** is the proof | Public availability, and its own visit: PC pass, session, waitlist place |
| Kiosk app on a PC | **Its own PC's key**, received when staff paired it | Only `/api/kiosk/*`, and only for **its own** PC: heartbeat, its session, help requests |
| Staff | **Sign-in** with username and password → an HttpOnly cookie | Everything else |
| Admin | Same, with the Admin role | Also staff accounts, adding PCs and the audit log |

Every endpoint needs a staff sign-in **unless it is marked otherwise**, so a new endpoint is protected by default.

- **Passwords** are stored as salted PBKDF2 hashes, never as text. Minimum 8 characters.
- **5 wrong passwords** lock the account for 15 minutes (an admin can unlock it sooner).
- **Deactivating** an account or changing its role signs that person out straight away.
- There must **always be one active admin**: the API refuses to deactivate or demote the last one.
- Open routes are **rate-limited** per device (IP address): 10 sign-in attempts a minute, 6 registrations, 6 PC picks and 5 waitlist joins per 10 minutes, and 10 pairing attempts a minute. Over the limit the API answers **429** with a message. This stops one phone from reserving every PC with a script.

### Pairing a PC

Each PC's kiosk app has **its own secret key**. A key copied from one PC (visitors use these PCs, so assume someone might) can only ever act as that PC. It can't see who is on the other PCs or end their sessions.

1. An admin creates a **pairing code** for the PC: `POST /api/computers/{id}/pairing-code`. You get something like `K7QM-3RXA`. It works once, within 15 minutes.
2. On that PC, the kiosk app's **Set up this computer** screen asks for the code. Staff type it in.
3. The app trades the code for its key (`POST /api/kiosk/pair`) and saves it in `C:\ProgramData\ComputerTracker\kiosk-key.txt`.

Pairing a PC again, or `POST /api/computers/{id}/unpair`, cancels its old key at once. Do this if you think a key was copied. The server stores only a hash of each key and code.

### Audit log

Every staff and kiosk action is recorded in the `AuditLog` table: who, what, which record, when, and from which IP address. Admins read it with `GET /api/audit`. Rows are never changed or deleted. Recorded actions:

- `auth.*`: sign-ins, failed sign-ins, sign-outs, password changes
- `staff.*`: account changes
- `pass.lookup`: staff **viewed** a visitor's details (Data Privacy Act)
- `pass.check_in`
- `visit.walk_in` (at the desk), `visit.kiosk` (on the PC, by staff)
- `session.end`, `session.extend`
- `kiosk.pairing_code`, `kiosk.paired`, `kiosk.unpaired`
- `problem.*`, `computer.*`, `peripheral.*`, `assist.status`
- `retention.visits_deleted`: visits (and their sessions) deleted after the 1-year retention period, with counts
- `report.assistant`: a report draft asked of Gemini (dates and model; counts only were sent)
- `report.export`: a report download (format, table, dates, and whether it had names)

## Settings (`appsettings.json`, section `Tracker`)

| Setting | Meaning | Default |
|---|---|---|
| `VisitLifetimeHours` | How long the phone can use its registration (pick a PC, join the waitlist) | `12` |
| `PassLifetimeMinutes` | Time to get the pass scanned | `10` |
| `SessionMinutes` | Session length | `180` |
| `ExtensionMinutes` | Time added per extension | `60` |
| `MaxExtensions` | Extensions allowed per session | `2` |
| `ExtendWindowMinutes` | Extending is allowed only in the last N minutes | `10` |
| `OfflineAfterSeconds` | No heartbeat for this long → Offline | `30` |
| `RequireHeartbeat` | `false` = never Offline (development only) | `true` |
| `WaitlistLifetimeMinutes` | Waitlist entries are dropped after this long | `240` |
| `WaitlistOfferMinutes` | A freed PC is held this long for the first person waiting | `5` |
| `CleanupIntervalSeconds` | How often passes expire and overdue sessions end | `15` |
| `KeepVisitsDays` | Data retention (DTC: 1 year). A visit is deleted this many days after it was logged, with its PC sessions, passes, waitlist places and answers. `0` = never. | `365` |
| `TimeZone` | The center's time zone: reports count days and hours of the day in it, and exports show times in it. Stored times stay UTC. Falls back to a fixed UTC+8 if Windows can't find it. | `Asia/Manila` |

### Rate limits (section `RateLimits`)

| Setting | Meaning | Default |
|---|---|---|
| `Enabled` | `false` turns all limits off | `true` |
| `SignInPerMinute` | Staff sign-in attempts per device | `10` |
| `RegistrationsPer10Minutes` | New registrations per device | `6` |
| `PassesPer10Minutes` | PC picks per device | `6` |
| `WaitlistJoinsPer10Minutes` | Waitlist joins per device | `5` |
| `PairingPerMinute` | Kiosk pairing attempts per device | `10` |

Limits count per IP address. Phones reach the API through the website's server, so every request arrives from this PC; the website's server passes each phone's real address in `X-Forwarded-For`, and the API uses it **only when the request comes from this same machine** (so a phone can't fake its address). The audit log records the same address. A proxy on another machine (for example a hosting service) must be added to `ForwardedHeadersOptions.KnownProxies` in `Program.cs`.

The 20-minute idle logout is set in the kiosk app (`idleTimeoutSeconds` in `kiosk.settings.json`), because only the PC knows when its keyboard and mouse were last used.

## Endpoints

Errors always come back as `{ "message": "..." }`, written so the frontend can show them as they are.

### Visitor's phone

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/public/computers` | Live availability (no names or personal data) |
| POST | `/api/visits` | Register (the form above) → returns `token` (keep it!) and the visit |
| GET | `/api/visits/{token}` | The visit: its PC pass, session and waitlist place (poll this) |
| POST | `/api/visits/{token}/pass` | `{ computerId }`: reserve a PC (picking again gives the first one back) |
| POST | `/api/visits/{token}/pass/cancel` | Give the PC back |
| POST | `/api/visits/{token}/session/end` | End the session |
| POST | `/api/visits/{token}/session/extend` | Extend the session |
| POST | `/api/visits/{token}/waitlist` | Join the waitlist (only when no PC is free) |
| DELETE | `/api/visits/{token}/waitlist` | Leave the line |

### Staff

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/computers` | Dashboard: status, current user, session end time, open problem, peripherals, `pairedAt` (when its kiosk app was paired; `null` = not paired) |
| GET | `/api/computers/{id}` | One PC, same details |
| GET | `/api/computers/{id}/sessions` | Session history for one PC |
| POST | `/api/computers` | Add a PC (admin) |
| POST | `/api/computers/{id}/pairing-code` | One-time code to pair that PC's kiosk app (admin) |
| POST | `/api/computers/{id}/unpair` | Cancel that PC's key (admin) |
| GET | `/api/visits` | The visitor log, newest first (`?from=&to=&limit=`) |
| POST | `/api/visits/walk-in` | Visitor without a phone: the form plus an optional `computerId` (starts the session right away; without it the visit is only logged) |
| POST | `/api/passes/lookup` | `{ token }` (the QR) or `{ code }` → the pass and the visitor's details |
| POST | `/api/passes/check-in` | `{ token }` or `{ code }` → validates and starts the session |
| GET | `/api/passes` | Recent passes (`?status=Pending`) |
| GET | `/api/sessions/active` | Running sessions |
| GET | `/api/sessions` | History (`?computerId=`, `?from=`, `?to=`, `?limit=`) |
| GET | `/api/sessions/{id}` | One session |
| POST | `/api/sessions/{id}/end` | End a session. Recorded as ended by staff; the only reason allowed is `Staff` |
| POST | `/api/sessions/{id}/extend` | Extend a session |
| GET / POST | `/api/problems` | List (`?status=Open&computerId=`) / open a problem report (the PC shows as Maintenance until every open report on it is resolved) |
| PUT | `/api/problems/{id}/resolve` | Resolve it, with optional `{ "notes": "..." }` |
| GET | `/api/waitlist` | Everyone waiting, in order |
| GET | `/api/reports/summary` | Counts for a range of days (`?from=2026-11-01&to=2026-11-30`, both included, days in `TimeZone`; default today; at most 366 days): see Reports |
| GET / POST | `/api/reports/assistant` | **Admin.** GET: is the Gemini assistant set up, and which model. POST `{ from, to }`: a draft of the period's report written by Gemini, plus exactly what was sent (see Report assistant) |
| GET | `/api/reports/export` | Download: `?format=xlsx` (summary and days), `&names=true` adds the visitor and session lists (**admin**); `?format=csv&table=summary\|days\|visits\|sessions` (visits/sessions: **admin**) |
| GET / POST | `/api/assist` | Help requests (`?status=Open`) / create one `{ computerId, message }` (500 characters max) |
| PUT | `/api/assist/{id}/status` | `{ status }`: Open, InProgress or Resolved |
| GET / POST | `/api/peripherals` | List (`?computerId=`, `?inUse=`) / add a peripheral |
| PUT | `/api/peripherals/{id}/assign` | `{ computerId }` to plug it into a PC, `null` to unplug |
| PUT | `/api/peripherals/{id}/use` | `{ inUse }` |

### Kiosk app on each PC (its own key; only for its own PC)

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/kiosk/pair` | `{ code }` → this PC's id and key (no key needed; rate-limited) |
| POST | `/api/kiosk/heartbeat` | "This PC is on" (every few seconds) → this PC, its session (with end time and whether it can be extended), and `reservedUntil` while a visitor's pass for it is waiting |
| POST | `/api/kiosk/sessions` | Staff-only form on the PC: the registration form → registers the visit and starts the session here |
| POST | `/api/kiosk/session/end` | Log out. Optional `{ "reason": "Idle" }`; without it the reason is `Visitor` |
| POST | `/api/kiosk/session/extend` | Extend this PC's session |
| POST | `/api/kiosk/help` | "Need help?" `{ message }` |

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

## Data retention

DTC keeps visit records for **1 year**. The cleanup job deletes every visit older than `KeepVisitsDays` (365), together with everything that belongs to it: its PC sessions, passes, waitlist places and sector/service answers. (A visit whose session is somehow still running is left until it ends.) Reports for dates older than a year therefore come out empty: download the year's reports before then. Each run is in the audit log as `retention.visits_deleted`, with counts only; older audit entries keep record numbers, never names. Database backups (phase 8) must follow the same rule: don't keep backups longer than a year.

## Reports

`GET /api/reports/summary` returns **counts only, no names**, for a range of days in the center's time zone (a "day" in Baguio runs from midnight to midnight Philippine time):

- Totals: visits, visitors who used a PC, PC sessions, PC hours, average session length, extensions, sessions still running
- Per day: visits, PC sessions, PC hours
- Per hour of the day (0-23): visits, and PC sessions started
- By age group, sex, sector, purpose / services availed, and how the visit was registered. Every possible answer is listed, also those with 0, in the form's order. A visitor can tick several sectors and services, so those add up to more than the number of visits.
- How PC sessions ended, PC sessions and hours per PC, and the waitlist (joined, got a PC, missed their turn, waited too long, left)

Sessions count on the day they **started**. A running session counts until now, but never past its time limit.

**Exports** (`/api/reports/export`): any staff member can download the counts (Excel with *Summary* and *By day* sheets, or CSV). The **lists with names** (Excel *Visits* and *PC sessions* sheets, or the `visits` / `sessions` CSV) are **admin only** (Data Privacy Act: least access). Every download is recorded in the audit log as `report.export`, with "with names" when it held any. CSVs are UTF-8 with a BOM, so Excel shows "ñ" correctly, and a cell that starts with `=`, `+`, `-` or `@` gets a leading `'`, so a name typed as a formula stays text. Times in exports are Philippine time.

## Report assistant (Gemini)

Admins can have **Google Gemini** draft the report for a period (Reports tab → *Report draft by Gemini*). DTC's partner asked for Gemini on the free tier, and Google may use what its free tier receives, so:

- **Only the summary's counts are sent**: totals, per day, per hour, age group, sex, sector, services, how visitors registered, how sessions ended, hours per PC, the waitlist. There are no names, no individual visits or sessions, and no box where staff type free text that could hold a name.
- The page shows **exactly what was sent** next to the draft, and every request is recorded in the audit log as `report.assistant`.
- The draft is a starting point: staff check the numbers and edit it before sending it on.

**Set it up:** create a free API key in [Google AI Studio](https://aistudio.google.com/apikey), then give it to the API, never in a committed file:

```bash
# while developing (stored outside the repo, in your Windows user profile)
dotnet user-secrets set "Gemini:ApiKey" "<your key>"
```

On a server, set the environment variable `Gemini__ApiKey` instead (on Render: *Environment* → add `Gemini__ApiKey`).

| Setting (section `Gemini`) | Meaning | Default |
|---|---|---|
| `ApiKey` | The Google AI Studio key. Empty = the assistant is switched off and says how to set it up. | empty |
| `Model` | Which Gemini model writes the draft. Free-tier models and their limits change; check AI Studio. | `gemini-2.5-flash` |
| `TimeoutSeconds` | How long to wait for Gemini | `90` |

Free-tier limits are per Google project and change often (Google calls them "best effort"); a monthly report needs only a few requests. Over the limit, the page says "Gemini's free limit was reached" and nothing else happens.

## Database rules

- **Foreign keys:** every pass, session and problem report must point to a real PC, and every pass and session to a real visit. A PC or visit with history can't be deleted.
- **One PC, one visitor:** one active session and one pending pass per PC, and one active session, one pending pass and one waitlist place per visit. These are enforced by unique indexes in the database itself, not just by the code.
- **Transactions:** "check the PC is free, then reserve it" and "check the pass, then start the session" each run as one all-or-nothing step, so two people can't grab the same PC at the same moment.
- **Server-clock timestamps:** every time comes from the server, stored in UTC (JSON times end in `Z`).
- **Secrets are hashed:** visit tokens and kiosk keys are stored as SHA-256 hashes, so a copy of the database can't be used to fake passes.
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
  Services/                the rules: registration, visits, passes, sessions, waitlist, status, problems, staff, audit, cleanup
  Auth/                    who may call what: cookies, per-PC kiosk keys, rate limits, create-admin command
  Controllers/             the HTTP endpoints (thin: they call the services)
TrackerApi.Tests/          automated tests
```
