# Playdex desktop app (Windows)

The desktop version of the launcher and the successor of the WinForms Wrapper: Playdex in a normal window and the tray. It shows the launcher's React UI (`../Launcher/ui`) in desktop mode and shares its window, input and quick settings code (`../Launcher/Shell`) and everything in `../Playdex.Core`. Plan: `docs/launcher/wrapper-merge-plan.md`, phase 4.

Windows only, unpackaged and self-contained (WinUI 3, WebView2, Native AOT in Release). Like the launcher host it is not in `Playdex.sln`, because the Windows App SDK XAML compiler only runs on Windows. `global.json` here allows any .NET 10 SDK.

## Run (dev)

```powershell
cd src/Launcher/ui; bun install; bun run dev        # http://localhost:5190
cd ../../Desktop; dotnet run                        # Debug builds load the dev server; --dist serves ../Launcher/ui/dist
```

Generated per-game launchers need `Playdex.Run.exe` next to `Playdex.exe`; a dev build without it skips them (logged).

## What it does

- **Window and tray.** One instance; starting it again opens the window. Closing the window releases it (and WebView2), and Playdex keeps running in the tray: click to open, right-click for Open and Quit. `--background` starts it in the tray, which is how it starts with Windows (Settings → This app).
- **Library and play.** The launcher's Home and Library in a window. Games added to Playdex start directly and their sessions are recorded here (`SessionManager`, `ConfiguredGameWatcher`).
- **Manage** (the Wrapper's list): add a game from its .exe, edit its name, Steam/API id, launch options, platform and daily backup, add it to Steam as a non-Steam shortcut with SteamGridDB art (Steam must be closed), back up its saves with Ludusavi, remove it. Changes reload the page with the new library.
- **Settings** (the Wrapper's dialog): API URL and key, default platform, SteamGridDB key, Ludusavi path, start with Windows, quit. Keys are never sent to the page.
- **Background:** at startup every game's launcher is regenerated from `Playdex.Run.exe` (replacing the Wrapper's copies of itself, so Steam shortcuts keep working), queued sessions are uploaded, and the daily Ludusavi backups run every 15 minutes as they did in the Wrapper (`state/ludusavi-backups.json`, kept in place).
- **Updates.** Installed builds (Release, `Playdex.Run.exe` next to `Playdex.exe`, a folder the user can write) check GitHub Releases at startup and daily, download `playdex-windows-v<version>.zip` with its `.sha256`, verify and extract it into `%LOCALAPPDATA%\Playdex\updates`, and offer "Restart to update" (also as a Windows notification while no window is open). Installing copies `Playdex.Run.exe` out of the app folder and starts it with `--apply-update`: it waits for Playdex to exit, swaps the app folder for the new one, starts it and waits up to 60 s for it to confirm (`--update-health`); otherwise the old folder goes back and starts again, and the next start reports why. The helper logs to `updates\update.log`.
- **Full Screen Experience launcher.** Settings → This app → Install downloads `playdex-launcher-v<version>.msix` and `playdex-launcher.cer` from the release Playdex came from, imports the certificate into Trusted People (one admin prompt), installs the package for the user and opens Settings → Gaming. Developer Mode must be on (the gamingHome capability is only granted with the developer-mode SCCD); without it, Install opens Settings → For developers. Development builds only report whether the launcher is installed.

The page talks to it with `{type:"desktop", id, method, params}` (see `DesktopHost.cs` and `../Launcher/ui/src/host/desktop.ts`); the reply is `{type:"desktop-result", id, ok, result | error}`.

## Install and release

`Playdex-Setup.exe` (`src/Playdex.Setup`) installs it into `%LOCALAPPDATA%\Programs\Playdex`; the same exe, as `Wrapper.exe`, moves WinForms Wrapper installs over. Releases, signing and the Wrapper migration: `docs/windows-releases.md`.

## Files

- Log: `%LOCALAPPDATA%\Playdex\desktop.log`. WebView2 profile: `%LOCALAPPDATA%\Playdex\WebView2-desktop` (the launcher has its own).
- Config: the install's `%LOCALAPPDATA%\Playdex\config.json`, shared with the launcher and `Playdex.Run.exe`.
