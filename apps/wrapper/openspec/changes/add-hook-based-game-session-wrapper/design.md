## Context

See `proposal.md` for motivation. The reference Millennium client sends a bearer-authenticated JSON game identity to `POST /api/playnite/sessions/ping`. The server treats the first ping as session start and closes a session after three minutes without a ping, so client-side lifecycle events and replay are unnecessary.

## Goals / Non-Goals

**Goals:**

- Launch and monitor configured games directly.
- Send one atomic ping immediately after launch and every 30 seconds while the game is running.
- Stop pings when monitoring ends and let the server timeout handle game or wrapper crashes.
- Log ping failures without delaying launch or accumulating retry state.
- Support global API credentials with an optional per-game override.
- Keep tray-based multi-game setup, dry-run behavior, and optional hooks.

**Non-Goals:**

- Persist or replay API events.
- Send explicit start, finish, suspend, or resume events.
- Reconstruct sessions after wrapper crashes.
- Implement server-side timeout or playtime accounting in the wrapper.
- Use Ludusavi as the game-process supervisor.

## Decisions

### API tracking uses atomic pings only

The wrapper will send `POST {baseUrl}/playnite/sessions/ping` with a 10-second timeout, `Authorization: Bearer <api-key>`, and this JSON body:

```json
{
  "gameTitle": "Cronos. The New Dawn",
  "gameId": "configured-game-id",
  "gameProvider": "steam"
}
```

`gameTitle`, `gameId`, and `gameProvider` come from the active game configuration. An API key may be configured globally and overridden per game. A failed ping is logged and discarded; the next scheduled ping is independent.

Alternatives considered:

- Start/heartbeat/finish events with a durable journal: rejected because the server's ping is already atomic and owns session lifecycle.
- Retrying missed pings: rejected because stale liveness signals would misrepresent when the game was active.

### Ping scheduling follows process monitoring

The ping loop starts after the game process launches, sends immediately, then waits the configured interval between pings. It stops after the process monitor's exit debounce completes. If the wrapper or game crashes, pings stop naturally and the server closes the session after three minutes. Sleep requires no explicit event: the lack of pings closes the old session, and a ping after resume can start a new one if the game remains active.

### Dry-run logs live ping attempts

Dry-run uses the same scheduler but replaces the HTTP operation with an immediate `dry-run api ping` log entry. It does not read, acknowledge, delete, or otherwise interact with any prior event journal.

### Setup remains tray-only

Normal startup opens the tray selector; explicit `--game-id` supports direct launch. The tray menu manages multiple games, executable selection, Ludusavi selection, and the bearer API key. Launching from the tray exits setup and continues monitoring in the same process.

### Hooks remain phase-based and optional

Pre-launch and post-exit hooks remain independent of API pings. Hook failures are logged and must not block game launch. Ludusavi remains an optional restore/backup hook rather than a process wrapper.

## Risks / Trade-offs

- A ping can be lost during a transient outage -> The next independent ping occurs after the configured interval; no stale event is replayed.
- The server may close a session during sleep or a long outage -> This is intentional and matches the three-minute liveness contract.
- A ping can continue during exit debounce -> Keep the debounce short and configurable; monitoring still treats the game as potentially restarting.
- Old `.events.json` and `.session.json` files may remain from previous builds -> New builds ignore them; users may delete them without affecting ping tracking.
- Missing or invalid API credentials disable tracking -> Log a clear warning while continuing game launch.

## Migration Plan

1. Add ping identity and bearer-key configuration.
2. Replace lifecycle API, state, event journal, replay, and power-event code with the atomic ping client and scheduler.
3. Update dry-run logging, tests, examples, and smoke instructions.
4. Publish a new self-contained wrapper.

Rollback uses the previous wrapper build. Existing local event files are not migrated or replayed.
