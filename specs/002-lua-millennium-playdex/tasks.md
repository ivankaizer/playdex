# Implementation Plan

- [x] 1. Update Playdex API tests for ping/end-only session semantics
  - Rewrite `apps/api/tests/playniteRoutes.test.ts` so live session tests use `/api/playnite/sessions/ping` and `/api/playnite/sessions/end` only.
  - Add failing tests that prove `/end` requires `gameTitle`, `gameId`, and `gameProvider`.
  - Add failing tests that prove `/end` closes only the active session for the matching credential and game, leaving other active games under the same API key running.
  - Add or update a failing test that proves `/ping` no longer ends a different active game solely because a new game pinged.
  - Add a failing test that proves `/api/playnite/sessions/start` is no longer registered as a live-session endpoint.
  - _Requirements: 2.5, 3.1, 3.2, 3.6, 3.7, 10.1_

- [x] 2. Change the Playdex API route and handler contract
  - Update `apps/api/src/modules/playnite/playnite.router.ts` to remove the `/api/playnite/sessions/start` route.
  - Update `endPlayniteSessionsHandler` in `apps/api/src/modules/playnite/playnite.handlers.ts` to accept `ctx.body`, parse the shared game payload, and pass game identity to the service.
  - Keep missing/invalid bearer token behavior unchanged.
  - Ensure invalid end payloads return the same style of `400` response as invalid ping payloads.
  - _Requirements: 3.2, 3.3, 3.4, 3.5, 3.6, 3.8_

- [x] 3. Scope Playnite active session repository operations by game
  - Add `fetchActiveSessionForCredentialAndGame` to `apps/api/src/modules/playnite/playnite.repository.ts`.
  - Add `endActiveSessionsForCredentialAndGame` to `apps/api/src/modules/playnite/playnite.repository.ts`.
  - Retain timed-out cleanup by credential so stale Playnite sessions are still closed.
  - Remove or stop exporting repository code that exists only to end mismatched active games during ping.
  - _Requirements: 2.5, 3.6, 3.7, 9.4_

- [x] 4. Update Playnite API service semantics
  - Change `PlayniteSessionEndInput` in `apps/api/src/modules/playnite/playnite.service.ts` to include `gameTitle`, `gameId`, and `gameProvider`.
  - Update `endSessions` to normalize/upsert the game and end only active sessions for `platformCredentialId + gameId`.
  - Update `pingSession` to fetch/touch an active session for `platformCredentialId + gameId` and to stop ending mismatched active games.
  - Preserve ping timeout cleanup and notification payloads for sessions ended by timeout.
  - Run the updated Playnite route tests and fix regressions in the API module.
  - _Requirements: 2.3, 2.4, 2.5, 3.1, 3.2, 3.6, 3.7, 9.2_

- [x] 5. Add a Lua backend test harness for extension domain logic
  - Add a test command under `apps/millenium/package.json` that runs Lua backend tests without Steam.
  - Create test doubles for Millennium logging and HTTP request behavior so backend logic can be tested without network access.
  - Add tests for bracketed game id parsing, configured id override, cleaned titles, effective API key selection, skipped missing-auth requests, and skipped missing-id requests.
  - Add tests for settings load/save behavior with missing, valid, corrupt, trimmed, and intentionally empty values.
  - _Requirements: 3.3, 3.4, 3.5, 4.5, 4.6, 4.7, 5.1, 5.2, 5.3, 5.6, 5.7, 10.1_

- [x] 6. Replace the Python backend with Lua modules
  - Remove the Python backend implementation files from `apps/millenium/backend`.
  - Create `backend/main.lua`, `backend/rpc.lua`, `backend/playdex.lua`, `backend/settings.lua`, `backend/game_identity.lua`, and `backend/http_client.lua`.
  - Implement settings persistence to plugin-local `settings.json` using safe writes where possible.
  - Implement ping and end HTTP requests with game identity payloads, bearer auth, JSON headers, and logged non-success responses.
  - Implement RPC methods for heartbeat, quit, global API key, per-game API key, and game id reads/writes.
  - Ensure backend startup calls `millennium.ready()` promptly and does not depend on Python runtime behavior.
  - Run the Lua backend tests added in task 5.
  - _Requirements: 1.2, 1.3, 3.1, 3.2, 3.3, 3.4, 3.5, 3.8, 3.9, 4.3, 4.4, 4.5, 4.6, 5.1, 5.4, 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 7.9, 8.1, 9.1_

- [x] 7. Update Millennium plugin packaging for Lua backend
  - Update `apps/millenium/plugin.json` to set `useBackend`, `backend`, `frontend`, and `backendType` for Lua.
  - Update `apps/millenium/build.ps1` to package the Lua backend files and exclude removed Python-only files.
  - Update `apps/millenium/dev.ps1` so dev/local installs use `playdex-dev` and production package state restores to `playdex`.
  - Ensure production build output remains a versioned `playdex-<version>.zip` archive.
  - _Requirements: 1.1, 1.4, 1.5, 8.2, 8.3, 8.4_

- [x] 8. Update frontend RPC and lifecycle monitoring
  - Update `apps/millenium/frontend/rpc.ts` to remove or stop exposing `OnNonSteamAppLaunch`.
  - Update `apps/millenium/frontend/index.tsx` so first observation and recurring heartbeat both use `RPC.OnNonSteamAppHeartbeat`.
  - Ensure `onAppLaunch` still sends one `RPC.OnNonSteamAppQuit` when a tracked non-Steam app disappears.
  - Preserve app id filtering with `NON_STEAM_APP_APPID_MASK`, the `inFlight` guard, and independent state per running app id.
  - Preserve `forceFakeLocationChange()` behavior after ping and per-game settings saves.
  - Run `pnpm check` in `apps/millenium`.
  - _Requirements: 1.6, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 6.5, 7.1, 7.2, 8.5, 9.3, 9.4_

- [x] 9. Verify and adjust Steam settings UI integration
  - Confirm the plugin settings panel still loads the global API key through Lua RPC and persists debounced changes.
  - Confirm the Playdex app properties page still appears only for non-Steam apps and does not duplicate itself.
  - Update `PlaydexGameSettings` save handling if Lua RPC now returns explicit save success/failure values.
  - Ensure empty per-game API key and empty custom game id values are persisted rather than ignored.
  - Run `pnpm lint` and `pnpm check` in `apps/millenium`.
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.8, 4.9, 5.7, 6.1, 6.2, 6.3, 6.4, 6.6, 8.5_

- [ ] 10. Wire final checks and regression coverage
  - Run the updated API Playnite tests with database access.
  - Run the full relevant API test suite if database access is available.
  - Run the Lua backend test command.
  - Run `pnpm check` and `pnpm lint` for `apps/millenium`.
  - Build the Millennium plugin package and verify the archive contains `plugin.json`, frontend build output, and Lua backend files only.
  - Fix any failures by updating the smallest relevant API, Lua backend, frontend, or packaging surface.
  - _Requirements: 8.2, 8.3, 8.5, 9.1, 10.1, 10.2_
