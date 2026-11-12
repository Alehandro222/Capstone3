# MISA: Computer System Tracker (capstone)

Digital login and public-computer tracker for the **DICT Digital Transformation Center (DTC), Baguio City**.
Visitors log their visit on their phone (same fields as DTC's Google Form), optionally pick a PC,
show a QR pass at the help desk, and the session runs on the PC with a time limit.

## The three parts

| Folder | What | Run |
|---|---|---|
| `TrackerApi/TrackerApi` | ASP.NET Core 8 API + SQLite (EF Core migrations) | `dotnet run` (port 5080) |
| `TrackerApi/TrackerApi.Tests` | xUnit tests: whole API in memory, fake clock | `dotnet test` |
| `tracker-web/tracker-web` | React + Vite: staff dashboard (`/`, tabs: help desk, computers, waitlist, problems, help requests, visitor log, reports, admin), visitor phone app (`#/visit`), entrance poster (`#/poster`) | `npm run dev` (5173), `npm run dev:lan` for phones, `npm test` |
| `KioskApp` | WPF app on each library PC: lock screen, session bar, staff menu | `dotnet run` |
| `KioskApp.Tests` | xUnit tests for the kiosk's logic (`KioskApp/Core`), tracker faked | `dotnet test` |

The READMEs in each folder are the reference for routes, settings and rules. Read them before changing a part.
The API project is the **inner** `TrackerApi/TrackerApi` folder; `dotnet run` elsewhere fails.

## Build plan

1. ~~Core server: visits, passes, sessions, status, waitlist, cleanup job~~ (done)
2. ~~Staff sign-in (local accounts), roles, per-PC kiosk keys, rate limits, audit log~~ (done)
3. ~~Visitor phone app + entrance QR poster~~ (done)
4. ~~Staff dashboard rebuild: QR/code check-in, walk-ins, live board, end/extend, problem reports + per-PC history, waitlist, visitor log, staff accounts, audit log, pairing codes; old `PUT /api/computers/{id}/status` removed~~ (done)
5. ~~Kiosk app polish: Reserved countdown, time-left warning, extend button, lock-screen QR (`#/visit?pc=<id>`), staff PIN hashed + lockout, "time is up" farewell, `KioskApp.Tests`~~ (done)
6. ~~Reports: `GET /api/reports/summary` (counts by Manila day/hour, age, sex, sector, service, source, end reason, hours per PC, waitlist) + Excel (ClosedXML) / CSV export; names only for admins, audited~~ (done)
7. ~~Gemini report assistant: `POST /api/reports/assistant` (admin) sends only the summary's counts (`ReportAssistant.Facts`), no free text; draft + what was sent shown on the Reports tab; key via user-secrets / `Gemini__ApiKey`~~ (done)
8. **Deployment** (in progress, branch `phase-8-deploy`): done: security headers, Netlify-signed proxy + trusted hops + HTTPS settings (`Hosting:*`), API serves the built site (`npm run build:api`), daily SQLite backups, PostgreSQL option + migrations, accessibility pass (axe, 0 violations), Dockerfile + `render.yaml` + `netlify.toml`, kiosk PC setup script, `DEPLOYMENT.md`. Still to do: first real deploy (needs the user's Netlify/Render/Supabase accounts), run the PostgreSQL migration against Supabase, try the setup script on a DTC PC

## Decisions already made (don't re-ask)

- **Staff log in with local accounts only.** eGovPH SSO was built and then removed (citizen-oriented, needs DICT approval, HTTPS, credits, internet).
- **Registration = the DTC form:** first/last name, age group (17 & under, 18–30, 31–59, 60+; DTC's overlap at 17 fixed), sex (required), sectors (multi, the 10 DTC sectors), purpose/services availed (multi: Connectivity, Use of ICT Equipment, Co-Working Space, eGov Services, Others), DICT-CAR privacy statement (submitting = consent).
- **A visit does not need a PC.** Getting a PC adds "Use of ICT Equipment" automatically. Every session belongs to a visit (no name-only logins anywhere).
- **Visitors without a phone:** staff register them at the desk (walk-in, with or without a PC) or on the PC through the kiosk's staff-only form behind the PIN.
- **Sessions:** 3 hours; extend 1 hour, at most twice, only in the last 10 minutes, never while someone is waiting; idle logout 20 minutes (kiosk setting).
- **Waitlist is first come, first served:** a freed PC is held 5 minutes for whoever waited longest; missed turn → next person. Entries last 4 hours.
- **Each PC has its own kiosk key** from a one-time pairing code (admin creates it); a kiosk can only see/act on its own PC.
- **Staff end sessions only with reason "Staff".** PC status is computed, never stored.
- **Data retention (DTC): visit records kept 1 year**, then the whole record is deleted by the cleanup job (`KeepVisitsDays`, `RetentionService`): visit, sessions, passes, waitlist places, answers. DTC builds up its data within the year; reports older than a year come out empty.
- Phone app styling comes from the Figma Make file "LibPC Mobile Web App" (all CSS classes `v-`).

## Still open (ask the user when relevant)

- **Hosting for testing (decided, phase 8):** website on Netlify, the ASP.NET API on Render (free tier), database on Supabase PostgreSQL (switch EF Core from SQLite to Npgsql, new migrations). Mind: Render free sleeps after 15 min idle (kiosk heartbeats keep it awake but use most of the 750 free hours), Supabase free pauses after a week idle, kiosks/help desk need internet, Render's proxy must be trusted for X-Forwarded-For, and DICT-CAR may need to approve visitor data in the cloud. Real deployment may still be a PC at DTC.
- **Kiosk lockdown at DTC:** user plans auto-sign-in + start-up app; explain the gaps (Task Manager, Win key, closing the app) and settle the Windows setup in phase 8.
- **PC topology** – user will send it; the seed data is a sample of 12 PCs (PC-01..PC-12, PC-03 under maintenance). Admins can now rename PCs and remove never-used ones (Details), so the real list can be set up from the dashboard.
- Phases 1-7 and the pre-trial fixes are merged into `master` and pushed (`Alehandro222/Capstone3`). Phase 8 is on branch `phase-8-deploy`. Ask before merging into `master` or pushing a new branch.

## Working notes

- Errors from the API are always `{ "message": "..." }`; services throw `TrackerException`.
- Enums are stored and sent as text. All times are UTC from the injected `TimeProvider` (tests use a fake clock). Pages show Philippine time (`Asia/Manila`, UTC+8, no DST; `tracker-web/src/time.js`); reports in phase 6 must group by Manila days too. Countdowns (web and kiosk) use the server's clock from the HTTP `Date` header.
- Reports: only aggregated counts leave the summary endpoint; lists with names are admin-only exports, audited as `report.export`. The Gemini assistant sends only `ReportAssistant.Facts(summary)`; never add free text, names or records to it (AssistantTests check no names are sent).
- Phones reach the API through the website's server: it sends `X-Forwarded-For` (Vite `xfwd`), and the API trusts it only from loopback, or `Hosting:TrustedProxyHops` hops when hosted (Netlify → Render = 2, with `Hosting:ProxySignatureSecret` so the API can't be reached around Netlify).
- PostgreSQL: never add `EnableRetryOnFailure` (it rejects the services' explicit transactions). New migrations must be added for both contexts: `--context TrackerDbContext` (SQLite, `Migrations/`) and `--context PostgresTrackerDbContext --output-dir Migrations/Postgres`.
- Accessibility: keep axe-core at 0 violations (light and dark); dialogs via `useModal`; text on blue uses `--on-primary`.
- The kiosk locks by itself when the tracker is unreachable (log out, idle, out of time + 30 s) and reports the reason later.
- Help desk uses a keyboard-mode (HID) QR scanner; one help-desk unit. Don't show pass codes or tokens on staff screens: the visitor shows their own pass.
- Kiosk staff PIN: 6+ characters enforced, 8+ recommended, hashed. The kiosk runs as the visitor's Windows account, so that account can read the hash and key; the setup script limits other accounts and removes the tools to get at them (DEPLOYMENT.md, "What this can't hide").
- New tables/columns: add an EF migration (`dotnet tool restore`, then `dotnet dotnet-ef migrations add Name` in the inner API folder).
- Don't run the real kiosk .exe for testing on the dev PC: it writes its pairing to `C:\ProgramData\ComputerTracker`, which may hold the user's own pairing. Test `KioskApp/Core` through `KioskApp.Tests` (fake tracker, temp folders).
- If a running API locks `bin/`, build elsewhere instead of killing it: `OutputPath=<temp>/ dotnet build` (also works for `dotnet test` and `dotnet dotnet-ef`). The user may have their own API running; don't stop processes you didn't start.
- Local dev admin `devadmin`; its password is in `TrackerApi/TrackerApi/dev-credentials.local.txt` (git-ignored). Never put credentials in commits or chat.
- `npm install` needs `--legacy-peer-deps` until `@vitejs/plugin-react` is upgraded to a version that supports Vite 8.
- Commit with a branch, never straight to `master`; end commit messages with the Co-Authored-By line used in the history.
