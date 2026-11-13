# MISA: status notes while you were away (2026-11-13)

Notes only. Nothing in the code, `master` or any `phase-*` branch was changed.
Reviewed: `origin/phase-8-deploy` (newest work, 2 commits on top of `master`).

## Where the project stands

| | State |
|---|---|
| Phases 1-7 | Merged into `master` and pushed (`c94b285`). |
| Phase 8 | On `phase-8-deploy` (pushed, **not merged**): part 1 = security headers, Netlify signed proxy, backups, PostgreSQL option, accessibility; part 2 = DEPLOYMENT.md, Dockerfile, render.yaml, netlify.toml, kiosk PC setup script, rename/remove PCs. |
| Tests (phase 8) | API **82/82 pass**. Kiosk Core **17/17 pass** (built on Linux without WPF). Web **13/13 pass**, `npm run build` OK. |
| PostgreSQL | Previously "not yet run against a real server". **Now smoke-tested** on a local PostgreSQL 16: migrations, sign-in, register, pick PC, check-in, end, walk-in, report summary, Excel export, audit log all work. One concurrency bug found (P1 below). |

## Priority 1: fix before any real use

**P1-a. PostgreSQL: a PC can be double-booked (desk walk-in vs phone pick).**
Reproduced 3 out of 3 times: when staff start a walk-in on a PC at the same moment a phone picks
it, both succeed, so the PC has a running session *and* someone else's pending pass. The visitor's
phone then shows a pass for a PC that's in use. SQLite doesn't have this problem (re-tested: one side
always wins), because SQLite runs one write at a time; PostgreSQL's default isolation (Read
Committed) doesn't. The unique indexes only stop two *passes* or two *sessions* on one PC, not a
pass + a session. The same kind of race applies to the waitlist's first-come-first-served check.
Places: `PassService.PickAsync`, `PassService.CheckInAsync`, `VisitService.StartOnFreeComputerAsync`,
`WaitlistService.EnsureMayPickAsync`.
Fix options (your call): lock the computer row (`SELECT ... FOR UPDATE`) inside those
transactions, or use Serializable transactions on PostgreSQL and turn the serialization error into
the existing 409 "just taken" message. Only matters if you deploy on Supabase (option B).

**P1-b. Netlify free plan will likely run out of credits within the month.**
Every `/api` call goes through Netlify and counts as a web request. Free plan: 300 credits/month,
hard limit; web requests cost about 2 credits per 10k (sources disagree: 2 or 3), so at most about
1.5M requests a month, and builds/bandwidth use credits too.
Current polling: kiosks every 5 s (12 PCs = 144/min), dashboard 6 calls every 5 s (72/min, even
in a hidden tab), phones every 3 s. That's roughly 3-4M requests/month at 10 h/day, about 2-3× the
cap. Kiosks and dashboards left on overnight make it worse.
Options (your call): slow polling (kiosk 15-30 s, dashboard 10-15 s), pause polling when the
tab is hidden, combine the dashboard's 6 calls into one, have kiosks call the Render address
directly (then they skip the Netlify signature, which would need a different check), or run option A
(PC at DTC) for real use.

**P1-c. The first admin can't be created as DEPLOYMENT.md says (B.5).**
Render's free plan has **no Shell tab** (shell/SSH are paid features). Workaround to document: run
`dotnet run -- create-admin <user> "<name>"` on your own PC with `Database__Provider=Postgres` and
the Supabase connection string. It only writes to the database.

## Priority 2: should fix in phase 8

- **Staff get signed out whenever Render restarts or wakes.** ASP.NET's sign-in cookie keys
  (Data Protection) aren't saved anywhere permanent. In a Render container they're lost on every
  deploy, restart or wake from the 15-minute sleep. Fix: store the keys in the database
  (`PersistKeysToDbContext`) or a Render disk (paid). On a PC at DTC it's fine as is.
