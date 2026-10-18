# Playdex for Windows

The Playdex app for Windows: your games and your Playdex history, one app with two modes.

- **Desktop mode:** a normal window and the tray, mouse and keyboard first, with the management screens (Manage, Settings).
- **Big Picture:** full screen and controller first, the 10-foot UI. Every return from a game, the Guide button and a relaunch bring it back home. It is also what the Full Screen Experience shows: the FSE home app (`../Launcher/Host`) is a small package that starts Playdex with `--big-picture`.

The mode is saved in the config (`shell.mode`, `desktop` or `bigPicture`) and switched in place, without a restart: Settings → This app → Switch to Big Picture, Quick settings → Desktop mode (Quick settings offers the other mode in either one), or the tray menu. A plain start, a click on the tray icon and starting with Windows open the saved mode; `--big-picture` opens Big Picture for that start only.

It shows the React UI in `../Launcher/ui` (`window.__PLAYDEX_MODE__` is `desktop` or `fse`) and compiles in the window, page bridge, controller input and quick settings from `../Launcher/Shell`; sessions, the queue, watchers, Steam and the library are `../Playdex.Core`. It replaced both the WinForms Wrapper and the standalone launcher (`docs/launcher/wrapper-merge-plan.md`).

Windows only, unpackaged and self-contained (WinUI 3, WebView2, Native AOT in Release). It is not in `Playdex.sln`, because the Windows App SDK XAML compiler only runs on Windows. `global.json` here allows any .NET 10 SDK (the root one pins 10.0.302).

## Run (dev)

```powershell
cd src/Launcher/ui; bun install; bun run dev        # http://localhost:5190
cd ../../Desktop; dotnet run                        # Debug builds load the dev server; --dist serves ../Launcher/ui/dist
```

Generated per-game launchers need `Playdex.Run.exe` next to `Playdex.exe`; a dev build without it skips them (logged).

Release build, measurements and self-tests:

```powershell
cd src/Launcher/ui; bun run build                   # Release serves ui/dist from https://app.playdex/
cd ../../Desktop
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"   # Native AOT needs vswhere to find link.exe
dotnet publish -c Release -r win-x64
powershell -File ../../tools/measure-big-picture.ps1     # cold start, input latency, idle memory and CPU in Big Picture
bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/Playdex.exe --selftest-job
```

## What it does

