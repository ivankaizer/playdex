## Why

The tray currently exposes setup state and separate configuration actions instead of functioning as a concise game launcher. Users need a direct game list, a single add flow, and one clear path to advanced JSON settings.

## What Changes

- Remove the non-actionable current-game label and separate launch-selected action.
- Make each item under the game list launch that game immediately.
- Replace separate add/pick/change flows with one **Add a game...** executable picker; existing games are not changed through this action.
- Add a confirmed **Remove a game** submenu that removes wrapper configuration and generated launcher artifacts without deleting installed game files.
- Discover the backend game ID from `steam_appid.txt` beside the selected executable when it contains a valid Steam AppID.
- Replace API identity and API-key dialogs with one **Settings...** action that opens the JSON configuration file.
- **BREAKING** Remove the Ludusavi search UI, Ludusavi configuration, templates, and `--skip-ludusavi` command-line shortcut.
- Preserve generic pre-launch and post-exit hook support through JSON settings.

## Capabilities

### New Capabilities

- `tray-game-launcher`: Direct game launching, executable-based game creation, and JSON settings access from a minimal tray menu.

### Modified Capabilities

None.

## Impact

- Simplifies the WinForms tray menu, forms, and launch routing.
- Removes Ludusavi-specific configuration and test coverage while keeping generic hooks.
- Changes how new game IDs and display names are derived from selected executable files.
- Updates example configuration, smoke-test documentation, tests, and published binaries.
