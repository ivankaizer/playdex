# Notifications

Playdex sends outbound notifications through a separate, internal Apprise API
container. Users never access Apprise and never handle the Telegram bot token.

## Local setup

1. Copy `.env.example` to `.env`.
2. Create one Telegram bot with BotFather and set `TELEGRAM_BOT_TOKEN` in `.env`.
3. Start the stack with `docker compose up`.
4. Each user opens Playdex Settings and enters only their Telegram chat ID.

When settings are saved, Playdex calls Apprise's internal `/add/{key}` endpoint
with `tgram://BOT_TOKEN/CHAT_ID`. The configuration is also created lazily if it
is missing when a notification is sent, which makes existing `chat_id` records
self-migrating. Users cannot reach Apprise because port 8000 is exposed only on
the Docker network and the Apprise web UI is disabled.

Configuration persists in the `apprise_config` Docker volume. Uploaded session
cards use the `apprise_attach` volume.

## Application configuration

The API accepts these settings:

- `Apprise__BaseUrl`: Apprise API base URL. Notifications are disabled when it
  is absent.
- `Apprise__TelegramBotToken`: administrator-owned Telegram bot token used to
  provision chat-specific Apprise configurations automatically.
- `Apprise__UseMock`: logs notifications without making HTTP calls. Intended
  for development and automated tests.

Docker Compose maps `TELEGRAM_BOT_TOKEN` from `.env` to the application setting.

## Test endpoint

An authenticated user can verify delivery using:

```http
POST /api/settings/notifications/test
Authorization: Bearer <access-token>
```

The endpoint uses the saved Telegram chat ID, provisions its Apprise
configuration if necessary, sends a test message, and returns `{ "sent": true }`.
It returns a validation error when the user has not configured a chat ID.
