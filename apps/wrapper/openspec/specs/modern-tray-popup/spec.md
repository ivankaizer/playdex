# modern-tray-popup Specification

## Purpose

Provide a compact, accessible tray experience for launching and managing configured games without exposing the structure of a legacy nested context menu.

## Requirements

### Requirement: Tray application uses a restorable window
The tray application SHALL present a normal application window, SHALL hide that window to the notification area when the user closes it, and SHALL terminate only through an explicit Exit operation.

#### Scenario: Tray application starts
- **WHEN** the user starts the tray application
- **THEN** the application window is visible with normal window chrome and a taskbar entry

#### Scenario: User closes the window
- **WHEN** the user activates the window Close button
- **THEN** the window and its taskbar entry are hidden while the notification icon and tray application remain running

#### Scenario: User restores the window
- **WHEN** the hidden tray application receives a notification-icon click
- **THEN** the application window is shown, restored from a minimized state if necessary, and activated

#### Scenario: User exits explicitly
- **WHEN** the user activates Exit Playdex Wrapper
- **THEN** the tray application and its notification icon terminate

### Requirement: Games are presented as launchable rows
The application window SHALL display each configured game as a directly launchable row containing its display name, provider identity, and available executable icon.

#### Scenario: User launches a configured game
- **WHEN** the user activates a configured game row
- **THEN** the popup launches that configured game through the existing wrapper session flow

#### Scenario: No games are configured
- **WHEN** the window opens with an empty game catalog
- **THEN** it displays a clear empty state and keeps the Add game action available

### Requirement: Game management is contextual
Each configured game row SHALL expose a secondary game-settings action for changing its display name and API-facing game ID together, plus an action for removing the game, and SHALL retain the existing removal confirmation and file-safety behavior.

#### Scenario: User changes a game's display name
- **WHEN** the user opens a game's settings, enters a non-empty name, and saves
- **THEN** the application persists the trimmed value as that game's `DisplayName` and refreshes the displayed row without renaming the internal game key, API game ID, executable, or generated launcher

#### Scenario: User changes a game's API identity
- **WHEN** the user opens a game's settings, enters a non-empty ID, and saves
- **THEN** the application persists the trimmed value as that game's `ApiGameId`, refreshes the displayed provider identity, and uses the new value for subsequent pings without renaming the internal game key or generated launcher

#### Scenario: User needs to find a Steam game ID
- **WHEN** the game-ID editor is open and the user activates its SteamDB link
- **THEN** the application opens `https://steamdb.info/` in the default browser

#### Scenario: User removes a game from its row
- **WHEN** the user selects Remove from a game's secondary action and confirms the warning
- **THEN** the game configuration and generated launcher artifacts are removed without deleting installed game files

### Requirement: Management actions remain available
The application window SHALL provide Add game, Settings, launcher folder, launcher synchronization, and Exit actions without nested top-level menus.

#### Scenario: User accesses a management action
- **WHEN** the user activates a management action
- **THEN** the corresponding existing tray operation executes

### Requirement: Window adapts to the Windows environment
The application window SHALL support light and dark application palettes, DPI-aware layout, keyboard focus navigation, and visible hover and focus feedback.

#### Scenario: Windows uses dark application mode
- **WHEN** the window opens while Windows is configured for dark application mode
- **THEN** the window uses the dark palette with legible text and interaction states

### Requirement: Application branding is consistent
The application SHALL present the product name Playdex Wrapper and SHALL use a Playdex-derived application icon consistently in the executable, application window, taskbar, and notification area.

#### Scenario: User views an application surface
- **WHEN** the user views the executable, application window, taskbar entry, or notification icon
- **THEN** that surface uses the Playdex Wrapper name where a name is shown and the same Playdex Wrapper icon where an icon is shown
