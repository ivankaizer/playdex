## Purpose

Provide a minimal tray experience where users launch configured games directly, add new executables once, and open one settings file for advanced configuration.

## ADDED Requirements

### Requirement: Minimal tray menu
The tray menu SHALL omit current-game status, executable-change actions, separate API configuration actions, Ludusavi actions, and a separate launch-selected action.

#### Scenario: User opens the tray menu
- **WHEN** the tray menu is displayed
- **THEN** it presents the game list, Add a game, Settings, launcher management, and Exit without the removed actions

### Requirement: Game-list items launch directly
Each configured game item in the tray game list SHALL directly select and launch that game through the wrapper.

#### Scenario: Configured game is clicked
- **WHEN** the user clicks a game in the game list
- **THEN** the tray closes and the wrapper launches and monitors that game

#### Scenario: Configured game cannot launch
- **WHEN** the user clicks a game whose executable is unavailable
- **THEN** the tray remains available and displays a contained failure notification

### Requirement: Add game from executable
The tray SHALL provide one Add a game action that creates a new game from a selected executable and SHALL NOT alter an existing game through that action.

#### Scenario: New executable is selected
- **WHEN** the user chooses an executable whose derived game ID is not configured
- **THEN** the system creates and saves the game, generates its launcher, and adds it to the game list

#### Scenario: Steam AppID file is present
- **WHEN** `steam_appid.txt` beside the selected executable contains a positive numeric Steam AppID
- **THEN** the new game uses that value as its backend API game ID while retaining its executable-derived wrapper ID

#### Scenario: Steam AppID file is unusable
- **WHEN** `steam_appid.txt` is missing, unreadable, empty, or invalid
- **THEN** game creation succeeds using the executable-derived value as its backend API game ID

#### Scenario: Existing game ID would be changed
- **WHEN** the selected executable derives an already configured game ID
- **THEN** the system rejects the addition and leaves the existing game unchanged

#### Scenario: Picker is cancelled
- **WHEN** the user cancels the executable picker
- **THEN** the system creates no game and changes no configuration

### Requirement: Single JSON settings action
The tray SHALL expose one Settings action that saves and opens the active JSON configuration file using the operating system's associated editor.

#### Scenario: Settings is clicked
- **WHEN** the user selects Settings
- **THEN** the system opens the JSON configuration file containing API, identity, hook, storage, and game settings

### Requirement: Remove configured game
The tray SHALL provide a separate Remove a game submenu and SHALL require confirmation before removing a game from wrapper configuration.

#### Scenario: Removal is confirmed
- **WHEN** the user selects a configured game under Remove a game and confirms the warning
- **THEN** the system removes its configuration and generated launcher executable and manifest without deleting the installed game executable

#### Scenario: Removal is cancelled
- **WHEN** the user declines the confirmation
- **THEN** the system changes no configuration or files

#### Scenario: Selected game is removed
- **WHEN** the removed game was the persisted selected game
- **THEN** the system selects another configured game deterministically or clears the selection when none remain

#### Scenario: Steam shortcut exists
- **WHEN** Steam has a shortcut targeting the removed generated launcher
- **THEN** the wrapper leaves Steam configuration unchanged and the user may remove the stale shortcut in Steam

### Requirement: No Ludusavi-specific behavior
The wrapper SHALL contain no Ludusavi-specific tray actions, configuration fields, search implementation, hook templates, or command-line shortcut while continuing to support generic hooks.

#### Scenario: Existing configuration contains generic hooks
- **WHEN** the wrapper loads and runs a game with generic pre-launch or post-exit hooks
- **THEN** those hooks continue to execute according to their JSON configuration

#### Scenario: Removed command-line option is used
- **WHEN** the wrapper is started with `--skip-ludusavi`
- **THEN** it rejects the unknown option instead of applying integration-specific behavior
