# Game Session Wrapper Smoke Test

1. Build both projects from the repository root:
   `dotnet build PlaydexWrapper.sln`

2. Publish the single-file wrapper:
   `dotnet publish src/GameSessionWrapper/GameSessionWrapper.csproj -c Release -r win-x64 --self-contained true`

3. Start `GameSessionWrapper.exe` directly. Confirm the Playdex Wrapper window opens and does not launch a game. The default configuration is stored under the current user's local application-data directory at `PlaydexWrapper\config.json`.

4. Open Settings. Clear the API base URL, save, close the wrapper, and start it again. Confirm the empty value persists and is accepted as API tracking disabled.

5. Choose Add a game and select a game executable. Confirm the executable filename becomes the internal game ID. If `steam_appid.txt` beside the executable contains a numeric AppID, confirm it becomes the API game ID; otherwise confirm the internal game ID is used.

6. Open the game's overflow menu and choose Game settings. Change the display name and API game ID, save, and confirm the game row refreshes without changing the generated launcher path.

7. Launch the game from its row. Confirm the wrapper window closes, the same wrapper process launches the game, and the log contains `api tracking disabled`. Confirm no API requests or hook commands are attempted.

8. Start the wrapper again, configure a valid API base URL and key, and launch the game. Confirm the log records `api ping sent` immediately and at the configured interval while the game runs. After exit or crash, confirm no further pings occur.

9. Confirm a `Playdex-<game-id>-<hash>.exe` with an adjacent `.playdex.json` exists under the current user's local application-data directory at `PlaydexWrapper\launchers`. The manifest must contain only `configPath` and `gameId`, never API credentials.

10. In Steam, select Games -> Add a Non-Steam Game -> Browse, choose the generated launcher, and leave its launch options empty. Starting it must directly launch its assigned game while the generated launcher remains active until monitoring finishes.

11. Rebuild the main wrapper, choose Sync, and confirm the same per-game launcher paths are refreshed. If the wrapper is a multi-file Debug build, synchronization must locate the newest single-file Release build.

12. Remove a game and cancel the confirmation; confirm nothing changes. Repeat and confirm removal. Verify the installed game executable remains while the generated launcher and manifest are deleted.
