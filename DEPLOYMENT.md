# Deploying MISA

There are two ways to run MISA. Both use the same code; only settings differ.

| | **A. One PC at DTC** | **B. Free cloud services (testing)** |
|---|---|---|
| Website | Served by the API itself | Netlify |
| API | `dotnet` on a PC at DTC | Render (Docker, free tier) |
| Database | SQLite file (`tracker.db`) | Supabase PostgreSQL (free tier) |
| Internet needed | No (only the Wi-Fi/LAN) | Yes, for phones, kiosks and the help desk |
| HTTPS | Needs a certificate (see A.4) | Included |
| Backups | Daily copy, automatic | Supabase's (see B.6) |

The kiosk PCs are set up the same way for both (section C).

---

## A. One PC at DTC

1. **Build the website into the API** (on any PC with Node.js):
   ```bash
   cd tracker-web/tracker-web
   npm install --legacy-peer-deps
   npm run build:api        # writes TrackerApi/TrackerApi/wwwroot
   ```
2. **Publish the API** for the server PC:
   ```bash
   cd TrackerApi/TrackerApi
   dotnet publish -c Release -r win-x64 --self-contained true -o publish
   ```
   Copy the `publish` folder (it includes `wwwroot`) to the server, e.g. `C:\MISA`.
3. **Settings** (`appsettings.json` next to `TrackerApi.exe`, or environment variables):
   - `ASPNETCORE_ENVIRONMENT=Production` (no Swagger; `RequireHeartbeat` on)
   - `Backup:Folder`: ideally a second disk or a USB drive; `Backup:KeepDays` (30)
   - Start it: `TrackerApi.exe --urls http://0.0.0.0:5080` and allow port 5080 in Windows Firewall.
   - First admin: `TrackerApi.exe create-admin <username> "<full name>"`.
4. **HTTPS.** Without it, staff passwords and kiosk keys cross the Wi-Fi in plain text, and the
   help-desk camera only works on the server itself. Either put the server and kiosks on a
   **staff-only network** (visitors' Wi-Fi separate), or get a certificate (e.g. from DICT's IT
   unit) and start with `--urls https://0.0.0.0:443` plus Kestrel certificate settings, then set
   `Hosting:RequireHttps=true`.
5. Phones open `http://<server address>:5080/#/visit` (print the poster from `/#/poster` opened
   with that address). Kiosks use `apiUrl: http://<server address>:5080/`.

## B. Netlify + Render + Supabase (testing)

The browser only ever talks to Netlify. Netlify forwards `/api/*` to Render and **signs** each
request; the API refuses anything unsigned, so nobody can go around the website.

```
phone / dashboard / kiosk ──https──▶ Netlify ──/api/* (signed)──▶ Render (API) ──▶ Supabase
```

1. **Supabase**: create a project (region: Singapore). Let Supabase generate the database
   password (letters and digits: a `;` or `'` would break the connection string). *Connect →
   type **.NET** → **Session pooler*** (IPv4; Render has no IPv6). Put in the password and add
   `;SSL Mode=Require`. It looks like
   `User Id=postgres.<ref>;Password=<pw>;Server=aws-0-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;SSL Mode=Require`.
   The API can't read the `postgresql://…` form; rebuild it in this form if that's all you see.
   Don't use the transaction pooler (port 6543): it breaks prepared statements.
