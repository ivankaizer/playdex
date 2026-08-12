# Game, integration, and play-session architecture

Status: target architecture and migration outline. Each migration stage is implemented as one independently deployable MR. Stage details will be expanded in this document before implementation.

## Goal

Separate the canonical game, its external identities, the selected gaming platform, the integration that delivered an event, and the method used to capture a play session.

## Domain model

- **Game** is a platform-independent game known to Playdex.
- **Game external identity** identifies a Game in a system outside Playdex. Provider identities (`steam`, `gog`, `psn`, `xbox`) may be observed during session capture; the catalog identity `igdb` is used for reconciliation and metadata enrichment.
- **Platform** is the gaming service, store, or console ecosystem selected for an event: for example `steam`, `gog`, `ps5`, or `xbox`.
- **Integration** is a configured user connection used for polling or API authentication. Polling integrations are bound to a provider-level Platform; API integrations are not.
- **Capture method** is how a session entered Playdex: `polling`, `api`, or `manual`.
- **Play session** records that a user played a Game on a Platform. Integration provenance is optional.
- **Poll result** preserves the technical evidence returned by an integration.

## Target relationships

```text
Integration ───> PlaySession ───> Game
                      │             ▲
Platform ─────────────┘             │
GameExternalIdentity ───────────────┘

Integration ───> PollResult ───> GameExternalIdentity
     │
     └────────> Platform (nullable; required for polling)
```

## Target persistence model

```text
games
  id
  title
  capsule_url
  title_locked
  capsule_url_locked
  created_at
  updated_at

game_external_identities
  id
  game_id
  type                       steam | gog | psn | xbox | igdb
  external_id
  title
  created_at
  updated_at
  UNIQUE(type, external_id)

platforms
  id
  key                        steam | gog | psn | ps5 | xbox | ... | unknown
  name

integrations
  id
  user_id
  type                       steam | psn | xbox | api
  platform_id                nullable; required for polling, null for api
  api_key
  account_id
  api_key_locked
  active
  created_at
  updated_at

play_sessions
  id
  user_id
  game_id
  platform_id                captured platform at the granularity known to the source
  capture_method             polling | api | manual
  integration_id             nullable
  started_at
  ended_at
  created_at
  updated_at

poll_results
  id
  integration_id
  game_external_identity_id  nullable
  polled_at
  raw_payload
  created_at
```

## Invariants

- `PlaySession.user_id`, `game_id`, `platform_id`, and `capture_method` are required.
- Manual sessions have no Integration.
- Polling and API sessions retain the Integration that owns their lifecycle.
- Provider identities observed during polling are recorded in PollResult rather than PlaySession.
- `PlaySession.platform_id` is the authoritative captured Platform for presentation and statistics.
- PollResult does not store Platform directly; the Platform of a polling result is available through its Integration.
- A polling Integration requires a provider-level Platform such as `steam`, `psn`, or `xbox`; it must not claim a device generation that the provider does not report.
- Polling copies `Integration.platform_id` to the resulting `PlaySession.platform_id`; therefore a PSN observation produces `psn`, not an inferred `ps4` or `ps5`.
- An API Integration has no fixed Platform. API events and manual entries select Platform explicitly and may use a more specific value such as `ps5`.
- One API key may send sequential events for different Platforms.
- One Integration owns at most one active session at a time; for an API Integration, changing the event-selected Platform ends or switches that session through the normal lifecycle.
- Integrations are disabled through `active = false` so historical provenance remains valid.
- A Game is never matched by title alone.
- Exact `(type, external_id)` and exact shared catalog identities may resolve automatically.
- Non-exact matches are either resolved automatically by an approved mechanism or merged through the admin interface.
- IGDB is an identity and metadata source, not the primary key of Game.
- Game does not duplicate external identifiers such as `igdb_id`; all external identifiers live in `game_external_identities`.
- IGDB metadata is materialized into Game fields and respects the existing metadata locks.
- Playnite is an API client, not an integration type or capture method.
- There is no import capture method.

## Main flows

### Polling

```text
Integration(platform_id) -> provider observation -> GameResolver
            -> GameExternalIdentity -> Game
            -> Integration.platform_id
            -> PlaySession(capture_method = polling)
            -> PollResult(integration + observed identity)
```

### API

```text
API client -> authenticated API Integration
           -> event-selected Platform -> GameResolver
           -> PlaySession(capture_method = api)
```

### Manual

```text
User selects or creates Game + Platform
     -> PlaySession(capture_method = manual, integration_id = null)
```

## Game resolution

1. Resolve an exact `(type, external_id)`.
2. Resolve an exact shared catalog identity, such as IGDB, when available.
3. Otherwise create a new Game and its external identity.
4. Resolve safe automatic matches or let an administrator merge duplicates through one transactional `MergeGames` operation.

## Migration principles

- One stage equals one MR and one OpenSpec change.
- Add new schema before removing old schema.
- Backfill before switching reads.
- Use dual-write while old and new models coexist.
- Validate session counts and duration totals after every data migration.
- Remove legacy columns only after all readers and writers use the target model.

## Ordered migration stages

### Stage 1: Introduce game external identities

Add and backfill `game_external_identities` while retaining the existing Game identity columns.

### Stage 2: Resolve games by external identity

Introduce a shared `GameResolver` and dual-write external identities from polling and API ingestion.

### Stage 3: Define integration-to-platform ownership

Create `integrations`, add their nullable `platform_id`, bind polling connections to a provider-level Platform, and leave API connections unbound so each API event selects its Platform.

### Stage 4: Add the target play-session fields

Add and backfill `user_id`, `platform_id`, `capture_method`, and `integration_id` directly on `play_sessions`.

### Stage 5: Move polling to the target session model

Use Integration for polling lifecycle correlation, keep the observed external identity on PollResult, and copy `Integration.platform_id` to PlaySession without inferring device generation.

### Stage 6: Generalize Playnite into API capture

Introduce a generic API session contract whose ping/event selects Platform per request, and retain Playnite endpoints only as temporary compatibility aliases.

### Stage 7: Replace manual game inputs

Materialize existing mappings into PlaySession, create future manual sessions directly from Game and Platform, and stop writing manual inputs or credentials.

### Stage 8: Switch presentation and analytics

Read Game and Platform directly from PlaySession and remove effective-platform and presentation-time manual mapping logic.

### Stage 9: Add reconciliation and game merge

Add approved automatic matching, admin-driven review, shared-catalog matching, and transactional duplicate merging without storing resolution state on Game.

### Stage 10: Remove the legacy model

Remove obsolete Game identity/platform columns, manual-input storage, platform credentials, Playnite/manual pseudo-platforms, and compatibility code.

## Completion state

The migration is complete when every session has a canonical Game and a captured Platform at the granularity supported by its source, manual sessions are independent of integrations, polling/API provenance remains traceable, external identities resolve through one path, and no application code depends on legacy Game keys or manual title mappings.
