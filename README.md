# Auto-RoK Scheduler

A Windows desktop app that logs into **auto-rok.com** in an isolated browser and
presses **Start** / **Stop** on the *App Control* page automatically, on a schedule
you set. Multiple accounts are supported, each with its own credentials, device
name, and (optionally shared) browser session. A dark "control-room" dashboard shows
each account's live status at a glance.

The automation runs **silently in the background** — no browser window pops up.

Built with **C# / .NET 10 / WPF** and **Selenium** driving **Microsoft Edge**.

---

## What it does

- **Scheduled Start/Stop** — set times (and days) per account; the app fires them
  automatically, once per day per entry.
- **Catches up on missed slots** — if a scheduled time passed while the app was
  closed, it runs when the app next opens (on by default), so a missed Stop isn't
  lost for the day.
- **Manual Start/Stop** — big buttons to run an action right now.
- **Live status on startup** — when the app opens it logs in, opens each account's
  machine and reads the real state, so the dashboard shows the truth immediately.
- **Per-account status dot** — STARTED (green), STOPPED (red), working/checking
  (grey), or UNKNOWN (a "?").
- **Handles the site's quirks automatically** — it reacts to whatever is on screen:
  - signs in if logged out,
  - answers the **device name** prompt when asked (and skips it when the session is cached),
  - detects the **maintenance** page and refreshes until it clears,
  - dismisses informational pop-ups.
- **Runs hidden** — the browser and its driver run headless by default; nothing
  appears on screen. (You can turn headless off in Settings to watch it, e.g. for
  debugging.)
- **Isolated browser session** — runs in a dedicated Edge profile folder, completely
  separate from your normal browser. Two accounts can **share** a profile or stay
  **fully separate**.
- **Secure at rest** — passwords are encrypted with Windows DPAPI (per-user); they are
  never stored in plaintext.

---

## Download & run (no build required)

1. Go to the [**Releases**](../../releases) page and download the latest
   `AutoRokScheduler-win-x64.zip`.
2. Extract it anywhere.
3. Double-click **`AutoRokScheduler.exe`** (it sits at the top of the extracted folder).

The download is **self-contained** — you do **not** need .NET installed. You do need
**Microsoft Edge** (preinstalled on Windows 10/11). The first run downloads the
matching Edge driver automatically, so it needs internet access once.

> Windows SmartScreen may warn about an unsigned app the first time — choose
> *More info → Run anyway*.

---

## Requirements

- **Windows 10 / 11**
- **Microsoft Edge** — Selenium Manager downloads the matching `msedgedriver`
  automatically the first time it runs (needs internet access once).
- **.NET 10 SDK** — only needed to **build from source** (not to run a release download).

---

## Build & run from source

From the repository root:

```powershell
# build
dotnet build AutoRokScheduler\AutoRokScheduler.csproj

# run
dotnet run --project AutoRokScheduler\AutoRokScheduler.csproj
```

Or open **`AutoRokScheduler.sln`** in Visual Studio 2022+ (with the .NET desktop
workload) and press F5. Or just double-click **`Launch Auto-RoK Scheduler.cmd`** in
the repo root — it finds the built (or published) exe and launches it, and falls back
to `dotnet run` if nothing is built yet.

> If `dotnet` isn't on your PATH, use the full path, e.g.
> `"C:\Program Files\dotnet\dotnet.exe" build AutoRokScheduler\AutoRokScheduler.csproj`.

To produce the same self-contained package that ships on Releases:

```powershell
dotnet publish AutoRokScheduler\AutoRokScheduler.csproj -c Release -r win-x64 --self-contained true
```

---

## First run

1. Click **➕ Add account** in the sidebar.
2. Fill in the account:
   - **Account name** — any friendly label (shown in the sidebar).
   - **Login (email)** — your auto-rok.com email.
   - **Password** — your auto-rok.com password (stored DPAPI-encrypted; never plaintext).
   - **Device name** — the name the site asks for on first sign-in.
   - **Browser** — Edge (or Chrome).
   - **Browser profile folder** — *leave blank* for its own isolated session.
3. Click **Save**. The account appears in the sidebar with a status dot, and the app
   reads its current status.
4. Press **▶ START** or **■ STOP** to run an action now, or add a schedule (below).
   The first successful sign-in caches the session in that profile folder, so later
   runs usually skip the login and device-name prompts.

### Sharing a browser session between accounts

Each account uses a browser **profile folder** (Edge `--user-data-dir`):

- **Blank** → the account gets its own isolated folder (named after the account).
- **Same value on two accounts** → those accounts **share** one folder (cookies,
  saved session, etc.). Type the same text in the *Browser profile folder* box for both.

