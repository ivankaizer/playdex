# Sentry observability

Playdex uses Sentry Cloud as its only persisted observability backend. The API still emits single-line structured JSON to stdout so startup failures remain visible with `docker compose logs app`.

## What is captured

- Unexpected ASP.NET and browser (React) exceptions are grouped into Sentry issues.
- `ILogger` records are sent to Sentry Logs; information and warning records also provide breadcrumbs on issues.
- Browser API operations propagate into ASP.NET Core OpenTelemetry spans and Npgsql spans. A game load should read as `ui.api_request` → `http.server` → `db` in Sentry.
- Expected application problems and invalid JSON are warnings, not issues.

Request bodies and default PII are disabled. Transaction names remove query strings and common identifier path segments to avoid high-cardinality names.

## Configuration

Copy `.env.example` to the ignored `.env` file and set:

| Variable | Required | Purpose |
| --- | --- | --- |
| `SENTRY_DSN` | Yes for remote telemetry | API project DSN; also the browser fallback DSN |
| `SENTRY_WEB_DSN` | No | Separate browser project DSN |
| `SENTRY_ENVIRONMENT` | No | Deployment name such as `production` or `staging` |
| `SENTRY_RELEASE` | No | Immutable release, ideally `playdex@<git-sha>` |
| `SENTRY_TRACES_SAMPLE_RATE` | No | Number from `0.0` to `1.0`; defaults to `0.2` |

The API exposes only browser-safe values at `GET /api/observability/sentry`. The React frontend loads them before enabling its Sentry SDK. If the API is unavailable during this short bootstrap, the UI still starts with browser telemetry disabled.

For direct frontend development, `src/Frontend/public/appsettings.json` may contain a local `Sentry` section (do not commit a DSN there). Never put a Sentry auth token there; browser code needs only the public DSN.

## Sentry setup

Create an ASP.NET Core project in Sentry. A separate browser (React) project is recommended because it gives cleaner ownership, alert rules, and issue streams, but a single project works: omit `SENTRY_WEB_DSN` to reuse the API DSN.

Use these initial alerts:

1. A new issue or regression occurs in `production`.
2. An error affects more than one user or occurs at least five times in ten minutes.
3. A transaction's p95 duration breaches the chosen API service-level threshold.

Keep the default production trace rate at `0.2` until real traffic and quota usage are known. Increase it temporarily when diagnosing a specific path.

## Verification

1. Start the stack with valid Sentry settings.
2. Open a game and locate its `ui.api_request` transaction in Trace Explorer.
3. Confirm the trace continues into the API and contains an Npgsql database span.
4. Trigger a controlled unexpected exception in a non-production environment and confirm it creates one grouped issue with `component=api` or `component=web`.
5. Send an invalid request and confirm it appears as a warning/log but not as a new issue.

If no events arrive, first inspect `docker compose logs app` and the browser console. A missing or blank DSN intentionally disables Sentry without failing startup.
