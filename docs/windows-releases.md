# Windows releases

The Windows apps release together from one `v<major>.<minor>.<patch>` tag (`.github/workflows/release-windows.yaml`). The assets go to the GitHub release and to **download.playdex.dev**, the public feed that installs and updates read (this repository is private):

| Asset | What it is |
|---|---|
| `playdex-windows-v<version>.zip` + `.sha256` | The app's folder (`Playdex.exe`, `Playdex.Run.exe`, `Playdex-Setup.exe` as its uninstaller, the Windows App SDK, `ui/`). What Playdex updates itself from. |
| `Playdex-Setup.exe` | The offline installer: the setup with the app folder (and the Full Screen Experience package, when signed) appended. Installs Playdex for the current user into `%LOCALAPPDATA%\Programs\Playdex`, updates an older install in place, adds the Start menu shortcut and the Settings → Apps entry, and starts it. |
| `playdex-wrapper-windows-v<version>.zip` + `.sha256` | The online setup (no payload; it downloads the app from the release) named `Wrapper.exe`: how WinForms Wrapper installs (v1.x) move to the app. **Every release must keep it**, because a v1.x Wrapper only ever looks for this asset in the latest release. |
| `playdex-launcher-v<version>.msix`, `playdex-launcher.cer` | The Full Screen Experience package, signed, and its public certificate. Only when the signing secrets are set. |

`tools/package-windows.ps1` makes every file but the MSIX (which `src/Launcher/Host/tools/package.ps1` makes locally, signed with the dev certificate); the workflow runs it too, so a local run gives the same files for a test.

## The download feed

`download.playdex.dev` is the Cloudflare R2 bucket `playdex-downloads` with a custom domain. `tools/downloads/publish.mjs release` (the last step of the release workflow) writes:

| Path | What it is |
|---|---|
| `windows/<tag>/<asset>` | Every asset of the release above, cached as immutable. |
| `windows/releases/<tag>.json` | The release in GitHub's release JSON shape (`tag_name`, `assets[].browser_download_url`, `size`), pointing at those files. `Playdex-Setup.exe` installs the version it was built for from here. |
| `windows/releases/latest.json` | The same for the highest version. The desktop app checks it for updates. Written last, and never moved back to an older tag. |
| `windows/Playdex-Setup.exe` | The latest setup at a stable URL: the download button on playdex.dev/launcher. |

`ReleaseFeed` (`src/Playdex.Core/AppUpdate.cs`) reads it. It only downloads files from https on `download.playdex.dev` itself and follows no redirects: a release JSON that points any file elsewhere is refused as a whole. Set it up once with `bun tools/downloads/publish.mjs setup` (creates the bucket and attaches the domain); CI needs the `CLOUDFLARE_API_TOKEN` (Workers R2 Storage: Edit; setup also needs Zone: Read on playdex.dev) and `CLOUDFLARE_ACCOUNT_ID` secrets.

v1.x WinForms Wrappers still update from the GitHub release, so it keeps being created.

## Publishing

1. Make sure `build-windows.yaml` passes on the commit (Core tests with the published Playdex.Run, the desktop app, the launcher and the setup build).
2. Push a tag `v<major>.<minor>.<patch>` higher than the last release. The workflow builds everything with that version, checks the product versions, the archives' contents and their checksums, and creates the release. It builds on the Windows PC when its runner is online and on GitHub otherwise ([windows-pc-runner.md](windows-pc-runner.md)).
3. Drafts and prereleases are not offered as updates.

To withdraw a faulty release, remove it from the latest release and publish the fix under a higher patch version: installs never downgrade, and assets under a published tag should not be replaced.

## Signing the launcher

The launcher MSIX is signed with a self-signed code-signing certificate whose subject is the manifest's Publisher, `CN=Playdex Launcher Dev`. The release workflow reads it from two repository secrets, `LAUNCHER_SIGNING_PFX` (the .pfx as base64) and `LAUNCHER_SIGNING_PASSWORD`. Without them a release has no launcher package, and the desktop app's "Install" says so.

