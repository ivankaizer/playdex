# Windows releases

The Windows apps release together from one `v<major>.<minor>.<patch>` tag (`.github/workflows/release-windows.yaml`):

| Asset | What it is |
|---|---|
| `playdex-windows-v<version>.zip` + `.sha256` | The desktop app's folder (`Playdex.exe`, `Playdex.Run.exe`, the Windows App SDK, `ui/`). What Playdex updates itself from. |
| `Playdex-Setup.exe` | Installs the desktop app for the current user into `%LOCALAPPDATA%\Programs\Playdex`, adds a Start menu shortcut and starts it. |
| `playdex-wrapper-windows-v<version>.zip` + `.sha256` | `Playdex-Setup.exe` named `Wrapper.exe`: how WinForms Wrapper installs (v1.x) move to the desktop app. **Every release must keep it**, because a v1.x Wrapper only ever looks for this asset in the latest release. |
| `playdex-launcher-v<version>.msix`, `playdex-launcher.cer` | The Full Screen Experience launcher, signed, and its public certificate. Only when the signing secrets are set. |

## Publishing

1. Make sure `build-windows.yaml` passes on the commit (Core tests with the published Playdex.Run, the desktop app, the launcher and the setup build).
2. Push a tag `v<major>.<minor>.<patch>` higher than the last release. The workflow builds everything with that version, checks the product versions, the archives' contents and their checksums, and creates the release.
3. Drafts and prereleases are not offered as updates.

To withdraw a faulty release, remove it from the latest release and publish the fix under a higher patch version: installs never downgrade, and assets under a published tag should not be replaced.

## Signing the launcher

The launcher MSIX is signed with a self-signed code-signing certificate whose subject is the manifest's Publisher, `CN=Playdex Launcher Dev`. The release workflow reads it from two repository secrets, `LAUNCHER_SIGNING_PFX` (the .pfx as base64) and `LAUNCHER_SIGNING_PASSWORD`. Without them a release has no launcher package, and the desktop app's "Install" says so.

`src/Launcher/Host/tools/export-signing-secret.ps1` prepares both from the dev certificate that `tools/package.ps1` creates and prints the `gh secret set` commands to run. Using that certificate keeps released packages in the same package family as locally built ones (`Playdex.Launcher_bmt4bex73v8a8`), so they update each other.

Users trust the certificate once: the desktop app's "Use as Full Screen Experience home" imports the release's `playdex-launcher.cer` into Trusted People (one admin prompt) before it installs the package. Developer Mode must be on, because the gamingHome capability is only granted with the developer-mode SCCD.

## Updates

- **Desktop app.** Checks GitHub Releases at startup and daily, downloads and verifies `playdex-windows-v<version>.zip`, and offers "Restart to update". A copy of `Playdex.Run.exe` swaps the app folder, starts the new version and waits up to 60 s for it to confirm; otherwise the old folder is restored and started, and the next start reports why. Details: `src/Desktop/README.md`.
- **From the WinForms Wrapper.** A v1.x Wrapper's updater downloads `playdex-wrapper-windows-v<version>.zip`, verifies it, and on "Restart to update" replaces `Wrapper.exe` with the setup and starts it with `--update-health`. The setup confirms at once (the Wrapper waits only 30 s), then installs the desktop app from the same release and starts it. The desktop app migrates the Wrapper's config (`%LOCALAPPDATA%\PlaydexWrapper\config.json` into `%LOCALAPPDATA%\Playdex\config.json`; the old file stays), keeps its storage folders and backup state, and regenerates every generated launcher from `Playdex.Run.exe`, so Steam shortcuts keep working. From then on `Wrapper.exe` just opens Playdex.
- **Launcher.** Installing a newer MSIX over an older one updates it (Settings → This app → Install in the desktop app, or `tools/install-dev.ps1` for local builds).

## Testing a release before publishing

The setup takes test flags: `--archive=<zip>` installs from a local `playdex-windows-v<version>.zip` instead of the release, `--install-dir=<path>` installs elsewhere, and `--no-shortcut` and `--no-start` skip the shortcut and starting Playdex. The Wrapper-to-desktop path is best checked once on a clean Windows user or VM with a v1.1.0 Wrapper installed, against a test release in a fork.