- **Window and tray.** One instance; starting it again opens the window. Closing the window releases it (and WebView2), and Playdex keeps running in the tray: click to open, right-click for Open, Switch to Big Picture / desktop mode, and Quit. Switching modes closes the window and opens one in the other mode. `--background` starts it in the tray, which is how it starts with Windows (Settings → This app).
- **Library and play.** Home and Library, in either mode. Games added to Playdex start directly and their sessions are recorded here (`SessionManager`, `ConfiguredGameWatcher`). Their save backups show on the game's page (with Back up saves and the daily backup switch), on Home's lead tile and on their Library cover, as the Wrapper's indicator did.
- **Manage** (the Wrapper's list): add a game from its .exe, edit its name, Steam/API id, launch options, platform and daily backup, add it to Steam as a non-Steam shortcut with SteamGridDB art (Steam must be closed), back up its saves with Ludusavi, remove it. Changes reload the page with the new library.
- **Settings** (the Wrapper's dialog): API URL and key, default platform, SteamGridDB key, Ludusavi path, Big Picture, start with Windows, quit, and updates in the header. Typed settings save together from the save bar; start with Windows applies at once. Keys are never sent to the page. Big Picture has a Settings tab too, with what a controller can change (updates, desktop mode, start with Windows) and the rest shown as set up or not; a dot on the tab means an update is ready.
- **Background:** at startup every game's launcher is regenerated from `Playdex.Run.exe` (replacing the Wrapper's copies of itself, so Steam shortcuts keep working), queued sessions are uploaded, and the daily Ludusavi backups run every 15 minutes as they did in the Wrapper (`state/ludusavi-backups.json`, kept in place).
- **Updates.** Installed builds (Release, `Playdex.Run.exe` next to `Playdex.exe`, a folder the user can write) check the download feed (`download.playdex.dev`, see `docs/windows-releases.md`) at startup and daily, download `playdex-windows-v<version>.zip` with its `.sha256`, verify and extract it into `%LOCALAPPDATA%\Playdex\updates`, and offer "Restart to update" (also as a Windows notification while no window is open). Installing copies `Playdex.Run.exe` out of the app folder and starts it with `--apply-update`: it waits for Playdex to exit, swaps the app folder for the new one, starts it and waits up to 60 s for it to confirm (`--update-health`); otherwise the old folder goes back and starts again, and the next start reports why. The helper logs to `updates\update.log`.
- **Full Screen Experience.** Settings → This app → Install downloads `playdex-launcher-v<version>.msix` and `playdex-launcher.cer` from the release Playdex came from, imports the certificate into Trusted People (one admin prompt), installs the package for the user and opens Settings → Gaming. Developer Mode must be on (the gamingHome capability is only granted with the developer-mode SCCD); without it, Install opens Settings → For developers. Development builds only report whether the package is installed. The package starts the `Playdex.exe` recorded in the config (`shell.executablePath`, written by installed and published builds at start), else `%LOCALAPPDATA%\Programs\Playdex\Playdex.exe`.

The page talks to it with `{type:"desktop", id, method, params}` (see `DesktopHost.cs` and `../Launcher/ui/src/host/desktop.ts`); the reply is `{type:"desktop-result", id, ok, result | error}`.

## Flags

| Flag | Effect |
|---|---|
| `--big-picture` | Open in Big Picture for this start, whatever the saved mode (the FSE package passes it); a running Playdex switches to it |
| `--background` | Start in the tray without a window (how it starts with Windows) |
| `--dev`, `--ui-url=URL` | Load the dev server (the default in Debug) |
| `--dist` | Debug build: serve `ui/` instead of the dev server |
| `--exit-on-ready` | Exit once the UI is interactive (cold-start runs) |
| `--bench-input=N` | Inject N presses through the input pipeline and log latency percentiles |
| `--launch=GAMEID` | Launch a game once the UI is ready (also `playdex-launcher://launch/GAMEID`). Ids: `steam-<appid>`, `playdex-<game id>` |
| `--no-suspend` | Keep the page running while a game is in front in Big Picture |
| `--update-health=PATH` | Started by the updater: confirm a healthy start by writing PATH |
| `--selftest-job` | Job Object exit-detection self-test |
| `--selftest-quick-settings[=toggle]` | Log the sound outputs, volume, HDR, refresh rates and the FPS counter; `=toggle` switches the sound output, changes the volume and mutes, switches HDR and refresh rate of each display, checks, and switches back, then toggles the FPS counter twice |

## Configuration

- API key and URL: `PLAYDEX_API_KEY` and `PLAYDEX_API_URL`, or `api.apiKey` and `api.baseUrl` in the install's config, `%LOCALAPPDATA%\Playdex\config.json` (shared with the Full Screen Experience entry and `Playdex.Run.exe`). Default URL `https://dash.playdex.dev/api`; a config that still has a retired default (`playdex-api.ejwan.dev`, the Wrapper's `playdex.ejwan.dev`) moves to it.
- The config is created on first start from the Wrapper's `%LOCALAPPDATA%\PlaydexWrapper\config.json` and the launcher's old `%LOCALAPPDATA%\PlaydexLauncher\config.json`, which are left in place. The offline queue moves from `PlaydexLauncher` once.
- The log (`desktop.log`), `metrics.jsonl` and the offline `queue.jsonl` live in `%LOCALAPPDATA%\Playdex`. Crashes on any thread are logged there before the process ends; Windows also keeps them in Event Viewer → Windows Logs → Application (events 1000 and 1026).
- `PLAYDEX_INPUT_FORCE_POLL=1`: diagnostic that polls every XInput slot at the foreground rate, as if a pad were connected.
- `PLAYDEX_INPUT_HZ=N`: foreground XInput poll rate (default 250), for measurements.

