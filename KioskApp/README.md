# Computer System Tracker: Kiosk App (Windows desktop, C# / WPF)

The program that runs on each office computer.

1. **Free:** a fullscreen screen tells visitors how to get this PC: scan the QR code at the entrance, register and pick this PC on their phone, then show the QR pass at the help desk. A **Need help?** button asks staff for help.
2. **Reserved:** someone picked this PC and is on their way to the help desk.
3. **In use:** when staff scan the pass, the screen hides and a small box stays in the corner (name, timer, **Need help?**, **Log out**). The visitor then uses the computer normally.
4. **Logged out, idle for 20 minutes, or out of time:** the lock screen comes back.

**Staff-only form on the PC:** for a visitor whose phone died, staff press **Ctrl + Shift + Q**, enter the PIN and choose **Register a visitor on this computer**. It's the same form as the phone's (name, age group, sex, sectors, services), and the session starts here straight away.

Everything is recorded through TrackerApi, so the staff dashboard shows the computer as In use, with the visitor's name and services, within a few seconds.

## Requirements

- Windows 10 or 11
- To build: the .NET SDK (same version as TrackerApi)
- To run on a PC without the SDK: build self-contained (see Deploying)
- TrackerApi running and reachable from this PC

## Try it on your own PC

1. Start the API (in the inner `TrackerApi/TrackerApi` folder, where `TrackerApi.csproj` is): `dotnet run`
2. In this folder (`KioskApp`):

```
dotnet run
```

3. The first time, a **Set up this computer** screen asks for a **pairing code**. An admin gets one for the PC (for example PC-01) from the API: sign in at http://localhost:5080/swagger, then call `POST /api/computers/1/pairing-code`. Type the code in and press **Pair this computer**. The pairing is remembered. (A dashboard button for this comes later.)
4. To start a session without the phone app (phase 3), use the staff-only form: **Ctrl + Shift + Q**, then **Register a visitor on this computer**. (With `kioskMode` `false` and no `staffPin`, any PIN works.) Then check the dashboard at http://localhost:5173.

If `dotnet run` says the target framework is not installed, open `KioskApp.csproj` and change `net8.0-windows` to your version plus `-windows`, for example `net9.0-windows`.

## Settings (`kiosk.settings.json`, next to the .exe)

| Setting | Meaning | Default |
|---|---|---|
| `apiUrl` | Address of TrackerApi. On the office PCs this must be the server's address, not `localhost`. | `http://localhost:5080/` |
| `kioskMode` | `false`: normal window (for testing). `true`: fullscreen, always on top, cannot be closed. | `false` |
| `staffPin` | PIN for staff (Ctrl+Shift+Q). In kiosk mode, an empty PIN disables the shortcut. | empty |
| `idleTimeoutSeconds` | Log out after this long with no keyboard or mouse use anywhere in Windows. `0` = never. | `1200` (20 min) |
| `idleWarningSeconds` | Show a warning during the last N seconds. | `60` |
| `refreshSeconds` | How often to check the tracker for changes made by staff. | `5` |

Pairing saves which PC this is in `C:\ProgramData\ComputerTracker\computer-id.txt`, and the PC's own secret key in `kiosk-key.txt` next to it. The key only works as this PC. If you think it was copied, pair the PC again: that cancels the old key.

## Staff shortcut

Press **Ctrl + Shift + Q** on the login screen and enter the PIN. The staff menu can register a visitor on this computer, unpair this computer (the setup screen shows again), or exit the kiosk.

## Deploying to the office computers

1. Make the API reachable from the other PCs. On the server PC, start it with `dotnet run --urls http://0.0.0.0:5080` and allow port 5080 in Windows Firewall. (CORS is not needed for this app.)
2. Build one copy that runs without .NET installed:

```
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

3. Copy the `publish` folder to each PC (for example `C:\ComputerTracker`).
4. On each PC, edit `kiosk.settings.json`: set `apiUrl` to the server's address, `kioskMode` to `true`, and a `staffPin`.
5. Run `ComputerTrackerKiosk.exe` once. Type the pairing code an admin created for that PC, then test.
6. Start it automatically: press Win + R, type `shell:common startup`, and put a shortcut to the .exe in that folder.

## Smart App Control

This is an unsigned program, so Windows **Smart App Control** blocks it where it is turned on, as happened with the API. Either turn it off on those PCs, or sign the .exe with a code-signing certificate.

## What this does not do

- **It is not a full Windows lockdown.** The login screen covers the desktop, but it cannot block Ctrl+Alt+Del, Task Manager or the Windows key. For that, use Windows **Assigned Access** (kiosk mode) or Group Policy on the office PCs.
- **Logging out does not close the user's programs or browser sign-ins.** For privacy, run the PCs under a Windows account that is reset or signed out between users.
- **Peripherals are not detected yet.** The session box does not show the USB devices on the computer.

## Files

```
KioskApp/
  KioskApp.csproj          project file
  kiosk.settings.json      settings
  App.xaml / .cs           starts the app, decides which window is visible, shared styles
  LockWindow.xaml / .cs    the fullscreen login, setup and staff screens
  SessionBar.xaml / .cs    the small box shown while someone is logged in
  HelpWindow.xaml / .cs    the "How can we help?" dialog
  Core/                    all the logic, with no screen code
    KioskController.cs     decides what to show; login, logout, idle timeout, help
    ApiClient.cs           calls TrackerApi
    IdleSource.cs          asks Windows how long the keyboard and mouse have been idle
    KioskSettings.cs       reads the settings
    Models.cs              the data shapes from the API
```