`src/Launcher/Host/tools/export-signing-secret.ps1` prepares both from the dev certificate that `tools/package.ps1` creates and prints the `gh secret set` commands to run. Using that certificate keeps released packages in the same package family as locally built ones (`Playdex.Launcher_bmt4bex73v8a8`), so they update each other.

Users trust the certificate once: the desktop app's "Use as Full Screen Experience home" (and the setup's option) imports the release's `playdex-launcher.cer` into Trusted People (one admin prompt) before it installs the package. Developer Mode must be on, because the gamingHome capability is only granted with the developer-mode SCCD.

The certificate is pinned: `FsePackage.SigningCertificateSha256` (`src/Playdex.Core/FsePackage.cs`) holds its SHA-256 thumbprint, and the app and the setup refuse a `.cer` with any other thumbprint and a package Windows doesn't report as signed with it, before the admin prompt. The release workflow fails when `LAUNCHER_SIGNING_PFX` holds a different certificate. **Rotating the signing certificate** therefore needs a new app release: change the secrets and the thumbprint in `FsePackage.cs` together. Each app and setup installs the package of its own release, so older installs keep using the packages signed with the old certificate. A package built on another machine with its own dev certificate (`tools/package.ps1`) can only be installed with `tools/install-dev.ps1`.

## Updates

- **Desktop app.** Checks `download.playdex.dev/windows/releases/latest.json` at startup and daily, downloads and verifies `playdex-windows-v<version>.zip`, and offers "Restart to update". A copy of `Playdex.Run.exe` swaps the app folder, starts the new version and waits up to 60 s for it to confirm; otherwise the old folder is restored and started, and the next start reports why. Details: `src/Desktop/README.md`.
- **From the WinForms Wrapper.** A v1.x Wrapper's updater downloads `playdex-wrapper-windows-v<version>.zip`, verifies it, and on "Restart to update" replaces `Wrapper.exe` with the setup and starts it with `--update-health`. The setup confirms at once (the Wrapper waits only 30 s), then installs the desktop app from the same release and starts it. The desktop app migrates the Wrapper's config (`%LOCALAPPDATA%\PlaydexWrapper\config.json` into `%LOCALAPPDATA%\Playdex\config.json`; the old file stays), keeps its storage folders and backup state, and regenerates every generated launcher from `Playdex.Run.exe`, so Steam shortcuts keep working. From then on `Wrapper.exe` just opens Playdex.
- **Launcher.** Installing a newer MSIX over an older one updates it (Settings → This app → Install in the desktop app, or `tools/install-dev.ps1` for local builds).

## The setup

- **Window.** Run by hand it asks for the Playdex server and API key (checked with the server; with a key already saved, an empty field keeps it), offers "Also use Playdex as the Full Screen Experience home" when Developer Mode is on (checked when the package is installed, which is then updated), and shows the install's progress. `--quiet` installs without it, updating an installed FSE package when the setup carries one.
- **Updating.** An older install is updated in place, keeping the config: a running Playdex 2.0 or later quits on `Playdex.exe --quit`; an older one is asked to quit by the user. The same or a newer install just starts.
- **Uninstalling.** Settings → Apps → Playdex → Uninstall runs the copy in the app folder with `--uninstall` (`--quiet` for the quiet uninstall string). It continues from a copy in `%TEMP%`, closes Playdex, removes the app folder and, when they belong to that install, the FSE package, the start-with-Windows entry, the Start menu shortcut and the Settings → Apps entry, then asks whether to remove `%LOCALAPPDATA%\Playdex` too. The app adds the Settings → Apps entry itself when it is missing (installs made by older setups) and keeps its version current after self-updates.

## Testing a release before publishing

Run `tools/package-windows.ps1 -Version <version> -Out <folder> [-Msix … -Cer …]` and try `<folder>\Playdex-Setup.exe`. The setup also takes test flags: `--archive=<zip>` installs from a local `playdex-windows-v<version>.zip`, `--install-dir=<path>` installs elsewhere, `--reinstall` installs over the same version, `--quiet` skips the window, and `--no-shortcut`, `--no-register` and `--no-start` skip the shortcut, the Settings → Apps entry and starting Playdex. The Wrapper-to-desktop path is best checked once on a clean Windows user or VM with a v1.1.0 Wrapper installed, against a test release in a fork.
