# Playdex launcher host (Windows)

.NET 10 WinUI 3 app that shows the React UI (`../ui`) full screen in WebView2, reads the controller natively, launches games and records sessions to Playdex. Spike results: `docs/launcher/spike-results.md`.

Session capture, the offline queue, game watchers, Steam and the library live in `src/Playdex.Core`, shared with the desktop app (`src/Desktop`); the window, page bridge, controller input and quick settings are in `src/Launcher/Shell`, compiled into both. This project is the full-screen launcher itself: the Full Screen Experience home app, its MSIX and its self-tests. Core's tests are `test/Playdex.Core.Tests`.

Windows only. It is not in `Playdex.sln`, because CI builds the solution on Linux and the Windows App SDK XAML compiler only runs on Windows. `global.json` here allows any .NET 10 SDK (the root one pins 10.0.302).

## Run (dev)

```powershell
cd src/Launcher/ui; bun install; bun run dev        # http://localhost:5190
cd ../Host; dotnet run                              # Debug builds load the dev server
```

## Release build and measurements

```powershell
cd src/Launcher/ui; bun run build                   # Release serves ui/dist from https://app.playdex/
cd ../Host
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"   # Native AOT needs vswhere to find link.exe
dotnet publish -c Release -r win-x64
powershell -File tools/measure.ps1                  # cold start, input latency, idle memory and CPU
bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/Playdex.Launcher.exe --selftest-job
```

## MSIX and Full Screen Experience

```powershell
powershell -File tools/package.ps1                  # dev cert as files (first run) + signed MSIX
powershell -File tools/install-dev.ps1              # elevated: trust cert, install package
```

Then go to Settings → Gaming → Full screen experience → Home app → Playdex. Developer Mode must be on. Before uninstalling, pick another home app (uninstalling the current one can break Task View).

## Flags

| Flag | Effect |
|---|---|
| `--dev`, `--ui-url=URL` | Load the dev server (the default in Debug) |
| `--dist` | Debug build: serve `ui/` instead of the dev server |
| `--exit-on-ready` | Exit once the UI is interactive (cold-start runs) |
| `--bench-input=N` | Inject N presses through the input pipeline and log latency percentiles |
| `--launch=GAMEID` | Launch a game once the UI is ready (also `playdex-launcher://launch/GAMEID`). Ids: `steam-<appid>`, `playdex-<wrapper game id>` |
| `--no-suspend` | Keep the page running while a game is in front |
| `--selftest-job` | Job Object exit-detection self-test |
| `--selftest-quick-settings[=toggle]` | Log the sound outputs, HDR, refresh rates and the FPS counter; `=toggle` switches the sound output, HDR and refresh rate of each display, checks, and switches back, then toggles the FPS counter twice |

## Configuration

- API key and URL: `PLAYDEX_API_KEY` and `PLAYDEX_API_URL`, or `api.apiKey` and `api.baseUrl` in the install's config, `%LOCALAPPDATA%\Playdex\config.json` (shared with the desktop app). Default URL `https://playdex-api.ejwan.dev/api`.
- The config is created on first start from the Wrapper's `%LOCALAPPDATA%\PlaydexWrapper\config.json` and the launcher's old `%LOCALAPPDATA%\PlaydexLauncher\config.json`, which are left in place. The offline queue and the WebView2 profile move from `PlaydexLauncher` once.
- The log, `metrics.jsonl` and the offline `queue.jsonl` live in `%LOCALAPPDATA%\Playdex`.
- `PLAYDEX_INPUT_FORCE_POLL=1`: diagnostic that polls every XInput slot at the foreground rate, as if a pad were connected.
- `PLAYDEX_INPUT_HZ=N`: foreground XInput poll rate (default 250), for measurements.

## Library

At startup the host lists the games on this PC and injects them into the page as `window.__PLAYDEX_LIBRARY__` (a plain browser keeps the demo library):

