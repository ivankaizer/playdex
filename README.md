# Playdex

Playtime tracker for PSN and Steam accounts, built with a .NET 10 API and
Blazor WebAssembly UI. Bun remains available for the legacy differential
reference API and Kysely database migrations during the backend cutover.

## Requirements

- .NET 10 SDK
- Bun 1.3+
- Docker (for local Postgres)

## Local setup

1. Start Postgres via Docker:

```bash
docker compose up -d db
```

2. Create a `.env` file at the repo root:

```bash
DATABASE_URL=postgresql://playdex:playdex@localhost:5432/playdex
POLL_INTERVAL_SECONDS=180
TELEGRAM_BOT_TOKEN=your-telegram-bot-token
```

3. Install migration and CSS dependencies:

```bash
cd apps/api
bun install
cd ../web
bun install
bun run css:build
```

4. Start the .NET API from the repository root:

```bash
DATABASE_URL=postgresql://playdex:playdex@localhost:5432/playdex \
JWT_SECRET=replace-with-at-least-32-random-bytes \
dotnet run --project apps/api-dotnet/Playdex.Api/Playdex.Api.csproj
```

5. Start the Blazor UI:

```bash
cd apps/web
dotnet watch --project Playdex.Web.csproj --urls http://127.0.0.1:5173
```

Open `http://127.0.0.1:5173/`. For the containerized development stack, run
`docker compose up --build db app ui` instead.

## Useful commands

```bash
cd apps/api
bun run format
bun run format:check
bun run check
bun run db:migrate
bun run db:seed
bun run telegram:notify-image --type started --title "Like a Dragon Gaiden: The Man Who Erased His Name" --platform "Steam" \
  --image "https://shared.steamstatic.com/store_item_assets/steam/apps/2375550/library_600x900_2x.jpg" \
  --chat-id 77434259
```

```bash
cd apps/web
bun run css:build
dotnet test Playdex.Web.Tests/Playdex.Web.Tests.csproj
dotnet publish Playdex.Web.csproj -c Release
```

## Database migrations

Migrations use [Kysely](https://www.kysely.dev/) with TypeScript modules under
`apps/api/migrations/`. Run `bun run db:migrate` as the authoritative database
deployment step before starting the .NET API. The legacy Bun reference API also
uses that same migration runner on startup.

The .NET API additionally runs its idempotent `001_current_schema` bootstrap on
startup. It can create the current schema in a brand-new database or record a
baseline for a database that already has the complete Kysely schema. Kysely
remains the source of truth for ordered historical and future schema changes,
so production deployments must still run `bun run db:migrate` first.

- **Naming convention**: migrations follow the `NNN_slug.ts` pattern (e.g.
  `001_init.ts`, `002_poll_results.ts`).
- **Historical migrations** (001–013) are immutable and represent the legacy
  SQL schema converted to Kysely.
- **Future migrations** use the same `NNN_slug.ts` convention and may provide an
  optional `down(db)` function for rollback.
- **Legacy compatibility**: existing databases with `schema_migrations` history
  are automatically adopted into Kysely's migration metadata on startup. No
  manual intervention is required.
- **Migration safety**: concurrent startup attempts are serialized with advisory
  locking and Kysely's built-in migration lock.

All future schema changes must be added as Kysely migration modules (`NNN_slug.ts`)
under `apps/api/migrations/` — standalone `.sql` files are no longer accepted.

## Notes

- The .NET API binds to the configured ASP.NET Core URL (port 3000 in Compose).
- The development Blazor UI binds to port 5173; its production nginx image
  listens on port 5056.
- Apply migrations before .NET API startup with `bun run db:migrate`.
- Metrics are available at `http://localhost:3000/api/metrics`.
- OpenAPI JSON is available at `http://localhost:3000/swagger/json`.
- Set `TELEGRAM_BOT_TOKEN` and `users.chat_id` to enable Telegram notifications.
