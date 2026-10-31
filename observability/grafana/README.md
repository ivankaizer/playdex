# Grafana

The Playdex dashboard and alert rules, kept here and pushed to a Grafana stack with `push.sh`. They read what the API
exports over OTLP when `OTLP_ENDPOINT` is set (see `src/Docs/src/content/docs/reference/configuration.mdx`).

```sh
GRAFANA_URL=https://<stack>.grafana.net \
GRAFANA_TOKEN=<service account token, Editor role> \
TELEGRAM_BOT_TOKEN=<bot token> TELEGRAM_CHAT_ID=<your chat id> \
  observability/grafana/push.sh
```

The Telegram variables create the `playdex-admin` contact point that every alert goes to; leave them out once it
exists. Edit a dashboard in Grafana, export it (Share → Export, "Export for sharing externally" off) over
`dashboards/playdex.json`, and commit it: the next push would overwrite the edit otherwise.

## What the API exports

| Metric (as Prometheus names it) | Labels | What it says |
|---|---|---|
| `playdex_poll_cycles_total` | `outcome`: completed, failed, skipped | Poll cycles. No completed ones means polling stopped. |
| `playdex_poll_cycle_duration_seconds` | | How long a completed cycle took. |
| `playdex_polls_total` | `integration_id`, `user_id`, `platform`, `outcome`: success, failure, auth_failure | Every poll of every integration. |
| `playdex_integration_state` | `integration_id`, `user_id`, `platform`, `state`: healthy, failing, paused, disabled, never_polled | 1 for the state each integration is in, 0 for the others. |
| `playdex_integration_since_success_seconds` | `integration_id`, `user_id`, `platform` | Time since the integration last polled successfully. |
| `playdex_integration_failing_for_seconds` | `integration_id`, `user_id`, `platform` | How long an active integration has been failing; 0 when it works. |
| `playdex_sessions_total` | `integration_id`, `event`: started, ended | Sessions an integration detected. |
| `playdex_notifications_total` | `kind`, `outcome`: sent, skipped, retrying, failed; `reason` when skipped; `user_id` | What became of every notification. |

Logs carry `integration_id`, `user_id` and `platform` on every line of a poll. Ids are Playdex's database ids: look
them up in the admin console. No emails, account ids or game titles are exported as labels.

## Alerts

| Alert | Fires when |
|---|---|
| Polling stopped | No completed poll cycle in 10 minutes, for 5 minutes (also when no data arrives at all). |
| Integration failing | An integration has failed every poll for 30 minutes. |
| Integration paused | Playdex paused an integration whose credential was rejected (the user is told too). |
| Notifications given up | A notification failed every retry: its user never got it. |
| Notifications failing | More than 5 deliveries failed in 15 minutes: Apprise or Telegram is down. |

Sentry, when `SENTRY_DSN` is set, gets errors and a `playdex-polling` cron check-in after every cycle; it raises an
issue on its own when the check-ins stop.
