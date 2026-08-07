## 1. Direct-Launch Tray Menu

- [x] 1.1 Remove current-game status and the separate launch-selected action.
- [x] 1.2 Make each game-list item persist its selection and launch directly with contained errors.
- [x] 1.3 Replace add and executable-change UI with one executable-first, add-only flow.

## 2. Settings Consolidation

- [x] 2.1 Remove API key and identity forms and their tray actions.
- [x] 2.2 Add one Settings action that saves and opens the exact JSON configuration file.

## 3. Ludusavi Removal

- [x] 3.1 Remove Ludusavi configuration, search forms, provider code, and hook templates.
- [x] 3.2 Remove the `--skip-ludusavi` command-line shortcut while preserving generic hook controls.
- [x] 3.3 Remove Ludusavi-specific example configuration, documentation, and tests.

## 4. Verification and Packaging

- [x] 4.1 Add tests for executable-derived game creation, duplicate rejection, and direct game selection.
- [x] 4.2 Build and run the complete test harness with zero warnings.
- [x] 4.3 Validate OpenSpec strictly and publish the updated Debug and single-file executables.

## 5. Steam Identity Discovery

- [x] 5.1 Read a valid AppID from `steam_appid.txt` beside a newly added executable and use it as `apiGameId`.
- [x] 5.2 Cover valid, missing, and invalid Steam AppID files and update setup documentation.

## 6. Game Removal

- [x] 6.1 Add deterministic configuration removal with selected-game fallback.
- [x] 6.2 Add confirmed Remove a game tray submenu and best-effort generated launcher cleanup.
- [x] 6.3 Cover removal and launcher cleanup with tests and update documentation.
