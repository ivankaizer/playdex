# Wrapper update releases

The Windows wrapper uses the latest stable GitHub Release in `ivankaizer/playdex` as its update channel. A compatible release tag must be exactly `v<major>.<minor>.<patch>`, for example `v2.4.1`.

## Publishing

1. Ensure wrapper tests and the single-file publish pass locally.
2. Create and push a stable version tag. The release workflow validates the tag before publishing.
3. Confirm the release contains exactly these updater assets:
   - `playdex-wrapper-windows-v<version>.zip`
   - `playdex-wrapper-windows-v<version>.zip.sha256`
4. Confirm the archive contains `Wrapper.exe`, its product version matches the tag without the leading `v`, and the checksum file contains the lowercase SHA-256 digest followed by two spaces and the archive filename.

Drafts and prereleases are not part of the automatic update channel. The wrapper checks once after canonical tray startup; generated game launchers never check for updates.

## Installation and recovery

The wrapper downloads into `%LOCALAPPDATA%\PlaydexWrapper\updates`, verifies the archive checksum, and waits for the user to choose **Restart to update**. A trusted copy of the running wrapper performs same-volume replacement after the tray exits. It retains the old executable until the new tray process confirms healthy startup.

If replacement or startup health confirmation fails, the helper restores and relaunches the old executable. The next tray startup consumes `updates\last-failure.txt` and shows the failure. Staging data can be removed after the wrapper is closed; user configuration, logs, backup state, and generated launchers are stored separately and are not part of replacement.

An executable in a directory the current user cannot modify cannot be updated automatically. Move it to a user-writable directory or replace it manually from the verified release archive; the updater does not request elevation.

## Withdrawing a faulty release

Remove the faulty release from the latest stable GitHub channel, then publish the known-good code under a higher patch version. Clients reject equal versions and downgrades, so republishing an older tag does not roll installations back.

For a checksum incident, remove the affected release immediately. Do not replace assets under the same published tag: publish a higher patch release so clients receive an immutable version and asset identity.
