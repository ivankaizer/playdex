# Playdex

All your play time, from every platform, in one history.

Playdex records play sessions from Steam, PlayStation, Xbox and your PC, lets you log the rest by hand or from a
screenshot, and turns it all into one timeline with stats: today, this week, yearly recaps, streaks, per-game pages.
Run it on your own server, or use the hosted app at [playdex.dev](https://playdex.dev).

## How sessions get in

| Capture | How it works |
|---|---|
| Polling | Connected Steam, PlayStation Network and Xbox (via [OpenXBL](https://xbl.io)) accounts are checked on an interval; presence becomes sessions. |
| Windows app | Launches and watches PC games, emulators included, adds them to Steam, backs up saves with Ludusavi, and doubles as a full-screen launcher. |
| Manual | A live timer, or a past session with game, platform, date and duration. |
| AI drafts | Paste text or a screenshot; an AI provider (OpenAI or OpenRouter) drafts sessions you review before saving. |

Notifications go to Telegram. Export and account deletion are self-service.

## Self-host

You need a Linux server with Docker, a domain pointing at it and ports 80 and 443 open.

```sh
mkdir playdex && cd playdex
base=https://raw.githubusercontent.com/ivankaizer/playdex/main/selfhost
curl -fsSLO $base/docker-compose.yml
curl -fsSLO $base/Caddyfile
curl -fsSL $base/.env.example -o .env   # set PLAYDEX_DOMAIN, POSTGRES_PASSWORD, JWT_SECRET, CREDENTIALS_KEY
docker compose up -d
```

The full guide, configuration reference, platform setup, updates and backups are at
[docs.playdex.dev](https://docs.playdex.dev). The Windows app installs from
[download.playdex.dev](https://download.playdex.dev/windows/Playdex-Setup.exe) and connects to any Playdex server.

## Repository

| Path | What |
|---|---|
| `src/Api` | ASP.NET Core API (.NET 10): one module per feature under `Modules/`, migrations run on start |
| `src/Persistence` | Postgres schema, data access and migrations |
| `src/Frontend` | Web app and admin console (React, Vite, Tailwind); the playdex.dev landing in `landing/` |
| `src/Desktop`, `src/Launcher`, `src/Playdex.*` | Windows app, full-screen launcher, shared core, game runner and installer |
| `src/Docs` | docs.playdex.dev (Astro Starlight) |
| `selfhost` | Compose file and Caddyfile for self-hosting |
| `tools` | Demo data seeder, Wiremock fakes of the platform APIs, Windows packaging |
| `test` | API tests (some need Postgres) and Windows core tests |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the development setup, tests and conventions. Report security issues
privately as described in [SECURITY.md](SECURITY.md).

## License

Playdex is free software under the [GNU Affero General Public License v3.0](LICENSE). If you run a modified
version as a service for others, you must offer them its source.

Game metadata and covers come from [IGDB](https://www.igdb.com) and Steam. Playdex is not affiliated with or
endorsed by Valve, Sony Interactive Entertainment, Microsoft, Nintendo or Twitch; their names and trademarks
belong to their owners.
