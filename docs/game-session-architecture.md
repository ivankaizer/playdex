# Game, integration, and play-session architecture

Status: target architecture and migration outline. Each migration stage is implemented as one independently deployable MR. Stage details will be expanded in this document before implementation.

## Goal

Separate the canonical game, its external identities, the selected gaming platform, the integration that delivered an event, and the method used to capture a play session.

## Domain model

- **Game** is a platform-independent game known to Playdex.
- **Game external identity** identifies a Game in a system outside Playdex. Provider identities (`steam`, `psn`, `xbox`) may be observed during session capture; the catalog identity `igdb` is used for reconciliation and metadata enrichment.
- **Platform** is the gaming service, store, or console ecosystem selected for an event: for example `steam`, `gog`, `ps5`, or `xbox`.
- **Game ID provider** is the external namespace in which an incoming game ID is meaningful. In the API contract, `gameIdProvider` maps directly to `GameExternalIdentity.type` and is independent of Platform.
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
  type                       game_external_identity_type
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
- Platform and Game ID provider are independent. A session played through GOG may identify its Game with either a Steam or IGDB identity.
- In API requests, `gameId` is an external identifier scoped by `gameIdProvider`; it is not the internal `games.id`.
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
           -> gameIdProvider + external gameId -> GameResolver -> Game
           -> event-selected Platform
           -> PlaySession(capture_method = api)
```

For example:

```json
{
  "platform": "gog",
  "gameId": "123",
  "gameIdProvider": "steam"
}
```

The resolver looks up `(type = steam, external_id = 123)`, while the resulting PlaySession records `platform = gog`. The Steam identity identifies the Game; it does not imply that the session was played on Steam.

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

**MR goal:** add the external-identity persistence model and backfill identities that can be derived without guessing. The application continues to resolve and write games through the legacy columns in this stage.

**Schema:**

```text
game_external_identity_type enum
  steam | psn | xbox | igdb

game_external_identities
  id            bigserial primary key
  game_id       bigint not null references games(id) on delete restrict
  type          game_external_identity_type not null
  external_id   text not null
  title         text not null
  created_at    timestamptz not null
  updated_at    timestamptz not null

  UNIQUE(type, external_id)
  INDEX(game_id)
```

`external_id` is a trimmed, non-empty value. `type` is the PostgreSQL enum `game_external_identity_type`; adding another provider or catalog requires a schema migration that extends this enum.

A Game referenced by an external identity cannot be deleted directly. Identities must be deliberately reassigned or removed first, including as part of a transactional Game merge.

**Migration and backfill:**

- Add migration `004_game_external_identities`, create `game_external_identity_type`, and register the migration after the existing migrations.
- Add the corresponding application enum, entity, `DbSet`, and EF/Npgsql enum mapping without changing the public API.
- For a Game with a non-empty `external_id`, derive `type` from the prefix of `canonical_key` only when the prefix is one of the supported identity types.
- Copy `games.external_id` to `external_id` and the current `games.title` to identity `title`.
- An `igdb:<id>` key always produces an `igdb` identity regardless of the Game's legacy Platform.
- Skip Games with a missing external ID, a title-derived canonical key, or an unsupported/`unknown` prefix. The migration must not invent an identity.
- Make the backfill conflict-safe on `(type, external_id)` and fail if the same external identity is already associated with a different Game; silently choosing one would hide an existing duplicate.

**Compatibility boundary:**

- Keep `games.external_id`, `games.canonical_key`, and `games.platform_id`, including their constraints and indexes.
- Keep Polling, Playnite/API, Manual, presentation, and session behavior unchanged.
- Do not dual-write newly observed identities yet; that belongs to Stage 2.
- Do not merge duplicate Games in this MR.

**Verification:**

- Migration tests verify the enum values, rejection of unsupported types, the restricted Game deletion, table shape, uniqueness, and indexes.
- Backfill tests cover Steam/PSN/Xbox/IGDB identities, blank external IDs, unsupported prefixes such as GOG, and conflict detection.
- Existing API and session-lifecycle tests remain green, proving the additive migration has no behavioral effect.

**Deployability:** the MR can be deployed and rolled back at the application level before Stage 2 because no runtime path depends on the new table. The table and its backfilled data remain in place for the next MR.

### Stage 2: Resolve games by external identity

**MR goal:** replace ingestion-specific Game upserts with one resolver while keeping the legacy Game model operational.

```text
Resolve(provider, externalId?, title, legacyPlatform, capsule?)
    -> GameResolution(gameId, needsCapsule)
```

- Resolve exact `(type, external_id)` before consulting legacy keys or titles.
- Create/update Game and identity atomically under a transaction-scoped advisory lock.
- Join a caller transaction or own one when called independently.
- Dual-write identities from Polling, Playnite/API, and manual IGDB mapping.
- Keep identity provider independent from selected Platform, including GOG sessions identified by Steam or IGDB IDs.
- Respect Game title/capsule locks while retaining the latest provider-reported title on the identity.
- Preserve title-derived legacy resolution without an identity when a provider supplies no external ID.
- Retain `games.external_id`, `canonical_key`, and `platform_id`; removing them remains out of scope.

### Stage 3: Define integration-to-platform ownership

Create `integrations`, add their nullable `platform_id`, bind polling connections to a provider-level Platform, and leave API connections unbound so each API event selects its Platform.

### Stage 4: Add the target play-session fields

Add and backfill `user_id`, `platform_id`, `capture_method`, and `integration_id` directly on `play_sessions`.

### Stage 5: Move polling to the target session model

Use Integration for polling lifecycle correlation, keep the observed external identity on PollResult, and copy `Integration.platform_id` to PlaySession without inferring device generation.

### Stage 6: Generalize Playnite into API capture

Introduce a generic API session contract whose ping/event selects `platform` and provides `gameIdProvider + gameId` per request. Validate `gameIdProvider` against `game_external_identity_type`, resolve the internal Game independently from Platform, and retain Playnite endpoints only as temporary compatibility aliases.

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
