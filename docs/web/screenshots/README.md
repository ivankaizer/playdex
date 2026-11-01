# Web app screenshots

Reference shots of the web app (`src/Frontend`), its admin console and the playdex.dev landing as of 2026-11-01, taken
before the redesign at commit `528e1bd`. They come from the Vite dev servers talking to the real API in Development,
with demo data from `tools/Seeder` and the Wiremock fakes standing in for Steam, PlayStation and Xbox.

| Folder    | Who                 | Screens                                         |
| --------- | ------------------- | ----------------------------------------------- |
| `public/` | Signed out          | Home, Privacy, admin login, and the landing     |
| `app/`    | `user@playdex.dev`  | Dashboard, Sessions, Recap, a game, Settings, … |
| `admin/`  | `admin@playdex.dev` | The admin console (`/admin`)                    |

Each folder has `desktop/` (1440×900) and `mobile/` (390×844, touch), and each of those a `dark/` and a `light/` theme
with the same file names. A `-full` file is the same screen as a full-page shot; it exists only where the page scrolls,
so some screens have one on mobile and not on desktop. Shots of an open dialog, dropdown or panel, or of a
scrolled-to section, have none.

## Screens

### `public/`

| File                  | Route                               |
| --------------------- | ----------------------------------- |
| `01-home`             | `/` signed out: the sign-in form    |
| `02-privacy`          | `/privacy`                          |
| `03-admin-login`      | `/admin/login`                      |
| `04-landing`          | Landing `/` (`bun run dev:landing`) |
| `05-landing-launcher` | Landing `/launcher`                 |
| `06-landing-privacy`  | Landing `/privacy`                  |

### `app/`

| File                             | Route and state                                                     |
| -------------------------------- | ------------------------------------------------------------------- |
| `01-home`                        | `/` signed in                                                       |
| `02-dashboard`                   | `/dashboard`                                                        |
| `03-sessions`                    | `/sessions`                                                         |
| `04-sessions-edit`               | `/sessions` with the Edit session dialog open on the newest session |
| `05-sessions-live`               | `/sessions` with "Start live session" open                          |
| `06-sessions-create`             | `/sessions/create`, Manual entry                                    |
| `07-sessions-create-ai-batch`    | `/sessions/create`, AI batch                                        |
| `08-recap`                       | `/recap` (All time)                                                 |
| `09-game`                        | `/games/<id>` for Marathon (Steam)                                  |
| `10-settings`                    | `/settings`, top                                                    |
| `11-settings-integrations`       | `/settings` scrolled to Connected services                          |
| `12-settings-integration-manage` | The same with Steam's Manage panel open                             |
| `13-settings-game-connections`   | `/settings#game-connections`, where `/mapping` redirects            |
| `14-settings-your-data`          | `/settings#your-data`                                               |
| `15-game-search`                 | `/dashboard` with "el" typed into the header's game search          |

### `admin/`

| File                | Route                                                          |
| ------------------- | -------------------------------------------------------------- |
| `01-dashboard`      | `/admin`                                                       |
| `02-sessions`       | `/admin/sessions`                                              |
| `03-games`          | `/admin/games`                                                 |
| `04-integrations`   | `/admin/integrations`                                          |
| `05-ai-generations` | `/admin/ai-generations`: empty, the seed has no AI generations |

## What the data is

- Everything on screen is seed data: the three demo accounts, eight games (Marathon and ELDEN RING each on Steam and
  PSN, DSX with a broken poster URL on purpose), random sessions on about half the days of this year and last,
  made-up integration ids (`steam-account-id`, …) and Telegram chat id `12345`. Posters load from the Steam and
  PlayStation CDNs.
- The seeder picks random days, so totals and charts differ every time it runs.
- They were taken on 1 November, so "this month" panels (the dashboard's November highlights) hold a single session.
- AI batch says AI generation is unavailable because no AI provider is configured locally. There is no
  `/admin/ai-generations/<id>` shot because the seed creates no generations.

## Retaking them

```sh
docker compose up -d db wiremock apprise
docker compose exec db createdb -U playdex playdex_screenshots   # a scratch database: the seeder truncates every table

export DATABASE_URL=postgresql://playdex:playdex@localhost:5432/playdex_screenshots
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:3000 \
  Credentials__EncryptionKey=dev-only-credentials-key-do-not-use-in-production \
  CORS_ORIGINS=http://localhost:5173,http://localhost:5174 \
  dotnet run --project src/Api                 # migrates the database, then serves the API
dotnet run --project tools/Seeder              # in another shell, with the same DATABASE_URL

cd src/Frontend && bun install
VITE_API_BASE_URL=http://localhost:3000/api/ bun run dev           # http://localhost:5173
VITE_API_BASE_URL=http://localhost:3000/api/ bun run dev:landing   # http://localhost:5174
```

The shots were taken with Playwright (`playwright-core` 1.48.2 driving Brave headless, run with Node), locale `en-US`,
time zone Europe/Warsaw and a device scale factor of 1:

- Sign in once per account through the real form (`/` for the user, `/admin/login` for the admin) and reuse the
  browser's storage state; the tokens live in localStorage.
- Set the theme before the app loads with `localStorage["playdex.theme"] = "dark" | "light"`, and the matching
  `colorScheme`.
- After each navigation wait for `networkidle`, for no `[aria-busy="true"]` or progress bar and for every image to load
  (lazy images switched to eager), then 800 ms more. Screenshots use `animations: "disabled"`.
- For `-full`, take a `fullPage` shot when the document scrolls; if only an inner container scrolls, grow the viewport
  by that container's `scrollHeight - clientHeight` instead.

The PNGs were then optimised losslessly with oxipng (level 4); the pixels are unchanged.
