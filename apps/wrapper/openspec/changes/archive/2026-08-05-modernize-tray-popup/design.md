## Context

The tray application uses a custom Windows Forms surface launched from `NotifyIcon`. All operational behavior is implemented in `TraySetupApplication`, and the published wrapper is a dependency-free single-file executable. The surface needs normal application-window lifecycle behavior while the notification icon remains the background entry point.

## Goals / Non-Goals

**Goals:**
- Modernize the visible tray surface while reusing existing action methods.
- Keep the popup lightweight, dependency-free, DPI-aware, and compatible with single-file publishing.
- Make games the primary content and management operations secondary.

**Non-Goals:**
- Rewriting the application in WinUI or WPF.
- Changing configuration, ping cadence, launcher generation, or process monitoring.
- Downloading game artwork or introducing network-backed UI data.

## Decisions

### Use a normal Windows Forms application window

Use a fixed-size, normally framed `Form` with a taskbar entry. Show it at startup and from `NotifyIcon.MouseClick`. Intercept only user-initiated Close to hide the form and taskbar entry; explicit Exit still ends the application loop. The window does not hide on deactivation and is not always on top. A borderless popup was rejected because its transient focus behavior conflicts with using the surface as an application.

### Build the popup from standard controls

Use layout panels, labels, buttons, and picture boxes with centralized palette and style helpers. Avoid custom raster assets; extract executable icons when possible and fall back to the application icon. Standard controls retain keyboard focus and accessibility behavior more reliably than a fully owner-drawn canvas.

### Rebuild content after catalog mutations

The window owns only presentation and delegates actions to callbacks supplied by `TraySetupApplication`. It rebuilds game rows after adding, editing, or removing games so that configuration remains the source of truth.

### Use a small contextual menu for secondary row actions

The per-game overflow button opens a compact themed menu containing Game settings and Remove. Game settings uses one validated modal editor for `DisplayName` and `ApiGameId`, whose bounds use the same explicit DPI metrics as the main window. The stable internal `GameId` key and generated launcher identity remain unchanged. Include a themed SteamDB link that opens `https://steamdb.info/` through the Windows shell. Keeping Remove in the overflow menu prevents accidental removal and avoids making a destructive command visually equal to Launch.

### Detect the Windows application theme at display time

Read the current user's application light-theme preference when the popup opens and apply a centralized light or dark palette. If the preference cannot be read, use the light palette. This avoids adding Windows App SDK dependencies.

### Scale one logical layout explicitly

Disable automatic WinForms form scaling for the dynamically rebuilt content and convert every logical dimension through one DPI metric object. Determine the active window DPI before rebuilding the controls, then scale the window content, rows, bounds, padding, and drawing radii together. Fonts remain point-based and therefore follow the same device DPI without being scaled twice. Prefer complete text labels for management commands over font-dependent Unicode glyphs.

Keep action controls compact: use shorter primary, management, Exit, overflow, and dialog buttons, and reclaim the resulting window height. Store these logical dimensions in the shared metric objects so every supported DPI uses the same proportions.

### Embed one generated application icon

Use a multi-resolution Windows icon generated from a square Playdex-derived source asset. Embed the ICO in the application executable and extract that embedded icon at runtime for both the window and `NotifyIcon`, keeping Explorer, taskbar, and tray branding aligned without an external runtime asset dependency. Retain the source PNG in the project for future branding work.

## Risks / Trade-offs

- [Closing a normal window usually terminates an application] → Cancel only user-close events, hide the taskbar entry, and keep explicit Exit separate.
- [Executable icon extraction can fail] → Fall back to the wrapper application icon and never block popup construction.
- [A hidden window can remain minimized] → Restore `WindowState` before showing and activating it from the tray icon.

## Migration Plan

Change the existing custom surface to normal window chrome and close-to-tray lifecycle while keeping the notification icon and action handlers. Configuration data requires no migration.
