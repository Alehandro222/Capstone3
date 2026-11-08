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

The phone keeps its visit token (in the browser's local storage), so closing or reloading the page does not lose the visit. It checks the visit every 3 seconds. **Done** forgets it.

## Dashboard (`/`)

- Shows every computer as a card: status, current user, purpose, how long they've been on.
- Lists the peripherals connected to each computer. While a session is running, a device can be toggled "In use" (headset, tablet, mic...).
- **Register visitor** on a free computer: the DTC registration form for a visitor without a phone. Their session starts straight away.
- **Log a visit (no computer):** the same form, for visitors who only need Wi-Fi, a desk or eGov help.
- End sessions, report a computer broken, mark it fixed.
- "Need help?" sends an assist request to the API.
- Refreshes automatically every 5 seconds.

## Structure

```
src/
  main.jsx                   entry point
  App.jsx                    loads data, owns state, handles actions
  auth/
    StaffGate.jsx            shows the sign-in screen until a staff member is signed in
    Login.jsx                username + password
  api.js                     every call to the C# API
  components/
    ComputerCard.jsx         one computer
    PeripheralList.jsx       devices on a computer
    Modal.jsx                reusable form dialog
    WalkInDialog.jsx         the registration form for walk-ins
  registration.js            the form's choices and privacy statement (shared with the phone app)
  visitor/                   the visitor phone app (#/visit) and the entrance poster (#/poster)
    VisitorApp.jsx           decides which screen to show from the visit; keeps the token
    RegisterScreen.jsx       the DTC login form
    ComputersScreen.jsx      live PC grid; pick a PC; join the waitlist
    VisitScreens.jsx         welcome, QR pass, time left, waitlist, messages
    EntrancePoster.jsx       printable QR poster
    visitor.css              the Figma design (all classes start with "v-")
  time.js                    reads the API's UTC times
  styles.css
```
