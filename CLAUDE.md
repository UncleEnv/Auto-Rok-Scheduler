# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows desktop app (C# / .NET 10 / WPF) that drives **Microsoft Edge via Selenium** to press
Start / Stop on the App Control page of auto-rok.com, on a schedule. Multiple accounts, each with
its own isolated browser session. Single-project solution; no test project.

## Commands

`dotnet` is **not on PATH** in this environment — always invoke it by full path:

```bash
# build
"/c/Program Files/dotnet/dotnet.exe" build AutoRokScheduler/AutoRokScheduler.csproj -v q --nologo

# run
"/c/Program Files/dotnet/dotnet.exe" run --project AutoRokScheduler/AutoRokScheduler.csproj

# single-file self-contained release exe (~66 MB, what ships on Releases)
"/c/Program Files/dotnet/dotnet.exe" publish AutoRokScheduler/AutoRokScheduler.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

`Launch Auto-RoK Scheduler.cmd` in the repo root finds the built or published exe and runs it.

### Testing

There is **no test project**. Two approaches that work here:

- **Pure logic** (e.g. `ScheduleEvaluator`) — create a throwaway console project in `%TEMP%` whose
  `.csproj` `<Compile Include="...">`s the real source files by absolute path, assert against them,
  then delete it. Keeps the repo clean while exercising shipping code.
- **The app itself** — launch the exe from PowerShell with `Start-Process -PassThru`, sleep, check
  `HasExited`, screenshot the desktop, then `CloseMainWindow()` (graceful — triggers `Save()`) or
  `Stop-Process`. Force-killing mid-run orphans `msedge`/`msedgedriver` processes that keep the
  browser profile dir locked, which makes the *next* run fail; clean them up by filtering
  `Win32_Process` command lines for `AutoRokScheduler` so the user's real Edge is untouched.

## Architecture

```
Views (XAML + code-behind)  ->  MainViewModel  ->  BotRunner  ->  SeleniumBot  ->  Edge/Chrome
                                     |                                  ^
                                  Storage  <->  config.json          SiteConfig
