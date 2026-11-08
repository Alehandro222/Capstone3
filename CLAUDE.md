# MISA: Computer System Tracker (capstone)

Digital login and public-computer tracker for the **DICT Digital Transformation Center (DTC), Baguio City**.
Visitors log their visit on their phone (same fields as DTC's Google Form), optionally pick a PC,
show a QR pass at the help desk, and the session runs on the PC with a time limit.

## The three parts

| Folder | What | Run |
|---|---|---|
| `TrackerApi/TrackerApi` | ASP.NET Core 8 API + SQLite (EF Core migrations) | `dotnet run` (port 5080) |
| `TrackerApi/TrackerApi.Tests` | xUnit tests: whole API in memory, fake clock | `dotnet test` |
| `tracker-web/tracker-web` | React + Vite: staff dashboard (`/`), visitor phone app (`#/visit`), entrance poster (`#/poster`) | `npm run dev` (5173), `npm run dev:lan` for phones |
| `KioskApp` | WPF app on each library PC: lock screen, session bar, staff menu | `dotnet run` |

The READMEs in each folder are the reference for routes, settings and rules. Read them before changing a part.
The API project is the **inner** `TrackerApi/TrackerApi` folder; `dotnet run` elsewhere fails.

## Build plan

1. ~~Core server: visits, passes, sessions, status, waitlist, cleanup job~~ (done)
2. ~~Staff sign-in (local accounts), roles, per-PC kiosk keys, rate limits, audit log~~ (done)
3. ~~Visitor phone app + entrance QR poster~~ (done)
4. **Staff dashboard rebuild** (next): QR scanner + 6-digit code check-in, walk-ins, live board with
   Reserved/Offline and countdowns, end/extend sessions, problem reports + per-PC history, waitlist,
   staff account admin, audit log viewer, PC pairing codes (today via Swagger), remove the old
   `PUT /api/computers/{id}/status` "Report broken" route, fix "Cannot reach the API" sticking while the API starts
5. Kiosk app polish: Reserved screen, time-left warning, extend button, lock-screen QR; staff PIN is still plain text in `kiosk.settings.json`
6. Reports (sessions per day/hour, by sector, by age group, hours per PC) + CSV/Excel export
7. Gemini monthly-report assistant (partner wants Gemini, free tier): send **only aggregated counts**, never names or individual records (free-tier data may be used by Google)
8. Deployment: HTTPS, Production settings (`RequireHeartbeat: true`, no Swagger), backups, data retention

## Decisions already made (don't re-ask)

- **Staff log in with local accounts only.** eGovPH SSO was built and then removed (citizen-oriented, needs DICT approval, HTTPS, credits, internet).
- **Registration = the DTC form:** first/last name, age group (17 & under, 18–30, 31–59, 60+; DTC's overlap at 17 fixed), sex (required), sectors (multi, the 10 DTC sectors), purpose/services availed (multi: Connectivity, Use of ICT Equipment, Co-Working Space, eGov Services, Others), DICT-CAR privacy statement (submitting = consent).
- **A visit does not need a PC.** Getting a PC adds "Use of ICT Equipment" automatically. Every session belongs to a visit (no name-only logins anywhere).
- **Visitors without a phone:** staff register them at the desk (walk-in, with or without a PC) or on the PC through the kiosk's staff-only form behind the PIN.
- **Sessions:** 3 hours; extend 1 hour, at most twice, only in the last 10 minutes, never while someone is waiting; idle logout 20 minutes (kiosk setting).
- **Waitlist is first come, first served:** a freed PC is held 5 minutes for whoever waited longest; missed turn → next person. Entries last 4 hours.
- **Each PC has its own kiosk key** from a one-time pairing code (admin creates it); a kiosk can only see/act on its own PC.
- **Staff end sessions only with reason "Staff".** PC status is computed, never stored.
- Phone app styling comes from the Figma Make file "LibPC Mobile Web App" (all CSS classes `v-`).

## Still open (ask the user when relevant)

- **How long to keep visitor names** (data retention) – user will confirm with DTC.
- **PC topology** – user will send it; the seed data is a sample of 12 PCs (PC-01..PC-12, PC-03 under maintenance).
- Branch `phase-1-core-server` holds all work so far; `master` only has the first commit. Nothing pushed yet (remote: `Alehandro222/Capstone3`). Ask before merging or pushing.

## Working notes

- Errors from the API are always `{ "message": "..." }`; services throw `TrackerException`.
- Enums are stored and sent as text. All times are UTC from the injected `TimeProvider` (tests use a fake clock).
- New tables/columns: add an EF migration (`dotnet tool restore`, then `dotnet dotnet-ef migrations add Name` in the inner API folder).
- If a running API locks `bin/`, build elsewhere instead of killing it: `OutputPath=<temp>/ dotnet build` (also works for `dotnet test` and `dotnet dotnet-ef`). The user may have their own API running; don't stop processes you didn't start.
- Local dev admin `devadmin`; its password is in `TrackerApi/TrackerApi/dev-credentials.local.txt` (git-ignored). Never put credentials in commits or chat.
- `npm install` needs `--legacy-peer-deps` until `@vitejs/plugin-react` is upgraded to a version that supports Vite 8.
- Commit with a branch, never straight to `master`; end commit messages with the Co-Authored-By line used in the history.
