# Security

## Reporting a vulnerability

Please don't open a public issue. Report it privately on GitHub:
[Security → Report a vulnerability](https://github.com/ivankaizer/playdex/security/advisories/new).

Include what's affected (API, web app, Windows app, self-hosting setup), how to reproduce it and the impact you see.
You'll get an answer within a week. Once a fix is released, the advisory is published with credit to you unless
you'd rather stay anonymous.

## Supported versions

Fixes go into `main`, which builds the `latest` images and the next Windows release. Self-hosted servers get them by
pulling the latest images (see [updates](https://docs.playdex.dev/self-host/updates-backups/)); the Windows app
updates itself.

## Scope

In scope: this repository's code, the images built from it, the Windows app and installer, and the self-hosting
files in `selfhost/`.

Out of scope: the third-party services Playdex talks to (Steam, PlayStation Network, OpenXBL, IGDB, Telegram, AI
providers), and findings that need an already compromised server or admin account.
