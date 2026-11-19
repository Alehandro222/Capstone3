# Automating the monthly report (Activepieces, n8n, a scheduler)

An automation tool can start the report job and fetch the result with one secret token. The token
is **not an admin**: it opens only the report runs and the exports **without names**. Names, staff,
settings and everything else still need a staff login.

## 1. Make the token

Make a random string of at least 32 characters (a password manager's generator is fine) and give
it to the API as `Automation__Token`:

- **Render:** Environment → add `Automation__Token`.
- **Local:** `dotnet user-secrets set "Automation:Token" "<token>"` in `TrackerApi/TrackerApi`.

Never put it in a committed file. Empty or shorter than 32 characters = switched off. To change
it, set a new value (the old one stops working at once). Every call is in the audit log as
"Automation (API token)".

## 2. What the tool calls

Send the header `X-Automation-Token: <token>` on each call. **Hosted, call the Netlify address**
(`https://<site>.netlify.app/api/...`), not Render's: the API only accepts requests that came
through Netlify. Locally, use `http://localhost:5080/api/...` (from Docker: `http://host.docker.internal:5080`).

| Step | Call |
|---|---|
| Start last month's report | `POST /api/reports/runs` with JSON `{ "kind": "Month", "previous": true }` (`previous` = the period before the current one; or give `date` = any day inside the month you want) → returns `id` |
| Wait | A 2-minute delay step (the job takes about a minute; fewer steps than a polling loop) |
| Read it | `GET /api/reports/runs/{id}` → `status` is `Done` or `Failed`; `note` says why text is missing; `document.narrative` is the text; `document.narrativeChecked` is `true` when its numbers passed the check |
| Excel | `GET /api/reports/export?from=2026-10-01&to=2026-10-31&format=xlsx` (no names) |

Weekly: `{ "kind": "Week", "date": "<any day in the week>" }`. Daily reports are written by code and
don't use Gemini.

## 3. Activepieces flow (about 5 steps a month)

1. **Schedule:** the 1st of each month, 08:00, time zone Asia/Manila.
2. **HTTP:** `POST` the run with `previous: true` (no date maths in the flow).
3. **Delay:** 2 minutes.
4. **HTTP `GET`** the run. If `status` is not `Done`, email yourself an alert and stop.
5. **Email** an **admin** (not the partners) with `document.narrative` in the body and the Excel attached
   (the export call, "download as file"). The admin reads it and forwards it: the number check proves
   a figure is in the data, not that it is used for the right thing.

Test it locally first (Activepieces in Docker, pointing at your own API), then hosted.
