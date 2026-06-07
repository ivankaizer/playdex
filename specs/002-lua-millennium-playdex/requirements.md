# Requirements Document

## Introduction

This feature specifies a from-scratch rebuild of the existing Playdex Millennium extension with a Lua backend instead of the current Python backend. The extension shall preserve the current user-facing behavior: tracking play sessions for non-Steam games launched through the desktop Steam client, sending authenticated session events to the Playdex API, and exposing global plus per-game configuration inside Steam's UI.

The rewrite shall target Millennium's current plugin model, where the frontend remains Steam UI TypeScript/React and the native backend is Lua. Millennium documentation notes that plugin backends run natively through LuaJIT, that `plugin.json` supports `backendType` values of `lua` or `python`, and that new plugins should be written in Lua because Python backend support is legacy. The Lua implementation shall use Millennium-supported Lua modules such as `millennium`, `http`, and `json` where applicable.

References:

- Existing extension: `apps/millenium`
- Millennium config documentation: https://docs.steambrew.app/plugins/structure/config
- Millennium file structure documentation: https://docs.steambrew.app/plugins/structure/file-structure
- Millennium Lua API documentation: https://docs.steambrew.app/plugins/lua/millennium
- Millennium Lua HTTP documentation: https://docs.steambrew.app/plugins/lua/http
- Millennium Lua JSON documentation: https://docs.steambrew.app/plugins/lua/cjson

## Requirements

### Requirement 1

**User Story:** As a Playdex user, I want the rewritten extension to install and load as a normal Millennium plugin, so that I can use Playdex in Steam without relying on legacy Python backend support.

#### Acceptance Criteria

1. WHEN the plugin package is built THEN the system SHALL include a valid `plugin.json` with `name`, `common_name`, `description`, `version`, `useBackend`, `backend`, `frontend`, and `backendType` values appropriate for a Lua-backed Millennium plugin.
2. WHEN Millennium loads the plugin backend THEN the system SHALL initialize through Lua and call `millennium.ready()` within 1 second under normal startup conditions.
3. IF backend initialization performs work that may take longer than 1 second THEN the system SHALL defer that work until after the backend has signaled readiness.
4. WHEN the plugin is installed for development or local testing THEN the system SHALL use the plugin identity `playdex-dev`.
5. WHEN the plugin is built or packaged for production THEN the system SHALL use the plugin identity `playdex`.
6. WHEN the plugin is unloaded THEN the system SHALL stop recurring frontend monitoring, remove patches introduced by the plugin, and avoid leaving duplicate callbacks registered after a reload.

### Requirement 2

**User Story:** As a Playdex user, I want the extension to track non-Steam game sessions, so that my Playdex playtime reflects games launched from Steam.

#### Acceptance Criteria

1. WHEN Steam reports a running app with an app id greater than or equal to `0x80000000` THEN the system SHALL treat it as a non-Steam app eligible for Playdex tracking.
2. WHEN Steam reports a running app with an app id lower than `0x80000000` THEN the system SHALL ignore it for Playdex session tracking.
3. WHILE an eligible non-Steam app remains running THEN the system SHALL send ping events at a 60 second interval.
4. WHEN an eligible non-Steam app is no longer observed as running after having been tracked THEN the system SHALL send exactly one session end event for that app.
5. IF multiple eligible non-Steam apps are running at the same time THEN the system SHALL track ping and quit state independently per app id.
6. IF a monitoring cycle is still in progress when the next cycle is due THEN the system SHALL avoid overlapping lifecycle detection work.

### Requirement 3

**User Story:** As a Playdex user, I want Playdex session events to be sent to the Playdex API with the correct game identity, so that sessions are associated with the intended game.

#### Acceptance Criteria

