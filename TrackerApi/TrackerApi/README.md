# Computer System Tracker: API (C# / ASP.NET Core)

Backend for tracking computers in a public open-workspace office.

## What it tracks

| Need | Where |
|---|---|
| Who is using the computer, and why | `UsageSession` (`UserName`, `Purpose`) |
| Computer status (Available / InUse / NotWorking) | `Computer.Status` |
| Peripherals connected to each computer | `Peripheral.ComputerId` |
| Peripheral currently in use (headset, tablet, mic...) | `Peripheral.IsInUse` / `InUseSince` |
| Assist / help requests | `AssistTicket` |

## Run it (VS Code)

1. Install the **C# Dev Kit** extension in VS Code.
2. Open the `TrackerApi` folder in VS Code.
3. In a terminal inside that folder:

```bash
dotnet --version   # if the major version isn't 8, edit <TargetFramework> in TrackerApi.csproj (e.g. net9.0)
dotnet restore
dotnet run
```

4. Open http://localhost:5080/swagger and try the endpoints from the browser.

The SQLite file `tracker.db` is created on first run with 3 sample computers and 6 peripherals. To reset the data, stop the app and delete `tracker.db`.

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/computers` | Dashboard: status, current user, peripherals |
| GET | `/api/computers/{id}` | One computer |
| POST | `/api/computers` | Add a computer |
| PUT | `/api/computers/{id}/status` | Mark Available / NotWorking |
| GET | `/api/sessions/active` | Who is on a computer right now |
| POST | `/api/sessions/start` | Start a session (sets computer to InUse) |
| POST | `/api/sessions/{id}/end` | End a session (frees computer + peripherals) |
| GET | `/api/peripherals` | List (`?computerId=` / `?inUse=`) |
| POST | `/api/peripherals` | Add a peripheral |
| PUT | `/api/peripherals/{id}/assign` | Plug into a computer / unplug (null) |
| PUT | `/api/peripherals/{id}/use` | Mark in use / not in use |
| GET | `/api/assist` | Help requests (`?status=Open`) |
| POST | `/api/assist` | Ask for help from a computer |
| PUT | `/api/assist/{id}/status` | Open / InProgress / Resolved |

## Project layout

```
TrackerApi/
  Program.cs               entry point + service setup
  appsettings.json         config (database connection string)
  Models/                  entities (tables) + DTOs (request/response shapes)
  Data/TrackerDbContext.cs database access + seed data
  Controllers/             the HTTP endpoints
```

## Notes

- `EnsureCreated()` builds the database from the models. Once the schema starts changing often, we'll switch to EF Core **migrations**.
- CORS allows `http://localhost:5173` (Vite's default) so a React frontend can call this API.
