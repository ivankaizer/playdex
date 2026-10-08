# Administration topology and operations

Playdex consists of two deployable applications: the API (`:3000`) and the
Frontend (`src/Frontend`, a React single-page app served by nginx on `:5056`,
`:5173` in local development). The Frontend contains both the customer UI and
the admin console under `/admin`.

The admin console never touches the database directly. It calls the admin-only
API endpoints under `/api/admin/*` (module `src/Api/Modules/Admin`).

## Authorization

- Admins sign in at `/admin/login` with their normal Playdex account through
  `POST /api/login`. The console keeps the session only when the response has
  `isAdmin: true`; otherwise it clears the tokens and shows an error.
- Every `/api/admin/*` request requires a valid access token **and** re-reads
  `users.is_admin` from the database (`Administrator` policy). A demoted admin
  is refused (`403`) on the next request, without waiting for token expiry.
- Anonymous requests get `401`, authenticated non-admins `403`.

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/admin/dashboard` | Live/today counts, live and recent sessions, failed AI generations |
| GET | `/api/admin/platforms` | Integration totals per platform |
| PUT | `/api/admin/platforms/{key}` | Enable or disable all integrations of a platform |
| GET | `/api/admin/integrations` | All integrations |
| PATCH | `/api/admin/integrations/{id}` | Update an integration's API key and/or account id |
| GET | `/api/admin/sessions` | Paged session search (`query`, `status`, `page`, `from`, `to`) |
| POST | `/api/admin/sessions/transfer` | Move sessions to another integration |
| GET | `/api/admin/games` | Game search (title, capsule URL, raw or encoded id) |
| PATCH | `/api/admin/games/{id}` | Edit title/capsule URL and their locks |
| POST | `/api/admin/games/merge` | Merge one game into another (transactional) |
| GET | `/api/admin/ai-generations` | Paged AI generation history |
| GET | `/api/admin/ai-generations/{id}` | Generation details |
| GET | `/api/admin/ai-generations/{id}/image` | Generation screenshot |
| DELETE | `/api/admin/ai-generations/{id}` | Delete a generation |

## Deployment order

1. Deploy PostgreSQL and the API (migrations run at API startup).
2. Verify API `/health/ready`.
3. Deploy the Frontend image with `PLAYDEX_API_URL` pointing at the API.
4. Sign in at `/admin/login` with an administrator account and check each page.

## Rollback

Redeploy the previous compatible API and Frontend images. The admin endpoints
introduce no database migration, so rollback does not require database rollback.

## Local development

```sh
docker compose up db app ui
```

Then open `http://localhost:5173/admin`. The seeder (`tools/Seeder`) creates the
development administrator `admin@playdex.dev`.
