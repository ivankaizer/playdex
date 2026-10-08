# Playdex launcher host (Windows)

.NET 10 WinUI 3 app that shows the React UI (`../ui`) full screen in WebView2, reads the controller natively, launches games and records sessions to Playdex. Spike results: `docs/launcher/spike-results.md`.

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

## Configuration

- API key: `PLAYDEX_API_KEY`, or `apiKey` in `%LOCALAPPDATA%\PlaydexLauncher\config.json`.
- API base URL: `PLAYDEX_API_URL`, or `apiBaseUrl` in the same file. Default `https://playdex.ejwan.dev/api`.
- The log, `metrics.jsonl` and the offline `queue.jsonl` live in `%LOCALAPPDATA%\PlaydexLauncher`.
- `PLAYDEX_INPUT_FORCE_POLL=1`: diagnostic that polls every XInput slot at 1 kHz.

## Library

At startup the host lists the games on this PC and injects them into the page as `window.__PLAYDEX_LIBRARY__` (a plain browser keeps the demo library):

- **Steam:** every fully installed app in each Steam library folder (`libraryfolders.vdf`, `appmanifest_*.acf`), minus redistributables and runtimes. Launched with `steam://rungameid`; the host records the session.
- **Playdex:** every generated launcher in the Playdex Wrapper's launchers folder (`%LOCALAPPDATA%\PlaydexWrapper\launchers\Playdex-*.exe` with its `.playdex.json`), matched to its entry in the Wrapper config, if the game's executable exists. Launched through that `Playdex-*.exe`, which records the session itself; the host only follows it to exit (a Job Object, so exit arrives after the Wrapper's exit debounce, 10 s by default). A Wrapper game with a Steam app id replaces the same Steam entry.

New games appear the next time the launcher starts.
