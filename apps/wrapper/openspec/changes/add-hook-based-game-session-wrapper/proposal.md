## Why

Launching GOG games through Steam needs a wrapper that keeps Steam playtime tracking alive while also reporting reliable play sessions to the custom API. Ludusavi should help with save restore and backup, but it should not supervise the game process or be required for every launch.

## What Changes

- Add a game session wrapper that launches and monitors a configured game process directly.
- Add API-backed play tracking through one atomic authenticated ping every 30 seconds while the game is running; the server starts sessions on the first ping and closes them after three minutes without a ping.
- Add hook-based pre-launch and post-exit automation, with Ludusavi restore/backup configured as optional hooks.
- Add launch-time controls to skip all hooks, skip pre/post hooks, or skip specific hooks such as Ludusavi.
- Add a dry-run log provider that records each scheduled ping and hook command without making API calls or executing external commands.
- Make normal wrapper startup open a tray-only surface that can add, select, configure, and launch multiple games, with a file picker for each game executable and a search window for selecting its Ludusavi game name; reserve direct launch for an explicit game id.
- Add failure policies so API or hook failures do not prevent the game from launching.

## Capabilities

### New Capabilities

- `game-session-wrapper`: Launching, monitoring, tray setup, session tracking, suspend/resume handling, hook execution, and recovery behavior for wrapped game sessions.

### Modified Capabilities

- None.

## Impact

- Adds a wrapper application, likely as a .NET/C# executable intended to be launched by Steam.
- Adds wrapper configuration for game launch, process monitoring, tray setup, API base URL and bearer key, hook definitions, and skip controls.
- Integrates with the existing atomic `POST /api/playnite/sessions/ping` contract and does not persist or replay API events.
- Integrates with Ludusavi through command hooks instead of `ludusavi wrap`.
- Adds local persistence for configuration and logs while leaving API session liveness to the server.