1. WHEN a session ping event is sent THEN the system SHALL POST to `https://playdex.ejwan.dev/api/playnite/sessions/ping`.
2. WHEN a session end event is sent THEN the system SHALL POST to `https://playdex.ejwan.dev/api/playnite/sessions/end`.
3. WHEN a ping or end event is sent for an app with a configured custom game id THEN the system SHALL include `gameTitle`, `gameId`, and `gameProvider` in the JSON request body.
4. WHEN a ping or end event is sent for an app whose display name contains a trailing numeric id in square brackets THEN the system SHALL parse that id as `gameId`, remove the bracketed id from `gameTitle`, and set `gameProvider` to `steam`.
5. WHEN a ping or end event is sent for an app without a configured or parsed Steam game id THEN the system SHALL NOT send the request.
6. WHEN the Playdex API receives an end request THEN the API SHALL accept `gameTitle`, `gameId`, and `gameProvider` fields and use them to resolve the session being closed.
7. WHEN multiple eligible non-Steam apps are tracked under the same API key THEN the system SHALL include game identity on each ping and end request so the API can resolve the correct session.
8. WHEN the API request is sent THEN the system SHALL include `Authorization: Bearer <api key>`, `Content-Type: application/json`, and `Accept: application/json` headers.
9. IF the HTTP request fails, times out, or returns a non-success status THEN the system SHALL log the failure without crashing the plugin backend.

### Requirement 4

**User Story:** As a Playdex user, I want to configure authentication globally and per game, so that I can use a default account while overriding individual games when needed.

#### Acceptance Criteria

1. WHEN the user opens the Playdex plugin settings panel THEN the system SHALL display a global API key field.
2. WHEN the global API key changes THEN the system SHALL persist the new value after a short debounce of approximately 300 milliseconds.
3. WHEN the user opens a Playdex section on an eligible non-Steam app's properties page THEN the system SHALL display fields for custom game id and optional per-game API key override.
4. WHEN the user saves per-game settings THEN the system SHALL persist both the custom game id and per-game API key for that app.
5. WHEN the user requests the API key for a game with a non-empty per-game API key THEN the system SHALL return the per-game API key.
6. WHEN the user requests the API key for a game without a per-game API key THEN the system SHALL return the global API key.
7. IF no applicable API key exists when a session event is due THEN the system SHALL skip the API call and log that authentication is missing.
8. WHEN per-game settings are saved successfully THEN the system SHALL show a temporary saved state before returning the save button to its default label.
9. IF per-game settings fail to save THEN the system SHALL show a temporary failed state before returning the save button to its default label.

### Requirement 5

**User Story:** As a Playdex user, I want settings to survive Steam and plugin restarts, so that I do not have to reconfigure Playdex repeatedly.

#### Acceptance Criteria

1. WHEN the Lua backend loads THEN the system SHALL read persisted settings from a plugin-local `settings.json` file.
2. IF `settings.json` does not exist THEN the system SHALL initialize with empty settings.
3. IF `settings.json` contains invalid JSON THEN the system SHALL initialize with empty settings and log a warning.
4. WHEN settings are saved THEN the system SHALL write the full settings document atomically or with an equivalent safe-write strategy that avoids partially written JSON after interruption.
5. WHEN settings are serialized THEN the system SHALL preserve the structure `{ "api_key": string, "games": { [appDisplayName]: { "gameId": string, "api_key": string } } }`.
6. WHEN a stored value contains leading or trailing whitespace THEN the system SHALL trim whitespace before persistence.
7. IF a value is intentionally cleared in the UI THEN the system SHALL persist the cleared value rather than restoring the previous value.

### Requirement 6

**User Story:** As a Steam user, I want Playdex controls to appear only where they are relevant, so that Steam's existing UI remains uncluttered.

#### Acceptance Criteria

1. WHEN the plugin loads THEN the system SHALL register a Playdex plugin settings entry titled `Playdex` with a settings icon and global API key content.
2. WHEN an app properties page is rendered for an eligible non-Steam app THEN the system SHALL add a `Playdex` page to that app's properties navigation.
3. WHEN an app properties page is rendered for a regular Steam app THEN the system SHALL NOT add a Playdex page.
4. IF the Playdex app properties page has already been added to a route list THEN the system SHALL NOT add a duplicate page.
5. WHEN per-game settings are saved THEN the system SHALL refresh or invalidate the relevant Steam UI route state so updated playtime-related UI can be reflected promptly.
6. IF Steam's internal UI objects required for patching are not ready at plugin load THEN the system SHALL wait for them before applying the patch.

### Requirement 7

**User Story:** As a developer, I want the Lua rewrite to preserve the frontend/backend RPC contract, so that the frontend can be reused or migrated incrementally.

