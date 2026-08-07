## Purpose

Provides a Windows game wrapper that launches and monitors configured games, reports activity through atomic authenticated pings, offers tray-based setup, and runs optional hooks.

## ADDED Requirements

### Requirement: Wrapper launches and monitors configured games
The system SHALL launch the configured game command directly and keep the wrapper process alive until the monitored game session is complete.

#### Scenario: Direct game launch succeeds
- **WHEN** a user launches a configured game through the wrapper
- **THEN** the wrapper starts the configured executable without requiring Ludusavi to supervise it

#### Scenario: Game exit is debounced
- **WHEN** the monitored game process stops
- **THEN** the wrapper waits the configured exit debounce before treating the game as closed

### Requirement: Wrapper reports play through atomic pings
The system SHALL send an authenticated game-identity ping immediately after launch and at the configured interval while the monitored game is running.

#### Scenario: First ping starts server-side session tracking
- **WHEN** the game starts running
- **THEN** the wrapper posts `{ gameTitle, gameId, gameProvider }` to `/api/playnite/sessions/ping` using the configured bearer API key

#### Scenario: Pings continue while running
- **WHEN** the monitored game remains active
- **THEN** the wrapper sends another atomic ping every 30 seconds by default

#### Scenario: Ping fails
- **WHEN** a ping times out or the API returns an unsuccessful response
- **THEN** the wrapper logs the failure, discards that ping, and continues running the game without replaying it

#### Scenario: Game or wrapper stops
- **WHEN** the game exits, the game crashes, or the wrapper terminates
- **THEN** the wrapper sends no further pings and relies on the server to close the session after its three-minute timeout

#### Scenario: System sleeps
- **WHEN** the computer sleeps long enough for the server timeout to elapse
- **THEN** pings stop naturally and a later ping after resume may start a new server-side session if the game is still running

### Requirement: Wrapper runs optional hooks
The system SHALL support configured pre-launch and post-exit hooks that can be enabled, disabled, and skipped independently of game process monitoring.

#### Scenario: Pre-launch hook runs
- **WHEN** an enabled pre-launch hook is not skipped
- **THEN** the wrapper runs it before launching the game

#### Scenario: Post-exit hook runs
- **WHEN** an enabled post-exit hook is not skipped
- **THEN** the wrapper runs it after the monitored game exits

#### Scenario: Ludusavi is configured as hooks
- **WHEN** Ludusavi restore or backup is enabled for a game
- **THEN** the wrapper invokes Ludusavi through pre-launch or post-exit hooks rather than `ludusavi wrap`

### Requirement: Wrapper supports dry-run execution
The system SHALL support a dry-run mode that logs scheduled pings and hook commands without making API calls or executing hooks.

#### Scenario: Dry-run ping
- **WHEN** a ping is scheduled in dry-run mode
- **THEN** the wrapper immediately logs `dry-run api ping` with the game identity and sends no network request

#### Scenario: Dry-run hook
- **WHEN** a hook is scheduled in dry-run mode
- **THEN** the wrapper logs the command without executing it

#### Scenario: Dry-run launch monitoring
- **WHEN** dry-run mode is enabled
- **THEN** normal game launch and process monitoring still occur

### Requirement: Wrapper provides multi-game tray setup and launch
The system SHALL provide a tray-only surface for adding, selecting, configuring, and launching wrapped games.

#### Scenario: User opens wrapper without a game id
- **WHEN** the wrapper starts without an explicit `--game-id`
- **THEN** it opens the tray selector and does not automatically launch a configured game

#### Scenario: User explicitly launches by game id
- **WHEN** the wrapper starts with an explicit configured `--game-id` and without `--tray`
- **THEN** it launches and monitors that game directly

#### Scenario: User selects a configured game
- **WHEN** the user selects a game from the tray game list
- **THEN** the wrapper persists that selection and applies setup actions to it

#### Scenario: User adds a game
- **WHEN** the user enters a new game identity and selects its executable
- **THEN** the wrapper creates and selects a separate game configuration

#### Scenario: User launches selected game
- **WHEN** the user chooses the launch action for a selected configured game
- **THEN** tray setup closes and the same wrapper process launches and monitors that game
