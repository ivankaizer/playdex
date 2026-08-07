## Context

The current tray combines launcher actions, selected-game state, API forms, executable editing, and a Ludusavi-specific workflow. Game sessions, generic hooks, generated per-game executables, and JSON configuration already exist independently of those UI elements.

## Goals / Non-Goals

**Goals:**

- Make the tray's game list the primary launch surface.
- Make game creation an executable-first, add-only operation.
- Consolidate advanced configuration behind the JSON settings file.
- Remove all integration-specific Ludusavi code without weakening generic hooks.

**Non-Goals:**

- Build a general-purpose graphical JSON settings editor.
- Add executable replacement UI or delete installed game files.
- Infer backend-specific API IDs beyond an initial value derived from the executable name.

## Decisions

### Game menu items launch instead of selecting

Each game item calls the existing contained tray action runner, persists that game as the last selection, sets it as the launch target, and exits the tray message loop. The existing coordinator then owns launch, ping, monitoring, and hooks. This avoids duplicate session logic.

### Add games from a single file picker

The executable filename without extension becomes the wrapper game ID and display name. During Add a game, the initial API game ID is a positive numeric value from the first non-empty line of `steam_appid.txt` in the executable directory when available, otherwise it falls back to the wrapper game ID. Pings use the value already stored in configuration and perform no filesystem identity lookup. An existing case-insensitive wrapper ID is rejected before any mutation. Users needing different identity values edit the JSON through Settings.

Alternative considered: retain a separate ID dialog. It adds a step and conflicts with the requested executable-first flow.

### Settings opens the concrete config file

The tray saves the in-memory configuration and shell-opens the exact configuration path, rather than opening its folder or maintaining multiple narrow dialogs. The application does not remain responsible for synchronizing external edits made while the tray is already running; they take effect on restart.

### Removal is separate, confirmed, and wrapper-scoped

A Remove a game submenu mirrors the configured game list but never launches items. After explicit confirmation, removal updates the central configuration and best-effort deletes only the deterministic generated launcher executable and adjacent manifest. It never touches the configured game executable or Steam's shortcut database. If the removed game was selected, the next game is chosen by case-insensitive game ID order.

### Remove only integration-specific hooks

Ludusavi fields, discovery UI, command shortcut, and hook-template helpers are deleted. The generic hook model, runner, skip controls, and JSON arrays remain supported.

## Risks / Trade-offs

- [Two executables can share the same filename] → Reject the duplicate derived ID and allow advanced users to add a distinct entry in JSON.
- [External settings edits do not update the current in-memory tray] → Document that the tray must be restarted after saving JSON changes.
- [Old JSON retains Ludusavi fields or hook entries] → Unknown removed fields are ignored; existing hook commands remain generic data and may be removed manually in Settings.
- [`steam_appid.txt` contains unrelated or malformed text] → Accept only a positive unsigned numeric AppID and otherwise use the safe executable-derived fallback.
- [A generated launcher is running or locked during removal] → Remove the configuration, log cleanup failure, and allow the stale launcher file or Steam shortcut to be cleaned up later.

## Migration Plan

1. Existing games remain loadable and appear in the direct-launch list.
2. Removed JSON fields are ignored during deserialization.
3. Existing hook arrays remain intact because generic hook support is unchanged.
4. Users move API and identity edits to Settings and restart the tray after external edits.