- **Supabase connection limits.** Npgsql pools up to 100 connections by default; Supabase free's
  session pooler allows far fewer. Add e.g. `Maximum Pool Size=10` to the connection string in
  DEPLOYMENT.md (to check on the first deploy).
- **The `CLAUDE.md` on `master` is out of date**: it says phase 7 is "on branch `phase-7-gemini`",
  but it's merged. The `phase-8-deploy` copy is correct, so merging phase 8 fixes it.
- **Extending a session that is already over.** `SessionService.CheckExtend` allows it in the
  gap between the session's end and the next cleanup run (up to 15 s), because a negative "time
  left" passes the "last 10 minutes" check. Small, but easy: refuse when `now >= ScheduledEndAt`.
- **Computer names are unique only by an app check.** There's no unique database index, so two
  admins renaming at the same moment could create duplicates. Low risk; the fix is a migration
  (both contexts).
- **Backups hold names in plain files** (`tracker-YYYY-MM-DD.db`, up to 30 days). Fine for DTC's
  1-year rule, but the backup folder needs the same restricted access as the database. Add this to
  the checklist.

## Priority 3: efficiency and cleanup (no behaviour change)

- **Each phone poll (every 3 s) writes to the database.** `GET /api/visits/{token}` →
  `ToVisitorDtoAsync` → `WaitlistService.StatusForAsync` → `RefreshAsync`, which saves and
  recomputes every PC's status. That's about 10+ queries per poll, per phone. The cleanup job
  already refreshes every 15 s; reads could skip it.
- **Each kiosk heartbeat works out the status of every PC.** `GetStaffViewAsync(id)` calls
  `GetAllAsync()` (4 queries over all PCs) to use one entry, then the session and pass are queried
  again. About 8 queries per heartbeat × 12 PCs every 5 s. Worth trimming before Supabase
  (network round trips).
- `CountAvailableAsync` runs 2-3 times per waitlist request; it could be computed once.
- The dashboard keeps polling in a hidden browser tab (no `visibilitychange` check), which also
  feeds P1-b.
- `axe-core` is a dev dependency but no automated test uses it; the "0 violations" was a manual
  run. Could become a test script.
- `npm install` still needs `--legacy-peer-deps` (`@vitejs/plugin-react` 4.x vs Vite 8).
- There's no ESLint config, so `npx eslint` does nothing. Optional.

## Decisions waiting for you

1. **Where does real data live?** Option A (PC at DTC, SQLite, no internet needed) or option B
   (Netlify + Render + Supabase). P1-a and P1-b only matter for B, and DICT-CAR may need to
   approve visitor data in the cloud for B.
2. **If B: how to stay under Netlify's free limit** (P1-b options above).
3. **If B: which fix for the PostgreSQL race** (row lock vs Serializable).
4. **Merge `phase-8-deploy` into `master`?** Tests are green; ask-first rule.
5. **Kiosk lockdown:** `Setup-KioskPC.ps1` was written but never run on a real PC. Which
   Windows edition do the DTC PCs have? Pro needs the script; Enterprise/Education can use
   Assigned Access.
6. **PC topology:** still waiting for the real PC list (rename/remove is ready on the dashboard).
7. **HTTPS for option A:** a certificate from DICT IT, or a staff-only network.

## Checked and fine (no action)

- Netlify signature check (HS256, constant-time compare, issuer and expiry) and forwarded-header
  trust by hop count.
- Render health checks accept 2xx/3xx, so the HTTPS redirect doesn't break `/health`.
- Kiosk keys: per PC, hashed, constant-time compare, and a key only acts as its own PC.
- Staff lockout (5 tries / 15 min) and "always one active admin".
- Retention deletes children before visits in one transaction and skips visits with a running session.
- Walk-ins can take a PC held for the waitlist on purpose, and the dialog warns staff.
- Two phones racing for one PC: the unique index refuses the second (SQLite and PostgreSQL).
- No `TODO`/`FIXME` left in the code.
