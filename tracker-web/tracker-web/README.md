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

If your API uses a different port, edit `VITE_API_URL` in `.env` and restart `npm run dev`.

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
  api.js                     every call to the C# API
  components/
    ComputerCard.jsx         one computer
    PeripheralList.jsx       devices on a computer
    Modal.jsx                reusable form dialog
  styles.css
```
