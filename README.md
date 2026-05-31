# Playdex

Playtime tracker for PSN and Steam accounts, built with Bun + Elysia.

## Requirements

- Bun
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

3. Install dependencies:

```bash
cd apps/api
bun install
```

4. Start the server:

```bash
cd apps/api
bun run dev
```

Open `http://localhost:3000/` in your browser.

5. (Optional) Run the web app:

```bash
cd apps/web
bun install
bun run dev
```

## Useful commands

```bash
cd apps/api
bun run dev
bun run format
bun run format:check
bun run check
bun run db:migrate
bun run db:seed
bun run telegram:notify-image --type started --title "Like a Dragon Gaiden: The Man Who Erased His Name" --platform "Steam" \
  --image "https://shared.steamstatic.com/store_item_assets/steam/apps/2375550/library_600x900_2x.jpg" \
  --chat-id 77434259
```

## Database migrations

Migrations use [Kysely](https://www.kysely.dev/) with TypeScript modules under
`apps/api/migrations/`. The `bun run db:migrate` command and application startup
both use the same migration runner.

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

- The server binds to port 3000 by default.
- Migrations run on startup.
- Metrics are available at `http://localhost:3000/metrics`.
- OpenAPI docs are available at `http://localhost:3000/swagger` (JSON at `/swagger/json`).
- Set `TELEGRAM_BOT_TOKEN` and `users.chat_id` to enable Telegram notifications.
