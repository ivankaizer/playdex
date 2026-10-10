# Preview environments

Playdex uses [preview-kit](https://github.com/ivankaizer/preview-kit), the shared setup for all
repos. After the tests pass on a pull request, CI deploys a full copy of Playdex at

```
https://<branch-slug>.playdex.deployment.lat
```

and records a demo. Each deploy posts a new PR comment (`comment: new` in
[`test.yaml`](../.github/workflows/test.yaml)) with the URL, the full tour and, from the second
commit on, a video of the steps that changed visually since the previous commit. The videos live on
the `demos/pr-<number>` branch, one folder per commit, so earlier comments keep working. The preview
and its demos are removed when the PR closes. The kit's README documents the CLI (`preview deploy`, `preview demo`,
`preview status`, …), the config formats and how it works.

## Playdex specifics

- **Stack:** [`docker-compose.preview.yml`](../docker-compose.preview.yml) contains Postgres,
  Wiremock (which mocks Steam, PSN and Xbox), Apprise, the API, the production UI image and a
  one-shot `seeder`. It publishes no host ports and sets memory limits.
- **Routing** ([`.preview/config.json`](../.preview/config.json)): `/api/*` goes to the API and
  everything else to the UI, on the same origin.
- **Seed data:** the `seeder` service runs `tools/Seeder` once per preview, after the API has
  applied migrations. Later deploys skip it, because the seeder truncates every table and would wipe
  data changed while testing. To reseed, run `preview destroy`, then `preview deploy`. Seeded
  logins, all with password `password`:
  - `user@playdex.dev`
  - `second_user@playdex.dev`
  - `admin@playdex.dev`, an administrator (`/admin`)
- **Demo tour** ([`.preview/tour.json`](../.preview/tour.json)) has two parts:
  - the user part: landing → login → dashboard → sessions → recap → settings
  - the admin part: login → dashboard → integrations → sessions → AI generations → games

  When a page is added, renamed or moved, update the tour in the same PR.

## Troubleshooting

- **Build or startup failure:** see the Dokploy dashboard (<https://dash.deployment.lat> →
  playdex → previews → `pr-<number>` → Deployments).
- **Seed failed or missing data:** check the `seeder` service logs there. A failed seed writes no
  marker, so the next deploy retries it.
- **Route or port changes:** if the UI or API port or the `/api` routing changes, update
  `.preview/config.json`, then `preview destroy` so the preview is recreated with the new routes.