2. **A shared secret** for Netlify → Render: a long random string, e.g. in PowerShell
   `$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)`
   (not `Get-Random`, which isn't meant for secrets).
3. **Render**: *New → Blueprint →* this repository (`render.yaml`). Fill in:
   `ConnectionStrings__Default` (step 1), `Hosting__ProxySignatureSecret` (step 2),
   optionally `Gemini__ApiKey`. Deploy. The API creates the tables on first start, and keeps
   the staff sign-in keys there too, so a restart or a free-plan sleep doesn't sign staff out.
   Open `https://<service>.onrender.com/health` → `{"status":"ok"}`.
4. **Netlify**: *Add new site → Import from Git*, **Base directory** `tracker-web/tracker-web`.
   In `netlify.toml`, replace `YOUR-RENDER-SERVICE` with the Render service name and push.
   *Site configuration → Environment variables*: `API_SIGNATURE_TOKEN` = the secret from step 2,
   scope **Runtime** (or "All").
5. **First admin**: Render's free plan has no *Shell* tab, so create it from your own PC,
   straight into Supabase. In PowerShell, in `TrackerApi/TrackerApi`:
   ```powershell
   $env:Database__Provider = "Postgres"
   $env:ConnectionStrings__Default = Read-Host "Supabase connection string"   # typed here, not saved
   dotnet run --no-launch-profile -- create-admin <username> "<full name>"
   Remove-Item Env:ConnectionStrings__Default, Env:Database__Provider
   ```
   (If the API is running on this PC, `dotnet run` can't build; close it first.)
6. **Check after the first deploy**:
   - `https://<service>.onrender.com/api/public/computers` directly → **403** (unsigned: good).
   - Sign in at `https://<site>.netlify.app`, register on a phone, then *Audit log*: the IP
     address column should show the phone's public address, not a Netlify/Render one. If it
     shows a Netlify address, set `Hosting__TrustedProxyHops` to `3`; if it is blank or odd, `1`.
   - Kiosks: `apiUrl: https://<site>.netlify.app/`, `visitorUrl: https://<site>.netlify.app/#/visit`,
     and `refreshSeconds: 15` (see "Request budget" below).
   - Restart the service (*Manual Deploy → Restart*): staff who were signed in stay signed in.
7. **Request budget (Netlify free plan).** Every request goes through Netlify, and the free
   plan has 300 credits a month with a hard limit (web requests cost 2 credits per 10,000:
   about 1.5 million requests). Rough use, 9 hours a day, 22 days a month:
   | | Every 5 s (default) | Every 15 s |
   |---|---|---|
   | 12 kiosks (heartbeat, `refreshSeconds`) | 1.7 million | 0.57 million |
   | Help-desk dashboard (one `/api/board` request per refresh) | 0.14 million | |
   | Phones | 1 request every 3 s while the visit page is on screen | |

   The website stops asking while a page is hidden (another tab, a locked phone). Kiosk PCs
   left on overnight still send heartbeats; turn them off after hours. Watch *Team →
   Usage & billing* in the first week. A kiosk heartbeat every 15 s is still well inside the
   30 s after which a PC shows as Offline.
8. **Rate limits behind one address.** Every phone on DTC's Wi-Fi reaches the cloud from DTC's
   one public address. Limits count per phone (a cookie), with a shared cap per address of
   `RateLimits__PerNetworkFactor` (10) times each limit: 60 registrations in 10 minutes for
   the whole centre. For an event with more people arriving at once, raise it on Render.
9. **Backups**: Supabase's free plan has no backups you can download. Before real data, either
   upgrade, or export regularly (Supabase dashboard → Database → Backups / `pg_dump`), and keep
   exports no longer than a year (DTC's rule).

## C. The kiosk PCs

1. Publish the kiosk app (KioskApp README, "Deploying") and copy it to `C:\ComputerTracker`.
2. `kiosk.settings.json`: `apiUrl`, `kioskMode: true`, `staffPin` (8+ characters, hashed on
   first start), `visitorUrl`.
3. In an administrator PowerShell window, from `KioskApp\setup`:
   ```powershell
   .\Setup-KioskPC.ps1 -WhatIf        # see what it would change
   .\Setup-KioskPC.ps1 -KeepAwake     # do it
   # sign in once as MISAVisitor, sign out, then:
   .\Setup-KioskPC.ps1 -RestrictionsOnly
   ```
   It creates a standard visitor account, starts the kiosk app at sign-in (and again within a
   minute if it is closed), turns off Task Manager / Lock / Run / Control Panel / Windows keys /
   command prompt for that account, and limits who can read the kiosk's files. Automatic sign-in:
   Microsoft's Sysinternals **Autologon** (`-AutologonExe <path>`), which stores the password
   encrypted.
4. Pair the PC from the dashboard (*Computers → Details → Create pairing code*), sign in as the
   visitor account, test a session end to end.
5. **BIOS/UEFI**: set a password and turn off booting from USB; otherwise a visitor can start
   another operating system and bypass Windows entirely.

### What this can't hide

The kiosk app runs as the visitor account, so that account can read `kiosk.settings.json` (the
PIN's hash) and the PC's key. The restrictions above make that hard (no command prompt, no Run,
no registry editor), and the key only ever acts as that one PC. The PIN hash is protected by a
slow hash, but a short PIN can still be guessed offline: use **8+ characters, not only digits**.
For a hard lockdown, Windows **Assigned Access / Shell Launcher** (Enterprise/Education editions)
replaces the desktop with the kiosk app.

## Production checklist

- [ ] `ASPNETCORE_ENVIRONMENT=Production` (Swagger off, heartbeats required)
- [ ] HTTPS, or staff/kiosks on a separate network (A.4)
- [ ] `Hosting:*` set for the cloud (B.3); unsigned requests get 403 (B.6)
- [ ] First admin created; `devadmin` and test accounts not present
- [ ] Real PC list entered (the sample PC-01..PC-12 are seed data)
- [ ] Backups going somewhere other than the server's own disk; kept ≤ 1 year
- [ ] Kiosk PCs: setup script, 8+ character PIN, BIOS password
- [ ] DICT-CAR has approved where visitor data is stored (especially for B)
