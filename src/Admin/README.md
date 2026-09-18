# Playdex Admin

Playdex Admin is an independently deployable Interactive Server Blazor application. It uses the free MIT-licensed `Radzen.Blazor` package and accesses PostgreSQL only through server-side services and the shared persistence project.

Set `DATABASE_URL` to a PostgreSQL connection URL, then run:

```sh
dotnet restore src/Admin/Admin.csproj
dotnet run --project src/Admin/Admin.csproj
```

No Radzen Studio license or license key is required. The Admin process never runs database migrations; deploy the API first so its migration runner establishes a compatible schema. Configure `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, `Admin__AllowedOrigin`, and the cookie policy at the ingress boundary for production.

GitHub Actions builds the Admin Docker image on pull requests and publishes commit SHA and `latest` tags from `main` using `.github/workflows/build-admin.yaml`. Create the `${REPO_NAME}-admin` repository in AWS ECR (`eu-north-1`) and allow the existing AWS credentials to push to it. Image publishing does not update the running Admin service; configure your hosting platform to deploy that image with the settings above.

The Dashboard monitors all accounts without mutation controls. Its global counters link to scoped session/history lists; sessions today use the browser timezone. Live sessions include polling, API and manual sources, support combined user/game/account search and platform filters, and show the latest poll per integration where applicable. The page refreshes every 30 seconds without overlapping reads, cancels work on navigation and rechecks the current database administrator role on each read. Temporary refresh failures preserve the last snapshot; revoked access clears it. Recent activity lists are limited to ten entries each.

The Integrations page displays full integration API keys and a visible key editor. Every integration-list read revalidates the current administrator role against the database. Editing a key still requires selecting “Replace or clear API key”; editing other fields leaves the stored key untouched. Keys are excluded from ordinary application logs.

Open **AI generations** (`/ai-generations`) to search future submissions by user email or source text, filter outcomes, and inspect original text/screenshots, the initial resolved result, and bounded raw provider responses (including automatic retries and invalid output). Successful request replays do not create duplicate history. Draft edits and acceptance do not alter the saved result snapshot. Screenshots require a current administrator role and are never served as public static files. Each run can be deleted after confirmation.

The API expires history hourly using `AiDrafts__HistoryRetentionDays` (default `30`; `0` retains until manually deleted). The composer discloses this configured retention before submission. Deleting a user also deletes their history. Old submissions cannot be reconstructed. Ordinary logs continue to exclude evidence, response bodies, and provider credentials. Deploy the API migration before this Admin version.
