## 1. Wrapper Foundation

- [x] 1.1 Create the C#/.NET Windows wrapper project and warning-as-error build configuration.
- [x] 1.2 Add JSON configuration shared by command-line launch and tray setup.
- [x] 1.3 Add command-line controls for config, game selection, tray mode, hooks, and dry-run.
- [x] 1.4 Add file logging and a self-contained Windows publish flow.

## 2. Tray and Multi-Game Setup

- [x] 2.1 Add an STA WinForms tray application with contained error handling.
- [x] 2.2 Add persisted multi-game selection and game creation.
- [x] 2.3 Add executable and Ludusavi selection for the active game.
- [x] 2.4 Launch the selected game from the same wrapper process.
- [x] 2.5 Route implicit startup to tray selection and explicit game-id startup to direct launch.
- [x] 2.6 Add global and per-game bearer API-key configuration to JSON and tray setup.

## 3. Launch, Monitoring, and Hooks

- [x] 3.1 Launch the configured executable directly and monitor it until exit.
- [x] 3.2 Support launched-process, executable-path, and executable-name monitor modes.
- [x] 3.3 Apply configurable exit debounce and per-game locking.
- [x] 3.4 Run optional pre-launch and post-exit hooks with skip controls and dry-run logging.
- [x] 3.5 Configure Ludusavi restore and backup as optional hooks.

## 4. Atomic Ping Tracking

- [x] 4.1 Add game title, game id, provider, ping interval, base URL, and bearer-key resolution matching the reference client.
- [x] 4.2 Implement atomic authenticated `POST /playnite/sessions/ping` with a 10-second timeout and no retry or replay.
- [x] 4.3 Send a ping immediately after launch and every configured interval until process monitoring ends.
- [x] 4.4 Log and discard ping failures without blocking the game or persisting API state.
- [x] 4.5 Remove lifecycle start/finish/suspend/resume events, local session state, event journaling, and replay.
- [x] 4.6 Make dry-run log each scheduled ping live without making network calls.

## 5. Verification and Packaging

- [x] 5.1 Cover command parsing, configuration, tray selection, process monitoring, hooks, and launch routing.
- [x] 5.2 Replace lifecycle and replay tests with ping contract, cadence, failure, cancellation, and dry-run tests.
- [x] 5.3 Update example configuration and smoke-test documentation for ping-only tracking and credentials.
- [x] 5.4 Build, run the complete harness, validate OpenSpec, and publish the self-contained executable.
