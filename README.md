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
bun install
```

4. Start the server:

```bash
bun run dev
```

Open `http://localhost:3000/` in your browser.

## Useful commands

```bash
bun run dev
bun run format
bun run format:check
bun run telegram:notify-image --type started --title "Like a Dragon Gaiden: The Man Who Erased His Name" --platform "Steam" \
  --image "https://shared.steamstatic.com/store_item_assets/steam/apps/2375550/library_600x900_2x.jpg" \
  --chat-id 77434259
```

## Notes

- The server binds to port 3000 by default.
- Migrations run on startup.
- Metrics are available at `http://localhost:3000/metrics`.
- OpenAPI docs are available at `http://localhost:3000/swagger` (JSON at `/swagger/json`).
- Set `TELEGRAM_BOT_TOKEN` and `users.chat_id` to enable Telegram notifications.
