## 1. Launcher Routing

- [x] 1.1 Add a credential-free launcher manifest model and startup resolution before command-line routing.
- [x] 1.2 Make missing or invalid launcher manifests fail safely with actionable logging.
- [x] 1.3 Verify a manifest-selected launcher directly runs its assigned game and retains the existing session lifetime.

## 2. Launcher Generation

- [x] 2.1 Publish the Windows wrapper as a self-contained single-file executable.
- [x] 2.2 Add deterministic launcher naming and configurable launcher storage.
- [x] 2.3 Generate or refresh each launcher using a hard link with a copy fallback and write its adjacent manifest atomically.
- [x] 2.4 Synchronize existing games without placing credentials in launcher artifacts.

## 3. Tray Experience

- [x] 3.1 Generate a launcher when a game is added and synchronize all launchers at tray startup.
- [x] 3.2 Add tray actions to regenerate launchers and open the launcher directory.
- [x] 3.3 Surface launcher-generation failures without losing saved game configuration.

## 4. Verification and Documentation

- [x] 4.1 Add tests for manifest routing, deterministic paths, synchronization, invalid manifests, and credential exclusion.
- [x] 4.2 Update example configuration and Steam setup documentation for argument-free per-game executables.
- [x] 4.3 Run the complete test harness and strict OpenSpec validation.
- [x] 4.4 Publish the single-file Windows executable and verify generated launcher behavior.

## 5. Multi-File Publish Compatibility

- [x] 5.1 Resolve a sibling single-file launcher source automatically when tray setup is started from an older multi-file publish.
- [x] 5.2 Add regression coverage for multi-file source discovery and rerun verification.

## 6. Debug Apphost Compatibility

- [x] 6.1 Discover the newest single-file publish under the current project's bounded `bin/Release` tree when started from Debug.
- [x] 6.2 Add regression coverage for the Debug path and rebuild the executable used by the reported command.