## Library

At startup the app lists the games on this PC and injects them into the page as `window.__PLAYDEX_LIBRARY__` (a plain browser keeps the demo library):

- **Steam:** every fully installed app in each Steam library folder (`libraryfolders.vdf`, `appmanifest_*.acf`), minus redistributables and runtimes. Launched with `steam://rungameid`; the host records the session.
- **Playdex:** every game added to Playdex (the config's games) whose executable exists. Launched directly with its launch options and followed to exit by its monitor settings (`ConfiguredGameWatcher`, `ProcessMonitor`); the host records the session. The game's lock (`GameLock`) is held while it runs, so starting it from its Steam shortcut at the same time fails instead of recording it twice. A game added to Playdex with a Steam app id replaces the same Steam entry.

Artwork is read in place from Steam, per image, in this order: the game's non-Steam shortcut grid art (`Steam\userdata\<user>\config\grid\<shortcut id>{p,_hero,_logo,}.*`, where the Playdex Wrapper puts SteamGridDB art; the shortcut id comes from `shortcuts.vdf`), custom grid art on its Steam app id, then Steam's artwork cache (`Steam\appcache\librarycache\<appid>\[<hash>\]`). Each user's grid folder is served as `https://<user id>.steam-grid.playdex/` and the cache as `https://steam-art.playdex/`; nothing else under `userdata` is exposed. Newer apps are only on the CDN under per-image hashes, so the fixed `cdn.cloudflare.steamstatic.com/steam/apps/<id>/...` URLs 404 for them; those URLs remain the fallback for each image none of these has.

New games appear the next time Playdex starts (games added in Manage reload the page at once).

## Playdex data

The Home rail and the hours on game tiles are the user's Playdex data, nothing local: `GET /api/launcher/summary` (src/Api/Modules/Launcher), which takes the install's API key (`ApiKey` auth scheme in the Api; the key reaches nothing else beyond capture). The launcher sends its time zone (Windows id, with the UTC offset as a fallback) so "today" and the 7-day bars are local days. The response has today, the last 7 days, the week's top games, the newest sessions, all-time totals, the streak, and every played game with its identities (`steam:<appid>`, or what a local game is captured with), which the page matches to library games.

`PlaydexSummary` (Core) keeps the last response in `%LOCALAPPDATA%\Playdex\playdex-summary.json` and the host injects it as `window.__PLAYDEX_SUMMARY__` before the page runs, so the rail has data on its first frame and offline. It is fetched again when the page loads, once a game starts running and when it ends, and every 5 minutes while the window is active; a change is posted as `{type:"playdex-summary", summary}`. Without an API key nothing is fetched, the cache is removed, and the rail says where to add the key.

## Now playing

While a game runs, the top bar shows it with its play time; A on it switches to the game. The game is the one launched here, or else one started elsewhere (`RunningGames` in Core): a game added to Playdex whose process runs (matched by its monitor settings, or its launch executable), or Steam's running app (`HKCU\Software\Valve\Steam\RunningAppID`; no play time, Steam doesn't say when it started). The host checks every 3 s while its window is active and posts `{type:"now-playing", gameId, since}` (epoch ms or null; gameId null when nothing runs) when it changes.

Resume (`{type:"resume-game"}`) brings back the window that was in front when the launcher was called up over the game (Guide, Game Bar "Home"), else a window of the game's process, else the topmost app window below the launcher that isn't Steam's. The launch overlay offers it too (A). If no window is found the page shows a toast (`{type:"resume-failed"}`).

## Quick settings

Menu on the controller (or the sliders button in the top bar, or `m`) opens a panel over the current screen. B or Menu closes it.

