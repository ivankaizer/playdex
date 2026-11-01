# Launcher UI screenshots

Reference shots of the launcher UI (`src/Launcher/ui`) as of 2026-11-01, taken before the redesign. They come from the
Vite dev server in a browser, so every screen shows the UI's built-in sample data, not a real library or account.

| Folder                 | Mode                                  | Viewport  |
| ---------------------- | ------------------------------------- | --------- |
| `big-picture/playdex/` | Big Picture, Playdex layout (default) | 1920×1080 |
| `big-picture/sidebar/` | Big Picture, Sidebar layout           | 1920×1080 |
| `desktop/`             | Desktop mode                          | 1440×900  |

Each folder has a `dark/` and a `light/` theme with every tab: Home, Library (list and grid in desktop mode), Playdex
(Overview and Recap), Manage (desktop mode only), Settings, and the Quick settings panel open over Home. A `-full`
file is the same screen with the window made tall enough to show everything its scrolling area holds.

To retake them, run `bun run dev` in `src/Launcher/ui` and open `http://localhost:5190/` with these query parameters:
`?mode=desktop` for desktop mode, `?skin=sidebar` for the Sidebar layout, and `?theme=light` for the light theme.