Profiles live under
`%LocalAppData%\AutoRokScheduler\BrowserProfiles\<key>`.

---

## Scheduling

On a selected account, use the **Schedule** card:

- **➕ Add schedule** → pick a **time**, an **action** (Start/Stop), and **days**.
  - Pick **Daily** (or select no specific days) for **every day**.
- Toggle an entry on/off with its switch.
- Entries fire **once per day**. The app checks every *poll* seconds (default 30).
- **Editing** an entry re-arms it: if its new time has already passed today, it runs
  on the next check (via catch-up).
- **Catch-up** (Settings, on by default): a time that passed while the app was closed
  still fires once when the app next runs, catching up to the correct state.

The app must be **running** for schedules to fire (it's a foreground app, not a
Windows service). Leave it open, or add a shortcut to `shell:startup` to launch it at
login.

---

## Settings

- **Default browser** — Edge or Chrome (used for new accounts).
- **Run browser hidden (headless)** — **on by default**; the browser runs invisibly.
  Turn it off to watch the automation in a real window (handy for debugging).
- **Quit browser after each action** — on by default; the session still persists on disk.
- **Catch up a missed schedule** — on by default; see above.
- **Schedule poll (seconds)** — how often the scheduler checks (min 5).
- **Action timeout (seconds)** — overall budget for one run before it gives up.
- **Browser window** — size and screen position (only visible when headless is off).

---

## Where your data lives

Everything is under `%LocalAppData%\AutoRokScheduler\`:

- `config.json` — accounts, schedules, and settings. Passwords are DPAPI-encrypted
  (Base64) and only decryptable by **your** Windows user on **this** machine. The login
  email and device name are stored in plain text (they aren't secrets).
- `BrowserProfiles\<key>\` — the isolated Edge/Chrome user-data-dirs (cached sessions).

Because DPAPI blobs are machine/user-bound, copying `config.json` to another PC or user
will invalidate the stored passwords (you'd just re-enter them). `config.json` is never
committed to source control.

---

## If the site changes (adjusting selectors)

All URLs and element selectors are centralized in
**`AutoRokScheduler/Models/SiteConfig.cs`** — no automation logic is hard-coded to the
markup. If auto-rok.com changes its page, edit the relevant field there and rebuild.
Key fields:

| Field | Purpose |
|-------|---------|
| `AppControlUrl` | The App Control page URL |
| `EmailCss` / `PasswordCss` | Sign-in inputs |
| `LoginTriggerXPath` / `SignInSubmitXPath` | Open/submit the login modal |
| `Swal*` selectors | SweetAlert2 pop-ups (device-name prompt, info, maintenance) |
| `DeviceNameKeyword` / `DeviceNameIncorrectKeyword` | Detect the device-name prompt / rejection |
| `StartButtonXPath` / `StopButtonXPath` | The Start / Stop buttons |
| `RunningText` / `StoppedText` | Status detection |
| `MaintenanceKeyword` / `MaintenanceMaxMinutes` | Maintenance detection + retry budget |

`SiteConfig` is also persisted into `config.json`, so once the app has run you can tweak
selectors there too (delete `config.json` to regenerate defaults from the current code).

---

## Troubleshooting

- **"Sign-in did not succeed"** — check the email/password on the account.
- **"Device name … was not accepted"** — verify the *Device name* field matches what
  the site expects for that account.
- **First run is slow / driver error** — Selenium Manager is downloading `msedgedriver`;
  ensure the machine has internet the first time.
- **Nothing happens on schedule** — the app must be open; check the poll interval and
  that the entry is enabled and its day matches.
- **Want to see what it's doing?** — turn off headless in Settings to watch the browser,
  and read the **LOG** panel: every step (launch, sign-in, device name, maintenance,
  click, status) is logged there.

---

## Project layout

```
AutoRokScheduler/
  Models/        ScheduleEntry, Profile, AppSettings, SiteConfig, AppState, Enums
  Services/      SeleniumBot (automation), BotRunner, ScheduleEvaluator,
                 Storage, AppPaths, CredentialProtector (DPAPI)
  ViewModels/    MainViewModel + Profile/Schedule/Log VMs (MVVM-lite)
  Views/         MainWindow + Profile/Schedule/Settings dialogs
  Converters/    value converters for the theme
  Themes/        DarkTheme.xaml (the control-room look)
  Assets/        app.ico (application icon)
Launch Auto-RoK Scheduler.cmd   ← double-click to run
```

## Notes on use

This tool automates **your own** auto-rok.com account for convenience. Keep your
credentials to yourself and use it within auto-rok.com's terms of service.