- **Steam:** every fully installed app in each Steam library folder (`libraryfolders.vdf`, `appmanifest_*.acf`), minus redistributables and runtimes. Launched with `steam://rungameid`; the host records the session.
- **Playdex:** every game added to Playdex (the config's games) whose executable exists. Launched directly with its launch options and followed to exit by its monitor settings (`ConfiguredGameWatcher`, `ProcessMonitor`); the host records the session. The game's lock (`GameLock`) is held while it runs, so starting it from its Steam shortcut at the same time fails instead of recording it twice. A game added to Playdex with a Steam app id replaces the same Steam entry.

Artwork is read in place from Steam, per image, in this order: the game's non-Steam shortcut grid art (`Steam\userdata\<user>\config\grid\<shortcut id>{p,_hero,_logo,}.*`, where the Playdex Wrapper puts SteamGridDB art; the shortcut id comes from `shortcuts.vdf`), custom grid art on its Steam app id, then Steam's artwork cache (`Steam\appcache\librarycache\<appid>\[<hash>\]`). Each user's grid folder is served as `https://<user id>.steam-grid.playdex/` and the cache as `https://steam-art.playdex/`; nothing else under `userdata` is exposed. Newer apps are only on the CDN under per-image hashes, so the fixed `cdn.cloudflare.steamstatic.com/steam/apps/<id>/...` URLs 404 for them; those URLs remain the fallback for each image none of these has.

New games appear the next time the launcher starts.

## Now playing

While a game runs, the top bar shows it with its play time; A on it switches to the game. The game is the one launched here, or else one started elsewhere (`RunningGames` in Core): a game added to Playdex whose process runs (matched by its monitor settings, or its launch executable), or Steam's running app (`HKCU\Software\Valve\Steam\RunningAppID`; no play time, Steam doesn't say when it started). The host checks every 3 s while its window is active and posts `{type:"now-playing", gameId, since}` (epoch ms or null; gameId null when nothing runs) when it changes.

Resume (`{type:"resume-game"}`) brings back the window that was in front when the launcher was called up over the game (Guide, Game Bar "Home"), else a window of the game's process, else the topmost app window below the launcher that isn't Steam's. The launch overlay offers it too (A). If no window is found the page shows a toast (`{type:"resume-failed"}`).

## Quick settings

Menu on the controller (or the sliders button in the top bar, or `m`) opens a panel over the current screen. B or Menu closes it.

- **Sound output:** the active playback devices; picking one makes it the default for every role (console, multimedia, communications), as Settings → Sound does. Listed with Core Audio's `IMMDeviceEnumerator`; set with `IPolicyConfig`, which is undocumented but stable since Windows 7 (EarTrumpet and similar tools use it).
- **HDR:** one switch per active display that supports HDR, the same switch as Settings → Display → Use HDR (`DisplayConfigSetDeviceInfo` with `DISPLAYCONFIG_SET_HDR_STATE`, falling back to the pre-24H2 advanced color call).
- **Refresh rate:** per display, the rates offered at its current resolution and colour depth (`EnumDisplaySettingsEx` on its GDI device); picking one keeps the resolution and is saved, as in Settings → Display → Advanced display (`ChangeDisplaySettingsEx` with `CDS_UPDATEREGISTRY`). Rates are whole Hz: this API reports 59.94 Hz as 59.
- **FPS counter:** shows or hides the NVIDIA App's statistics overlay (FPS, GPU, CPU, latency) by pressing its hotkey, Alt+R unless rebound; the hotkey is read from the overlay's settings (`HKCU\Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS`, `PMOCOverlayHKey*`). Listed only while the NVIDIA overlay runs. NVIDIA has no API for it and keeps no readable on/off state, so it is a toggle, not a switch.
- **Power:** Sleep, Restart, Shut down. Each needs a second press within 4 s. Restart and shut down ask apps to close (never forced); shut down is hybrid when Fast startup is on, as in the Start menu. On Modern Standby PCs (most handhelds) Sleep turns the display off, which is what puts them to sleep; elsewhere it suspends.

The page asks with `{type:"quick-settings"}`, `{type:"audio-output", id}`, `{type:"hdr", display, on}`, `{type:"refresh-rate", display, hz}`, `{type:"fps-counter"}` and `{type:"power", action}`; the host answers each with the current state (`{type:"quick-settings", outputs, displays, fpsCounter?, error?}`). In a plain browser the panel shows stand-in devices.

