## Context

The wrapper currently publishes as a self-contained multi-file application and selects a game through `--game-id`. Steam can monitor the wrapper for the whole session, but users must manually reproduce the command. See `proposal.md` and `specs/per-game-launchers/spec.md` for the intended behavior.

## Goals / Non-Goals

**Goals:**

- Produce a directly selectable `.exe` per configured game with no Steam launch arguments.
- Keep one central game configuration and API credential store.
- Preserve the existing wrapper process as the lifetime Steam observes.
- Make launcher regeneration deterministic and safe.

**Non-Goals:**

- Editing Steam's undocumented `shortcuts.vdf` file or restarting Steam.
- Making Steam treat the launcher as the original store AppID.
- Embedding API keys or game configuration in generated executables.

## Decisions

### Publish the wrapper as one self-contained executable

The release wrapper will use .NET single-file self-contained publishing. A generated per-game launcher is a hard link to that executable when possible, with a file copy fallback. This provides a real `.exe` per game without duplicating an entire runtime directory.

Alternatives considered: a framework-dependent bootstrap would require a separately installed .NET runtime; compiling a bespoke executable for every game would add build tooling and latency; Windows `.lnk` files are less consistently accepted by Steam's program picker.

### Route generated launchers through adjacent manifests

Each launcher has a same-name `.playdex.json` manifest containing an absolute config path and wrapper game ID. On startup, the wrapper checks for that manifest before parsing command-line arguments and synthesizes an explicit direct-launch selection. The manifest contains no API credentials.

Alternative considered: encode the game ID in the filename. That limits valid IDs, complicates renames and collisions, and cannot safely carry a custom configuration path.

### Use deterministic filenames

Launcher filenames use a sanitized wrapper game ID plus a short hash of the complete game ID. This keeps paths readable, collision-resistant, and stable when the display name changes.

### Synchronize in tray setup

Tray startup synchronizes all configured games. Adding a game synchronizes it immediately, and explicit tray actions expose regeneration and the launcher folder. Failures are logged and shown without corrupting central configuration.

## Risks / Trade-offs

- [A launcher hard link can retain an older binary after publish replaces the main executable] → Tray synchronization recreates launcher links/copies from the currently running wrapper.
- [Fallback copies consume the size of one single-file wrapper per game] → Prefer NTFS hard links and log when copying is necessary.
- [Users may start an older multi-file publish or a Debug apphost] → Detect non-single-file execution and automatically use a sibling or project `bin/Release` single-file publish when present; otherwise explain which build is required.
- [Steam may associate overlay behavior with the non-Steam shortcut rather than the original store AppID] → Preserve the full launcher lifetime; automatic store-AppID substitution remains out of scope.

## Migration Plan

1. Publish the wrapper as a self-contained single file.
2. On the first tray startup, generate launchers for all existing games.
3. Users add the desired per-game executable from the launcher folder to Steam once.
4. Rollback by using the main wrapper with `--game-id`; generated manifests contain no unique state and can be ignored.
