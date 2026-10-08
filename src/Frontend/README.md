# Playdex Frontend

React + TypeScript single-page app (Vite, React Router, Tailwind CSS) that serves
both the user UI and the admin console (`/admin`). It talks to the API only;
the admin console uses the admin-only `/api/admin/*` endpoints.

## Local development

```bash
bun install --frozen-lockfile
VITE_API_BASE_URL=https://app.playdex.orb.local/api/ bun run dev
```

The API base URL is resolved at startup from `/appsettings.json` (`ApiBaseUrl`).
When that value is empty or still the `__PLAYDEX_API_URL__` placeholder, the app
falls back to `VITE_API_BASE_URL`, then to `<origin>/api/`.

## Verification

```bash
bun run typecheck
bun run test
bun run build
docker build -f src/Frontend/Dockerfile .   # from the repository root
```

In the container, `PLAYDEX_API_URL` replaces the placeholder in
`appsettings.json` at startup, so one image can target any API.