#### Acceptance Criteria

1. WHEN the frontend calls `RPC.OnNonSteamAppHeartbeat` with an app name THEN the Lua backend SHALL send a Playdex ping for that app.
2. WHEN the frontend calls `RPC.OnNonSteamAppQuit` with an app name THEN the Lua backend SHALL end the current Playdex session.
3. WHEN the frontend calls `RPC.OnApiKeyFieldChange` with a key THEN the Lua backend SHALL persist the global API key.
4. WHEN the frontend calls `RPC.OnGameApiKeyFieldChange` with an app name and key THEN the Lua backend SHALL persist that game's API key override.
5. WHEN the frontend calls `RPC.GetApiKey` THEN the Lua backend SHALL return the global API key or an empty value.
6. WHEN the frontend calls `RPC.GetGameApiKey` with an app name THEN the Lua backend SHALL return the effective API key for that app or an empty value.
7. WHEN the frontend calls `RPC.SetGameId` with an app name and id THEN the Lua backend SHALL persist that game's custom id.
8. WHEN the frontend calls `RPC.GetGameId` with an app name THEN the Lua backend SHALL return that game's custom id or an empty value.
9. IF an RPC payload has invalid JSON or missing required fields THEN the backend SHALL reject the request gracefully and log a diagnostic message.

### Requirement 8

**User Story:** As a developer, I want the rewrite to be built from scratch but behaviorally compatible, so that the new plugin can replace the old one with low migration risk.

#### Acceptance Criteria

1. WHEN the Lua rewrite is implemented THEN the backend source SHALL be Lua and SHALL NOT depend on Python modules, Python package installation, or Python runtime behavior.
2. WHEN the plugin package is created THEN it SHALL include only the runtime files required by Millennium and Playdex.
3. WHEN the package is built for production THEN it SHALL produce a versioned archive equivalent in purpose to the current `playdex-<version>.zip` artifact.
4. WHEN the plugin is built for development THEN it SHALL support copying the plugin to the local Steam plugins directory and launching Steam in development mode.
5. IF the existing frontend is retained THEN the system SHALL keep TypeScript checks, formatting, and build commands passing for the frontend.
6. IF the frontend is rewritten during the Lua migration THEN the system SHALL preserve the user-facing settings behavior and lifecycle monitoring requirements in this document.

### Requirement 9

**User Story:** As a developer, I want observable failures and predictable edge-case behavior, so that Playdex support issues can be diagnosed without disrupting Steam.

#### Acceptance Criteria

1. WHEN the plugin backend starts, loads settings, sends session events, skips events, or encounters errors THEN the system SHALL write useful logs through Millennium-compatible logging or standard Lua logging.
2. IF the Playdex API host is unreachable THEN the system SHALL continue monitoring running apps and retry only on the next normal lifecycle or heartbeat event.
3. IF a game launches and quits between monitoring intervals THEN the system MAY miss the session, but it SHALL NOT create a false session for a different app.
4. IF the same app id disappears and reappears in a later polling cycle THEN the system SHALL treat the later appearance as a new session.
5. IF Steam returns an app object without a display name THEN the system SHALL skip Playdex API calls for that app and log the missing identity.
6. IF the user's API key is present only in memory because persistence failed THEN the system SHALL surface the save failure through logs and the relevant UI save state.

### Requirement 10

**User Story:** As a maintainer, I want clear verification criteria for the Lua rewrite, so that I can confirm it matches the current extension before release.

#### Acceptance Criteria

1. WHEN automated checks run THEN the system SHALL validate Lua backend parsing, settings persistence, API payload construction, and effective API key selection.
2. WHEN frontend checks run THEN the system SHALL validate the TypeScript build and lint/format rules used by the plugin project.
3. WHEN a manual Steam test is performed THEN the tester SHALL verify global API key persistence, per-game custom game id persistence, per-game API key override behavior, ping event delivery, and end event delivery.
4. WHEN a manual Steam test is performed with a regular Steam game THEN the tester SHALL verify that Playdex does not add per-game settings or send session events.
5. WHEN a manual Steam test is performed with corrupted settings JSON THEN the tester SHALL verify that the plugin loads, logs the warning, and allows settings to be saved again.