```

- **`ViewModels/MainViewModel.cs`** is the hub: owns profiles, the scheduler tick, the pending
  queue, logging, and persistence. MVVM-lite (`ObservableObject`, no framework).
- **`Services/BotRunner.cs`** is a thin async wrapper that moves work to a background thread.
- **`Services/SeleniumBot.cs`** (the largest file) does all browser work and is *reactive*, not a
  fixed script: `ReachControls` inspects whatever is on screen each pass and handles sign-in, the
  device-name prompt, SweetAlert2 pop-ups and the maintenance page in whatever order they appear.
  This matters because the site sometimes skips login/device-name entirely when the session is cached.

### Invariants that are easy to break

**Runs are serialized in two independent places.** `MainViewModel.IsBusy` gates the UI/scheduler,
and `BotRunner._gate` (`SemaphoreSlim(1,1)`) gates the actual browser work. Any work on concurrency
must change *both*.

**Why serialization exists:** each account runs in its own `--user-data-dir`
(`AppPaths.BrowserProfileDir(profile.EffectiveProfileKey)`). A Chromium user-data-dir may only be
open in **one** browser process at a time, or the launch fails with "user data directory is already
in use". Accounts sharing a profile key deliberately share cookies and must never overlap.

**Scheduling is arm-then-drain, not evaluate-when-idle.** `OnTick` arms due entries into `_pending`
on *every* tick even while busy, and `DrainPending` runs them when free (also kicked from
`RunAsync`'s `finally` via the dispatcher, so a deferred slot starts in seconds). `ScheduleEvaluator.IsDue`
takes `awakeSince` and has **no expiry window** — an earlier 3-minute window silently dropped slots
the app was merely too busy to notice. Semantics:

| Situation | Behaviour |
|---|---|
| App awake, runner busy | Always runs, queued then drained. Not configurable. |
| App was closed during the slot | Governed by `AppSettings.CatchUpMissed` (**default off**) |
| Machine slept through the slot | Treated as closed — a large tick gap resets `awakeSince` |

`LastFired` is stamped at **drain**, not arm, so a slot still queued when the app closes isn't
falsely recorded as fired. Several slots stacked on one account collapse to the latest.

**`SiteConfig` is persisted into config.json.** Changing a selector default in
`Models/SiteConfig.cs` will **not** affect an existing install — the stored copy wins. Delete
`config.json` to regenerate defaults. All URLs/selectors live there; no markup is hard-coded in
`SeleniumBot`.

**`RenderOptions.ProcessRenderMode = SoftwareOnly`** in `App`'s constructor is load-bearing, not a
performance tweak: some machines have a broken system `d3d9.dll` and the hardware path crashes WPF
at startup. Don't remove it.

**Use `Dispatcher.InvokeAsync`, never `BeginInvoke`.** `BeginInvoke` dispatches through
`DynamicInvoke`, which rewraps every failure as an opaque `TargetInvocationException`
("Exception has been thrown by the target of an invocation") and destroys diagnosability.

**`Storage.Save` never throws.** It retries (antivirus/indexer locks on `config.json` are real),
falls back to a direct write, and reports through the `SaveFailed` event — because it's called from
timer ticks where an exception becomes a modal crash dialog.

### Data and secrets

Everything user-specific lives in `%LocalAppData%\AutoRokScheduler\` — never in the repo or the exe:
`config.json` (accounts, schedules, settings, SiteConfig), `BrowserProfiles\<key>\`, `crash.log`.

Passwords are DPAPI-encrypted (`CredentialProtector`, `DataProtectionScope.CurrentUser`). This is
exactly why the folder is **Local, not Roaming** — the ciphertext is bound to this user on this
machine, so roaming it would make passwords undecryptable. `config.json` is gitignored.

Errors surface through `CrashLog`, which unwraps the inner-exception chain to the real cause and
appends full stack traces to `crash.log`.

## Releasing

1. Bump `<Version>` in `AutoRokScheduler/AutoRokScheduler.csproj`.
2. Commit, then `git tag -a vX.Y.Z -m "..."` and push both branch and tag.
3. `dotnet publish` as above; the artifact is
   `AutoRokScheduler/bin/Release/net10.0-windows/win-x64/publish/AutoRokScheduler.exe`.
4. Create the GitHub release and upload that exe as an asset named `AutoRokScheduler.exe`.

**`gh` is not installed on this machine.** Use the GitHub REST API with the credential Git already
pushes with (`git credential fill`, never echo the token). In Windows PowerShell, send the JSON body
as `[System.Text.Encoding]::UTF8.GetBytes($body)` — a string body is sent as ISO-8859-1 and GitHub
rejects it with "Problems parsing JSON".

## TO-DO

### Run multiple accounts simultaneously

Today every action is strictly sequential, so N accounts scheduled at the same minute run one after
another (~20–60s each, capped by `ActionTimeoutSeconds`). The queue guarantees none are *dropped*,
but the last one starts minutes late. Per-account browser isolation already exists, so the
groundwork is done.

Required changes:

- Replace the single global `MainViewModel.IsBusy` with per-account busy state. The UI already
  models this (per-account status dots), so the view layer mostly fits already.
- Replace `BotRunner`'s single `SemaphoreSlim(1,1)` with a semaphore **keyed on
  `Profile.EffectiveProfileKey`**. This is the critical constraint: accounts with *different* keys
  may run in parallel, but accounts *sharing* a key must still take turns or the browser refuses to
  launch (see the user-data-dir invariant above).
- Give each run its own `CancellationTokenSource` (currently one shared `_cts`), and make `Cancel`
  target a specific run.
- `DrainPending` must be able to start a run for account B while A is still going, instead of
  returning early on a global busy flag.
- Guard concurrent `Storage.Save()` calls from runs completing simultaneously.

Trade-off worth surfacing to the user before building: each headless Edge is roughly 200–400 MB of
RAM, so N concurrent accounts cost N × that.
