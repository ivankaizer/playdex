# Playdex

Playtime tracker for PSN and Steam accounts, powered by Bun + Elysia.

## Local setup (Postgres)

1. Start Postgres via Docker:

```bash
docker compose up -d db
```

2. Create a `.env` file at the repo root:

```bash
DATABASE_URL=postgresql://playdex:playdex@localhost:5432/playdex
POLL_INTERVAL_SECONDS=60
```

3. Install dependencies and start the server:

```bash
bun install
bun run dev
```

Open `http://localhost:3000/` in your browser.

## Notes

- Migrations run on startup.
- Metrics are available at `http://localhost:3000/metrics`.
