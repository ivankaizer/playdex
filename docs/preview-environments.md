# Preview environments

Every pull request gets its own copy of Playdex on the staging server:

```
https://<branch-slug>.playdex.deployment.lat
```

`<branch-slug>` is the branch name lowercased with every run of non-alphanumerics replaced by `-`,
cut to 40 characters (`feat/react-frontend` → `feat-react-frontend.playdex.deployment.lat`).
UI and API share that origin: Traefik sends `/api/*` to the API and everything else to the UI.

## How it works

- **Server:** a Dokploy instance at <https://dash.deployment.lat> (project `playdex`,
  environment `previews`). Each PR is one compose app named `pr-<number>`.
- **Stack:** [`docker-compose.preview.yml`](../docker-compose.preview.yml) — Postgres, Wiremock,
  Apprise, API and the production UI image, with no published ports and memory limits. The database
  is fresh per PR and external gaming providers are mocked by Wiremock.
- **Source:** Dokploy clones the PR branch over SSH with a read-only deploy key and builds the images
  on the server.
- **Script:** [`deploy/preview.sh`](../deploy/preview.sh) does everything through the Dokploy API:
  `deploy` creates the app and its domains on first run, redeploys the latest commit of the branch,
  waits for it and prints the URL; `destroy` removes the app and its volumes.

### Automatic (CI)

- `.github/workflows/test.yaml` → job **Deploy preview** runs after the tests pass on every PR
  push, then posts or updates a `🚀 Preview:` comment on the PR with the URL.
- `.github/workflows/preview-cleanup.yaml` removes the preview when the PR is merged or closed.
- CI reads `vars.DOKPLOY_URL` and `secrets.DOKPLOY_API_KEY` from the repository settings.

## Deploying manually (agents)

Use this to put a branch online without waiting for CI, or to redeploy after a failure.

1. **Credentials** live in `~/.env` on the developer machine, never in the repo, commits, PR text or
   logs:

   ```sh
   export DOKPLOY_URL=https://dash.deployment.lat
   export DOKPLOY_API_KEY=...   # Dokploy → Settings → Profile → API/CLI
   ```

   If `~/.env` is missing those variables, stop and ask the user for them; do not create an API key
   yourself.

2. **Prerequisites:**
   - The branch is pushed and has an open PR. The server builds from GitHub, not from the local
     working tree, so unpushed commits are not deployed.
   - `docker-compose.preview.yml` and `deploy/preview.sh` exist on that branch.
   - `gh`, `curl` and `jq` are installed and `gh` is authenticated.

3. **Deploy** from the repository root:

   ```sh
   source ~/.env && deploy/preview.sh deploy
   ```

   The PR number and branch come from `gh pr view` for the checked-out branch. To target another PR,
   set them explicitly: `PR_NUMBER=29 BRANCH=feat/react-frontend deploy/preview.sh deploy`.

   A first deploy builds three images on a 2-CPU server and takes roughly 5–10 minutes; later
   deploys reuse the Docker cache. The last line printed is the URL.

4. **Verify** before reporting success: open the URL (or `curl -fsS <url>/`) and check
   `<url>/api/` returns JSON from the API (`{"data":"Ok",...}`), not the UI's HTML.

5. **Remove** a preview that is no longer needed: `source ~/.env && deploy/preview.sh destroy`.

## Troubleshooting

- **Build or startup failure:** the script exits non-zero and names the deployment. Logs are in the
  dashboard under *playdex → previews → pr-<number> → Deployments*, or over SSH on
  `root@157.180.30.193` in `/etc/dokploy/logs/<appName>/`.
- **Out of memory:** the server has 4 GB RAM plus 4 GB swap, which is enough for 2–3 previews at
  once. Destroy previews of closed or stale PRs before deploying more.
- **`HTTP 401` from the API:** `DOKPLOY_API_KEY` is wrong or revoked; ask the user.
- **Changing the stack** (new service, env var, port): edit `docker-compose.preview.yml`. If the UI
  or API port or the `/api` routing changes, also update the domain routes in `create_compose` in
  `deploy/preview.sh` and destroy the existing preview so it is recreated with the new routes.
