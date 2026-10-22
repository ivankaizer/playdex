# Playdex Full Screen Experience entry (Windows)

The MSIX that Windows can pick as the Full Screen Experience (FSE) home app. It has no UI of its own: it starts the Playdex app (`src/Desktop`, `Playdex.exe`) with `--big-picture` and stays running, windowless, while Playdex runs, so FSE sees its home app alive. Every later activation (Game Bar "Home", a second start, `playdex-launcher://launch/GAMEID`) starts a short-lived copy that hands the request on (Playdex is single instance and goes home) and exits. Big Picture itself, its flags and its measurements are in `src/Desktop/README.md`.

Why a separate package: an FSE home app needs package identity, the `windows.gamingApp` extension and the `gamingHome` custom capability (developer-mode SCCD), while Playdex is unpackaged so it can update itself from the download feed (download.playdex.dev) and share real `%LOCALAPPDATA%` paths with `Playdex.Run.exe` and Steam shortcuts.

- **Finding Playdex:** the `Playdex.exe` recorded in the install's config (`shell.executablePath`; installed and published builds record themselves at start), else `%LOCALAPPDATA%\Programs\Playdex\Playdex.exe`. Without either it says Playdex isn't installed.
- **Outside the package:** a child of a packaged app runs in the package's container by default (its identity, its single-instance keys). Playdex is started with `PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY` = `PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_OVERRIDE`, so it runs as an ordinary unpackaged process; Playdex logs `WITH package identity` at start if it ever doesn't.
- **Foreground:** started by the shell, the entry may set the foreground, and passes that on (`AllowSetForegroundWindow`) before starting Playdex.
- **Log:** `%LOCALAPPDATA%\Playdex\launcher.log` (`fse entry: ...` lines).

Windows only. It is not in `Playdex.sln`, because CI builds the solution on Linux and the Windows App SDK tooling only runs on Windows. `global.json` here allows any .NET 10 SDK.

## Run (dev)

```powershell
cd src/Launcher/Host; dotnet run                    # unpackaged: starts the recorded or installed Playdex.exe in Big Picture
```

## MSIX and Full Screen Experience

```powershell
powershell -File tools/package.ps1                  # dev cert as files (first run) + signed MSIX
powershell -File tools/install-dev.ps1              # elevated: trust cert, install package
```

Then go to Settings → Gaming → Full screen experience → Home app → Playdex. Developer Mode must be on, and Playdex for Windows must be installed (or a published build started once, so its path is recorded). Before uninstalling, pick another home app (uninstalling the current one can break Task View).

Installed users get the package from the app: Settings → This app → Install (see `src/Desktop/README.md`). Releases build and sign it as `playdex-launcher-v<version>.msix` (`.github/workflows/release-windows.yaml`).