- **Volume:** the default output's master volume, the level the taskbar's slider shows (Core Audio's `IAudioEndpointVolume`). Left / right step it by 5, a pointer clicks or drags along the bar, and A mutes and unmutes; changing the level unmutes, as the taskbar does. A held stick sends a stream of `{type:"volume"}` messages: the host collapses queued ones into the latest and answers only when one fails. Focused when the panel opens.
- **Sound output:** the active playback devices; picking one makes it the default for every role (console, multimedia, communications), as Settings → Sound does. Listed with Core Audio's `IMMDeviceEnumerator`; set with `IPolicyConfig`, which is undocumented but stable since Windows 7 (EarTrumpet and similar tools use it).
- **HDR:** one switch per active display that supports HDR, the same switch as Settings → Display → Use HDR (`DisplayConfigSetDeviceInfo` with `DISPLAYCONFIG_SET_HDR_STATE`, falling back to the pre-24H2 advanced color call).
- **Refresh rate:** per display, the rates offered at its current resolution and colour depth (`EnumDisplaySettingsEx` on its GDI device); picking one keeps the resolution and is saved, as in Settings → Display → Advanced display (`ChangeDisplaySettingsEx` with `CDS_UPDATEREGISTRY`). Rates are whole Hz: this API reports 59.94 Hz as 59.
- **FPS counter:** shows or hides the NVIDIA App's statistics overlay (FPS, GPU, CPU, latency) by pressing its hotkey, Alt+R unless rebound; the hotkey is read from the overlay's settings (`HKCU\Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS`, `PMOCOverlayHKey*`). Listed only while the NVIDIA overlay runs. NVIDIA has no API for it and keeps no readable on/off state, so it is a toggle, not a switch.
- **Playdex:** Desktop mode in Big Picture, Big Picture in desktop mode (`{type:"desktop", method:"mode.set"}`): saves the mode and swaps the window.
- **Power:** Sleep, Restart, Shut down. Each needs a second press within 4 s. Restart and shut down ask apps to close (never forced); shut down is hybrid when Fast startup is on, as in the Start menu. On Modern Standby PCs (most handhelds) Sleep turns the display off, which is what puts them to sleep; elsewhere it suspends.

The page asks with `{type:"quick-settings"}`, `{type:"audio-output", id}`, `{type:"hdr", display, on}`, `{type:"refresh-rate", display, hz}`, `{type:"fps-counter"}` and `{type:"power", action}`; the host answers each with the current state (`{type:"quick-settings", outputs, displays, fpsCounter?, error?}`). In a plain browser the panel shows stand-in devices.

## Install and release

`Playdex-Setup.exe` (`src/Playdex.Setup`) installs, updates and uninstalls it (`%LOCALAPPDATA%\Programs\Playdex`, a Start menu shortcut, the Settings → Apps entry); the released one carries the app inside, and the online one, as `Wrapper.exe`, moves WinForms Wrapper installs over. Its window asks for the Playdex server (API URL, prefilled) and API key and checks the key with the server (`GET launcher/summary`: a refused key is shown as such; an unreachable server can be saved anyway; a saved key is kept when the field is left empty), offers the Full Screen Experience package when Developer Mode is on, and shows the install running; Skip installs without a key. `--api-url=URL --api-key=KEY` save them without asking and `--quiet` installs without the window; the Wrapper's updater path never asks. `--quit` makes a running Playdex quit (the setup uses it before updating or uninstalling), and at start an installed Playdex adds or updates its Settings → Apps entry (`InstallRegistration`). Releases, signing and the Wrapper migration: `docs/windows-releases.md`.

## Files

- Log: `%LOCALAPPDATA%\Playdex\desktop.log`. WebView2 profile: `%LOCALAPPDATA%\Playdex\WebView2-desktop` (both modes).
- Config: the install's `%LOCALAPPDATA%\Playdex\config.json`, shared with the FSE package and `Playdex.Run.exe`.
