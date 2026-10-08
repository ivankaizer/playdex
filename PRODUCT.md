# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

The responsive web app (React, `src/Frontend`) is the design surface. A Windows desktop wrapper (`src/Wrapper`) captures sessions but is not a design surface for the web UI.

## Users

People who play games across several platforms (PC storefronts, PlayStation, Xbox, Nintendo Switch, emulators) and want to know where their play time goes.

- **Today:** invite-only. Accounts are created by an administrator; there is no public sign-up.
- **Intended:** grow into a public product with self-service sign-up. Treat invite-only as a current state, not a design assumption.
- **Administrators** run the service from a separate admin console (`/admin`): users, integrations, sessions, AI generations, games.

## Product Purpose

Playdex records play sessions from every platform a person uses and turns them into one history with insights: today, this week, this month, yearly recaps, per-game stats.

Success means a user can see all of their stats, across all of their platforms, in one place, and trust that the history is complete and accurate.

## Positioning

**All stats and all platforms in one place.** Platform-native stats (Steam, PSN wrap-ups, Xbox) each see only their own ecosystem, and catalog/backlog sites depend on manual entry. Playdex merges automatic capture from several ecosystems with manual and AI-assisted logging for platforms that don't sync, into a single timeline and a single set of statistics.

## Operating Context

How sessions enter Playdex (capture methods):

- **Polling:** connected Steam, PlayStation Network and Xbox (via OpenXBL) accounts are checked on an interval; presence becomes sessions.
- **API capture:** the Windows desktop wrapper launches and watches games (including non-Steam PC games and emulators), pings the API while a game runs, adds non-Steam shortcuts and backs up saves with Ludusavi.
- **Manual:** a live timer, or logging a past session (game, platform, date, duration).
- **AI batch:** the user pastes text or a screenshot; an AI provider drafts sessions that the user reviews, edits and accepts.

What users do with the history: dashboard (today, this week, overview), sessions log with filters and edits, recaps (rhythm by hour/weekday, streaks, platform mix, full library), per-game pages with ratings, game connections that merge duplicate titles, and Telegram notifications.

## Capabilities and Constraints

- Terminology (see `CONTEXT.md`): **Gaming Provider** (Steam, PSN), **Notification Channel**, **Transport** (Telegram), **Destination**; capture methods `polling` / `api` / `manual`.
- Game metadata and covers come from Steam and IGDB; covers are stored per game and may be missing (UI must fall back gracefully).
- Statistics are computed over UTC day ranges.
- Short sessions can be filtered out per user (minimum minutes setting).
- Self-service data export (`GET /me/export`) and account deletion (`DELETE /me`, password-confirmed) exist.
- Retention: AI batch inputs are kept 30 days; raw provider sync responses are kept 30 days.
- Known open item: connected-service credentials are stored without additional encryption at rest.
- Undecided: public sign-up flow, pricing or business model.

## Brand Commitments

- **Name:** Playdex.
- **Visual identity is binding:** the v3 "cozy" identity from the redesign handoff (`design_handoff_playdex_redesign`): frosted cards on a pastel mint/cyan/sand background, pill controls, Space Grotesk, spaced-caps kickers, a dark mode. Refine within it; do not replace it.
- **Accessibility overrides the palette where they conflict** (see Accessibility): adjust handoff values to meet WCAG AA rather than ship failing contrast.

## Evidence on Hand

- Real product surfaces: the deployed app and per-PR previews (`https://<branch>.playdex.deployment.lat`) with seeded demo data (`tools/Seeder`).
- Real game artwork via Steam and IGDB covers.
- **Absent, do not fabricate:** user counts, testimonials, reviews, press, benchmarks, pricing. The landing page's recap preview and top-5 games use clearly labelled sample data.

## Product Principles

1. **Complete before clever.** A missing or wrong session undermines every stat; capture coverage and accuracy come before new visualisations.
2. **One history, every platform.** Never let a platform feel second-class; non-syncing platforms get first-class manual and AI paths.
3. **Effortless where possible, honest where not.** Automate capture, and show plainly what was synced, logged or estimated.
4. **Privacy is part of the product.** Collect only what tracking needs, explain it plainly, and keep export and deletion self-service.
5. **Build for the public product.** Today's invite-only setup should not leak into flows, copy or assumptions that would block public sign-up.

## Accessibility & Inclusion

- **WCAG 2.2 AA is required** across the web app, in both light and dark themes, including text contrast (4.5:1 body, 3:1 large text and UI components), keyboard operability with visible focus, and labelled controls.
- Respect `prefers-reduced-motion`.
- Charts must expose their values without hover (labels, accessible names) as well as on hover.
