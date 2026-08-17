# Auto-RoK Scheduler

A Windows desktop app that logs into **auto-rok.com** in an isolated browser and
presses **Start** / **Stop** on the *App Control* page automatically, on a schedule
you set. Multiple accounts are supported, each with its own credentials, device
name, and (optionally shared) browser session. A dark "control-room" dashboard lets
you watch the browser work.

Built with **C# / .NET 10 / WPF** and **Selenium** driving **Microsoft Edge**.

---

## What it does

- **Scheduled Start/Stop** — set times (and days) per account; the app fires them
  automatically and once per day per entry.
- **Manual Start/Stop** — big buttons to run an action right now.
- **Handles the site's quirks automatically** — it reacts to whatever is on screen:
  - signs in if logged out,
  - answers the **device name** prompt when asked (and skips it when the session is cached),
  - detects the **maintenance** page and refreshes until it clears,
  - dismisses informational pop-ups.
- **Isolated browser session** — runs in a dedicated Edge profile folder, completely
  separate from your normal browser. Two accounts can **share** a profile or stay
  **fully separate**.
- **Watch it run** — the browser opens in a small window you can see (unless you turn
  on headless mode in Settings).
- **Secure at rest** — passwords are encrypted with Windows DPAPI (per-user); they are
  never stored in plaintext.

---

## Requirements

- **Windows 10 / 11**
- **.NET 10 SDK** (to build) — a runtime-only install is enough to *run* a published build.
  Detected on this machine: SDK `10.0.400` at `C:\Program Files\dotnet`.
- **Microsoft Edge** — detected on this machine: `151.0.4129.86`.
  Selenium Manager downloads the matching `msedgedriver` automatically the first time
  it runs (needs internet access once).

---

## Build & run

From the repository root (`Auto RoK - Scheduler`):

```powershell
# build
dotnet build AutoRokScheduler\AutoRokScheduler.csproj

# run
dotnet run --project AutoRokScheduler\AutoRokScheduler.csproj
```

Or open **`AutoRokScheduler.sln`** in Visual Studio 2022+ (with the .NET desktop
workload) and press F5.

> If `dotnet` isn't on your PATH, use the full path, e.g.
> `"C:\Program Files\dotnet\dotnet.exe" build AutoRokScheduler\AutoRokScheduler.csproj`.

To produce a standalone build:

```powershell
dotnet publish AutoRokScheduler\AutoRokScheduler.csproj -c Release
```

---

## First run

1. Click **➕ Add account** in the sidebar.
2. Fill in the account. For the provided test account:
   - **Account name:** `My PC` (any friendly label)
   - **Login (email):** `you@example.com`
   - **Password:** `your-password`
   - **Device name:** `My PC`  *(what the site asks for on first sign-in)*
   - **Browser:** `Edge`
   - **Browser profile folder:** *leave blank* for its own isolated session.
3. Click **Save**. The account appears in the sidebar with a status dot.
4. Press **▶ START** or **■ STOP** to test it now, and watch the browser window.
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
  - No days selected = **every day**.
- Toggle an entry on/off with its switch.
- Entries fire once per day. The app checks every *poll* seconds (default 30).
- **Catch-up** (Settings): if on, a time that already passed earlier today still fires
  once when the app launches. Off by default.

The app must be **running** for schedules to fire (it's a foreground app, not a
Windows service). Leave it open, or add a shortcut to `shell:startup` to launch it at
login.

---

## Settings

- **Default browser** — Edge or Chrome (used for new accounts).
- **Run browser hidden (headless)** — off by default so you can watch it.
- **Quit browser after each action** — on by default; the session still persists on disk.
- **Catch up a missed schedule on launch** — see above.
- **Schedule poll (seconds)** — how often the scheduler checks (min 5).
- **Action timeout (seconds)** — overall budget for one run before it gives up.
- **Browser peek window** — size and screen position of the browser window.

---

## Where your data lives

Everything is under `%LocalAppData%\AutoRokScheduler\`:

- `config.json` — accounts, schedules, and settings. Passwords are DPAPI-encrypted
  (Base64) and only decryptable by **your** Windows user on **this** machine.
- `BrowserProfiles\<key>\` — the isolated Edge/Chrome user-data-dirs.

Because DPAPI blobs are machine/user-bound, copying `config.json` to another PC or user
will invalidate the stored passwords (you'd just re-enter them).

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
- **Watch the LOG panel** — every step (launch, sign-in, device name, maintenance,
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
```

## Notes on use

This tool automates **your own** auto-rok.com account for convenience. Keep your
credentials to yourself and use it within auto-rok.com's terms of service.
