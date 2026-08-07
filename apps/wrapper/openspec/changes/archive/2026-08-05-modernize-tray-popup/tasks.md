## 1. Popup Foundation

- [x] 1.1 Add centralized light and dark tray palettes with Windows application-theme detection.
- [x] 1.2 Implement the borderless, rounded, DPI-aware popup shell with screen clamping and dismissal behavior.

## 2. Game Experience

- [x] 2.1 Render configured games as keyboard-accessible launch rows with title, provider identity, and executable icon fallback.
- [x] 2.2 Add the per-game overflow removal action and the no-games empty state.

## 3. Tray Integration

- [x] 3.1 Add prominent Add game and grouped Settings, launcher folder, synchronization, and Exit actions.
- [x] 3.2 Replace the stock tray context menu and refresh popup content after catalog mutations.

## 4. Verification

- [x] 4.1 Add focused tests for theme selection, game-row presentation data, and popup screen positioning.
- [x] 4.2 Run the test suite, debug build, single-file publish, and strict OpenSpec validation.

## 5. High-DPI Regression Fix

- [x] 5.1 Replace mixed automatic and fixed sizing with shared logical-to-device popup metrics.
- [x] 5.2 Remove font-dependent command glyphs and ensure headers, game metadata, actions, and Exit have scaled bounds.
- [x] 5.3 Add representative DPI metric tests and rerun build, tests, publish, and strict validation.
- [x] 5.4 Defer overflow-menu disposal and removal dispatch until WinForms completes item-click processing.
- [x] 5.5 Paint rounded-row corners with the popup background, remove the harsh class shadow, and remove the non-semantic Ready label.
- [x] 5.6 Remove the decorative Playdex header and reclaim its vertical space without changing game-list scrolling.
- [x] 5.7 Tighten popup dimensions and spacing, and size the Remove flyout symmetrically around its text.

## 6. Application Window Lifecycle

- [x] 6.1 Convert the transient popup into a fixed normal application window with standard chrome and taskbar presence.
- [x] 6.2 Open the window at startup, hide user-close events to tray, and restore and activate it from the notification icon while preserving explicit Exit.
- [x] 6.3 Add lifecycle-state tests and rerun build, tests, publish, and strict validation.

## 7. Application Branding

- [x] 7.1 Rename the visible product and window accessibility identity to Playdex Wrapper.
- [x] 7.2 Add a generated Playdex-derived multi-resolution application icon and use it for the executable, window, taskbar, and notification area.
- [x] 7.3 Add focused branding tests and rerun build, tests, publish, and strict validation.

## 8. Game ID Editing

- [x] 8.1 Add Change game ID to each game's overflow menu and provide a validated, explicitly DPI-scaled editor dialog with a SteamDB lookup link.
- [x] 8.2 Persist the edited `ApiGameId`, refresh the row identity, and leave the internal game key and launcher identity unchanged.
- [x] 8.3 Add focused normalization, persistence, and dialog DPI metric tests, then rerun build, tests, publish, and strict validation.

## 9. Display Name Editing

- [x] 9.1 Add Game settings to each game's overflow menu with one validated, DPI-scaled editor for display name and API-facing game ID.
- [x] 9.2 Persist the edited `DisplayName` and `ApiGameId` together and refresh the row without changing game, executable, or launcher identity.
- [x] 9.3 Add focused normalization and persistence tests, then rerun tests, publish, and strict validation.

## 10. Compact Controls

- [x] 10.1 Reduce main-window, overflow, and dialog button dimensions and reclaim unused window height.
- [x] 10.2 Extend DPI metric tests, then rerun tests, publish, and strict validation.
