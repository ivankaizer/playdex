# Playdex .NET API

The .NET API preserves the supported behavior of the archived Bun API at commit `7bf8e76`. The route-by-route scope, normalization rules, and the two approved .NET-only Playnite routes are recorded in [`../../openspec/changes/restore-bun-api-feature-parity/compatibility-manifest.md`](../../openspec/changes/restore-bun-api-feature-parity/compatibility-manifest.md).

## Runtime configuration

Required:

- `DATABASE_URL`: PostgreSQL connection URL.
- `JWT_SECRET`: signing secret of at least 32 bytes.

Optional:

- `TELEGRAM_BOT_TOKEN`: enables inbound bot commands and outbound session/poll-failure notifications. With no token, Telegram is disabled and the rest of the API remains operational.
- `POLL_INTERVAL_SECONDS`: credential poll interval, clamped from 10 to 3600 seconds.
- `STEAM_API_KEY`, `IGDB_CLIENT_ID`, `IGDB_CLIENT_SECRET`: provider integration credentials.
- `CORS_ORIGINS`, `ACCESS_TOKEN_TTL`: HTTP authentication/runtime settings.

Telegram session cards are 1280×1024 WebP images rendered with SkiaSharp. The API image includes the Linux native Skia assets. Cover download, rendering, or `sendPhoto` failure falls back to a text notification; Telegram failures never roll back session state.

## Verification

Run API tests, including PostgreSQL integration tests:

```sh
PLAYDEX_INTEGRATION_DATABASE_URL=postgresql://playdex:playdex@127.0.0.1:5432/playdex \
  dotnet test apps/api-dotnet/Playdex.Api.Tests/Playdex.Api.Tests.csproj --configuration Release
```

Run the differential harness against independently seeded APIs/databases:

```sh
BUN_API_BASE_URL=http://127.0.0.1:3001 \
DOTNET_API_BASE_URL=http://127.0.0.1:3002 \
BUN_CONTRACT_DATABASE_URL=postgresql://playdex:playdex@127.0.0.1:5432/playdex_bun \
DOTNET_CONTRACT_DATABASE_URL=postgresql://playdex:playdex@127.0.0.1:5432/playdex_dotnet \
scripts/run-api-compatibility.sh
```

`BUN_DATABASE_RESET_COMMAND`, `DOTNET_DATABASE_RESET_COMMAND`, `BUN_API_START_COMMAND`, and `DOTNET_API_START_COMMAND` may be supplied when the harness should reset and launch its targets. Otherwise it targets already-running APIs.

Strict build and production image checks:

```sh
dotnet build apps/api-dotnet/Playdex.Api/Playdex.Api.csproj --configuration Release --warnaserror
docker compose build app
```

## Deployment checks

1. Confirm `/health/live` and `/health/ready` return success.
2. Confirm startup reports Telegram enabled or one actionable disabled warning, never a dependency-lifetime failure.
3. With a linked test chat, verify a provider-detected start sends a photo, a no-game result sends an end notification, and an unavailable cover sends text fallback.
4. Verify Playnite ping and matching-game end with a test credential.
5. Check poll metrics and logs for credential-specific backoff without delays to unrelated credentials.

The change has no schema migration. Roll back by deploying the previous .NET image; sessions, games, and poll results written by this version remain compatible and require no data repair.
