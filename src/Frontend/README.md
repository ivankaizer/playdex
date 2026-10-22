# Playdex Frontend

React + TypeScript single-page app (Vite, React Router, Tailwind CSS) that serves
both the user UI and the admin console (`/admin`). It talks to the API only;
the admin console uses the admin-only `/api/admin/*` endpoints. The app runs at
https://dash.playdex.dev, where `/` is the sign-in page.

## Landing

The public landing (https://playdex.dev) is a separate static build in
`landing/` that reuses the app's components through the `@` alias. It never
calls the API; its sign-in links go to the app (`VITE_DASH_URL`, default
`https://dash.playdex.dev`). `.github/workflows/landing.yml` deploys it to the
Cloudflare Pages project `playdex-landing`: `main` to production, pull requests
to a `*.pages.dev` preview.

```bash
bun run dev:landing     # http://localhost:5174
bun run build:landing   # landing/dist
```

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
bun run build:landing
bun run check:csp     # after the builds: CSP hashes match the inline script and style
docker build -f src/Frontend/Dockerfile .   # from the repository root
```

In the container, `PLAYDEX_API_URL` replaces the placeholder in
`appsettings.json` at startup, so one image can target any API. When that URL
is on another origin, the entrypoint also adds it to the CSP's `connect-src`.

## Security headers

nginx sends the security headers and the Content-Security-Policy from
`security-headers.conf`, included in every location of `nginx.conf`; the
landing sends the same set from `landing/public/_headers`. Both CSPs allow the
theme script and boot-loader style inlined in `index.html` and
`landing/index.html` by sha256 hash, so an edit to either block changes its
hash: `bun run check:csp` then fails and prints the hash the policy needs. CI,
the landing deploy and the image build run the check.
