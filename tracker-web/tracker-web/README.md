# Computer System Tracker: Web Dashboard (Vite + React)

Frontend for the C# `TrackerApi`.

## Run it

1. Start the API first (in the `TrackerApi` folder): `dotnet run` (it listens on http://localhost:5080).
2. In this folder:

```bash
npm install
npm run dev
```

3. Open http://localhost:5173

4. Sign in with a staff account. The first admin is created from the API folder with `dotnet run -- create-admin <username> "<full name>"` (see the API README).

The dev server forwards `/api` to the API on http://localhost:5080 (see `vite.config.js`), so the browser only ever talks to one address and the sign-in cookie just works. If your API uses a different port, start the dev server with `API_URL=http://localhost:1234 npm run dev`.

## What it does

- Shows every computer as a card: status, current user, purpose, how long they've been on.
- Lists the peripherals connected to each computer. While a session is running, a device can be toggled "In use" (headset, tablet, mic...).
- Start / end sessions, report a computer broken, mark it fixed.
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
  styles.css
```
