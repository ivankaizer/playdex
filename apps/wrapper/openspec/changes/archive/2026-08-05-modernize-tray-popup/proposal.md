## Why

The stock Windows Forms context menu makes the tray application feel dated and separates closely related game actions into nested menus. A normal application window backed by a tray icon can make launching and managing games clearer while remaining available when the window is closed.

## What Changes

- Replace the tray icon's stock context menu with a normal application window that opens at startup.
- Make the window Close button hide the application to the notification area, and make a tray-icon click restore and activate it.
- Keep explicit Exit as the operation that terminates the tray application.
- Present configured games as rich rows with executable icons, title, provider identity, launch behavior, and a per-game overflow action.
- Consolidate game settings and removal into each game's overflow menu, with one editor for display name and API-facing game ID while preserving removal confirmation and safety behavior.
- Present Add game prominently and group Settings, launcher folder, synchronization, and Exit as secondary actions.
- Add light and dark palettes, DPI-aware sizing, keyboard focus, standard window behavior, and an empty state.
- Use compact action and dialog buttons so management controls do not dominate the game list.
- Present the product as Playdex Wrapper and use one Playdex-derived application icon across the executable, window, taskbar, and notification area.

## Capabilities

### New Capabilities
- `modern-tray-popup`: Modern visual presentation and interaction requirements for the Playdex tray popup.

### Modified Capabilities

None.

## Impact

- Affects the Windows Forms tray presentation in `src/GameSessionWrapper` and its UI-oriented tests.
- Preserves configuration, launcher generation, game monitoring, heartbeat, and API behavior.
- Adds no external runtime dependency and retains the existing single-file publishing model.
