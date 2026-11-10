# Computer System Tracker: Website (Vite + React)

Frontend for the C# `TrackerApi`. One website, three pages:

| Address | Page | Who |
|---|---|---|
| `http://<server>:5173/` | Staff dashboard (sign-in first) | Staff |
| `http://<server>:5173/#/visit` | **Visitor phone app**: the DTC digital login, picking a PC, the QR pass, time left, the waitlist | Visitors (the entrance QR opens it) |
| `http://<server>:5173/#/poster` | Printable entrance QR poster | Staff, to print |

## Run it

1. Start the API first (in the inner `TrackerApi/TrackerApi` folder, where `TrackerApi.csproj` is): `dotnet run` (it listens on http://localhost:5080).
2. In this folder:

```bash
npm install
npm run dev
```

3. Open http://localhost:5173

4. Sign in with a staff account. The first admin is created from the API folder with `dotnet run -- create-admin <username> "<full name>"` (see the API README).

The dev server forwards `/api` to the API on http://localhost:5080 (see `vite.config.js`), so the browser only ever talks to one address and the sign-in cookie just works. If your API uses a different port, put `API_URL=http://localhost:1234` in a file named `.env.local` in this folder.

### Trying the phone app on a real phone

1. Connect the phone to the same Wi-Fi as this PC.
2. Start the dev server so other devices can reach it: `npm run dev:lan`. It prints a "Network" address such as `http://192.168.1.10:5173/`.
3. On this PC, open `http://192.168.1.10:5173/#/poster` (your Network address, **not** localhost) and scan the QR with the phone, or open `http://192.168.1.10:5173/#/visit` on the phone directly.
4. If the phone cannot connect, allow Node.js through Windows Firewall when Windows asks, or allow port 5173.

Only the website needs to be reachable: it forwards the phone's requests to the API on this PC.

## Visitor phone app (`#/visit`)

From the Figma Make design ("LibPC Mobile Web App"):

1. **Welcome:** live count of free computers, **Log my visit**, and **View PCs**.
2. **Log your visit:** the DTC form (first and last name, age group, sex, sectors, purpose / services availed) and DICT-CAR's privacy statement.
3. Ticked **Use of ICT Equipment**? It goes straight to **Choose a computer**: a live grid, green = free. Otherwise the visit is just logged, with an **I need a computer** button.
4. **QR pass:** the QR code (the visit's secret token), the 6-digit backup code, and how long it stays valid. Staff scan it at the help desk.
5. **Time left:** switches on by itself when staff check the visitor in. **Extend by 1 hour** appears in the last 10 minutes when allowed; **End session** asks first.
6. **Waitlist** when no computer is free: the visitor's place in line, then "It's your turn" with a 5-minute countdown to pick the held PC.

**Scanned at a PC:** each kiosk's lock screen shows a QR code for `#/visit?pc=<id>`. That PC is then already chosen on **Choose a computer** (if it is still free; otherwise the phone says so).

The phone keeps its visit token (in the browser's local storage), so closing or reloading the page does not lose the visit. It checks the visit every 3 seconds. **Done** forgets it.

## Staff dashboard (`/`)

Sign in first. The dashboard refreshes every 5 seconds. Tabs:

| Tab | What staff do there |
|---|---|
| **Help desk** | **Check visitors in**: scan the QR pass with the desk's QR scanner, or type the 6-digit code. (**Scan QR with camera** also works on `localhost` / HTTPS.) The visitor's details (name, age group, sex, sectors, purpose, PC, pass status) show first; **Check in and start session** starts the 3-hour session. Also lists PCs reserved and waiting to be scanned (names and time left only: the visitor must show their own pass), **Register and give a PC** / **Log a visit (no PC)** for visitors without a phone, and the waitlist. |
| **Computers** | Live board: every PC with its status (Available, Reserved, In use, Maintenance, Offline), who is on it, a countdown to the end of the session (red in the last 10 minutes), **Extend 1 h** (when the rules allow it; hover for why not), **End session** (asks first), **Register visitor** on a free PC, peripherals. **Details** shows the PC's problem reports, its session history and (admins) kiosk pairing. Admins can **Add a PC**. |
| **Waitlist** | Everyone waiting, in order; when a PC is held for the first person, the time they have left to pick it. |
| **Problems** | Open problem reports (**Report a problem**, **Resolve** with optional notes) and recently resolved ones. A PC with an open report shows as Maintenance. |
| **Help requests** | "Need help?" requests from the kiosk app on each PC. |
| **Visitor log** | Every visit logged on a chosen day (phone, help desk or kiosk), with the DTC form answers. |
| **Reports** | Counts for a period (today, yesterday, this week, this month, last month, or custom dates), in Philippine time: totals, visits and PC sessions per day and per hour of the day, age, sex, sector, purpose, how visitors registered, how sessions ended, hours per PC, the waitlist. Each day/hour chart has a table view. **Download** gives Excel or CSV of the counts; admins can also download the visitor and session lists with names (recorded in the audit log). |
| **Staff accounts** (admin) | Add accounts, edit name / role / active, set a new password, unlock after too many wrong passwords. Accounts are deactivated, never deleted. |
| **Audit log** (admin) | Who did what and when, filtered by person, kind of action and dates. |

**Pairing a PC's kiosk app** (admin): Computers → **Details** → **Create pairing code**, then type the code into the kiosk app's "Set up this computer" screen. **Unpair** stops the PC's key at once.

**Desk QR scanner:** a scanner in keyboard mode (USB or Bluetooth "HID") works out of the box: it types what it read and presses Enter. On the Help desk tab a scan is caught even when the code box does not have focus (a fast burst of keys ending in Enter); it is ignored while a dialog is open or another text field has focus. Set the scanner to send **Enter** after each scan and to the same keyboard layout as the PC (US). A scanner that sends results over the network instead of as a keyboard would need its own integration.

**Philippine time:** every time and every "day" (visitor log, audit log) is shown in Philippine time (`Asia/Manila`), whatever the device is set to. Countdowns use the server's clock (from each answer's `Date` header), so a phone or PC with a wrong clock still shows the right time left.

**Free PCs held for the waitlist:** the waitlist holds free PCs by number, not particular PCs. When every free PC is held, the board says so, free cards read "Held for the waitlist", and the walk-in form warns that giving one away takes a turn (the last person offered goes back to waiting).

**Camera:** browsers only allow the camera on `https://` or on `localhost`. On the help-desk PC, open the dashboard as `http://localhost:5173`; on another address, type the code instead (or use a USB scanner) until the site runs on HTTPS (phase 8).

**If the API is down or still starting**, the sign-in screen says "Cannot reach the API" and keeps checking every 3 seconds; the message goes away, and the dashboard opens if you were signed in, as soon as the API answers. The dev server answers `502` with that message while the API is unreachable (`vite.config.js`).

Everyone can **Change password** (top of the page).

## Structure

```
src/
  main.jsx                   entry point: picks the page from the address (#/visit, #/poster, else dashboard)
  App.jsx                    staff dashboard: loads data every 5 s, tabs, dialogs, actions
  auth/
    StaffGate.jsx            shows the sign-in screen until a staff member is signed in; retries while the API is down
    Login.jsx                username + password
  api.js                     every call to the C# API
  dashboard/
    HelpDesk.jsx             check-in (code or QR), reserved passes, walk-ins, waitlist summary
    QrScanner.jsx            camera QR reader (jsQR)
    Board.jsx                live board of PCs with countdowns, end / extend
    ComputerDetails.jsx      one PC: problem reports, session history, kiosk pairing
    Problems.jsx             open and resolved problem reports
    Waitlist.jsx             the line, in order
    VisitorLog.jsx           visits on a chosen day
    StaffAccounts.jsx        admin: staff accounts
    AuditLog.jsx             admin: audit log with filters
    Reports.jsx              reports for a period: tiles, charts, downloads
    format.js, ui.jsx        labels, time formatting, dialogs
    dashboard.css
  components/
    HelpRequests.jsx         help requests from the PCs
    PeripheralList.jsx       devices on a computer
    WalkInDialog.jsx         the registration form for visitors without a phone
  registration.js            the form's choices and privacy statement (shared with the phone app)
  visitor/                   the visitor phone app (#/visit) and the entrance poster (#/poster)
  time.js                    reads the API's UTC times
  styles.css
```
