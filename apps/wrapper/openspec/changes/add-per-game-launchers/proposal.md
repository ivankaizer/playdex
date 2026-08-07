## Why

Adding a configured game to Steam currently requires manually recreating the wrapper command and game ID. Each game should instead receive a stable launcher executable that Steam can target directly and keep observing for the full game session.

## What Changes

- Automatically create or refresh a persistent launcher executable and manifest for every configured game.
- Make a generated launcher resolve its config and game ID without command-line arguments.
- Keep the generated launcher process alive while it launches, monitors, and pings for its assigned game.
- Expose the launcher location and regeneration action in the tray UI.
- Publish the wrapper as one self-contained file and hard-link or copy that file into stable per-game launchers.

## Capabilities

### New Capabilities

- `per-game-launchers`: Stable per-game executables, manifest-based routing, automatic generation, and Steam-oriented launcher lifecycle.

### Modified Capabilities

None.

## Impact

- Adds a launcher-manifest contract and per-game launcher generator.
- Updates tray game creation and configuration persistence to synchronize launchers.
- Updates publishing, tests, example configuration, and Steam setup documentation.
- Generated launchers remain local files and do not alter Steam configuration automatically.
