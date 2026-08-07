## Purpose

Provide stable per-game executable targets that launch configured games through the wrapper and remain observable for the complete game session.

## ADDED Requirements

### Requirement: Automatic per-game launcher generation
The system SHALL create or refresh one persistent launcher executable and manifest for every configured game when tray setup starts and when a game is added.

#### Scenario: Game is added in the tray
- **WHEN** the user adds a game through tray setup
- **THEN** the system creates a launcher executable assigned to that game's wrapper ID

#### Scenario: Existing configuration opens in the tray
- **WHEN** tray setup opens with previously configured games
- **THEN** the system ensures each game has a current launcher executable and manifest

### Requirement: Argument-free game routing
A generated launcher SHALL resolve its central configuration path and assigned wrapper game ID from its adjacent manifest without requiring launch arguments.

#### Scenario: Steam starts a generated launcher
- **WHEN** Steam starts the per-game executable with no wrapper arguments
- **THEN** the wrapper directly launches the assigned configured game without opening the selector

#### Scenario: Launcher manifest is invalid
- **WHEN** a generated launcher has a missing or invalid manifest
- **THEN** the wrapper logs the error and exits without launching an arbitrary game

### Requirement: Observable session lifetime
The generated launcher process SHALL remain running while the assigned game is being launched, monitored, and pinged, and SHALL exit after monitoring and post-exit hooks complete.

#### Scenario: Game remains running
- **WHEN** the assigned game process is running
- **THEN** the generated launcher process remains running and continues scheduled pings

#### Scenario: Game exits or crashes
- **WHEN** game monitoring detects that the assigned game has ended
- **THEN** the generated launcher stops pinging, completes post-exit handling, and exits

### Requirement: Stable and lightweight launcher artifacts
Each game's launcher path SHALL remain stable across regeneration, SHALL not expose API credentials, and SHALL share or copy only the single-file wrapper executable rather than a full runtime directory.

#### Scenario: Launcher is regenerated
- **WHEN** launcher synchronization runs again for an existing game
- **THEN** the launcher remains at the same path and its manifest reflects the current central configuration path

#### Scenario: Launcher files are inspected
- **WHEN** a user or another program reads the launcher manifest
- **THEN** it contains only routing data and no bearer API key

### Requirement: Launcher access from tray setup
The tray application SHALL let the user open the launcher directory and manually regenerate all configured launchers.

#### Scenario: User wants to add a launcher to Steam
- **WHEN** the user selects the launcher-folder action
- **THEN** the system opens the directory containing the persistent per-game executables

