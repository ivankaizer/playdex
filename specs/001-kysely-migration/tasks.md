# Implementation Plan

- [ ] 1. Establish the typed Kysely database foundation
  - [ ] 1.1 Add Kysely dependencies and define the schema contract
    - Add `kysely` and `kysely-postgres-js` to `apps/api/package.json`.
    - Create `apps/api/src/common/db/schema.ts` with table interfaces for every
      application table, including generated, nullable, and update-specific
      columns.
    - Model PostgreSQL `bigint` output as strings and define reusable selected,
      inserted, and updated row types where they improve repository clarity.
    - Run the API type check before continuing.
    - _Requirements: 2.1, 2.2, 2.3_

  - [ ] 1.2 Implement checked identifier normalization with tests
    - Add focused unit tests for positive numeric values, Postgres.js `bigint`
      strings, zero, negative values, malformed values, and values outside the
      JavaScript safe-integer range.
    - Create `apps/api/src/common/db/ids.ts` with `toDomainId()` and make the
      tests pass.
    - _Requirements: 2.3_

  - [ ] 1.3 Introduce the shared Kysely factory and transitional client bridge
    - Create `apps/api/src/common/db/database.ts` as the only module that
      constructs a Postgres.js client and `PostgresJSDialect`.
    - Add `createDb()`, `getDb()`, `destroyDb()`, and the validated
      administrative database-creation helper.
    - Refactor `apps/api/src/common/db/postgres.ts` into a temporary compatibility
      bridge backed by the same client so existing repositories continue to work
      while they are converted incrementally.
    - Add focused tests for database-name validation and cleanup behavior.
    - _Requirements: 1.1, 1.4, 2.1, 6.2_

- [ ] 2. Replace SQL migration files with Kysely modules
  - [ ] 2.1 Add clean-database migration tests and convert historical modules
    - Rewrite the clean-database cases in `apps/api/tests/migrations.test.ts` to
      expect Kysely metadata, ordered module execution, the complete schema, and
      a no-op second run.
    - Replace `apps/api/migrations/*.sql` with immutable
      `apps/api/migrations/NNN_slug.ts` modules that preserve the original 13
      migrations' order and effects.
    - Refactor `apps/api/src/common/db/migrations.ts` to use Kysely `Migrator`
      and `FileMigrationProvider` with a stable API-root migration path.
    - Run the migration tests against an empty PostgreSQL database.
    - _Requirements: 4.1, 4.3, 4.4, 4.6, 7.1_

  - [ ] 2.2 Implement legacy history adoption with fail-closed tests
    - Add migration tests for a fully applied legacy database, a legacy prefix
      with pending modules, unknown versions, duplicate rows, missing
      predecessors, and divergence between legacy and Kysely metadata.
    - Implement the advisory-locked adoption phase in
      `apps/api/src/common/db/migrations.ts`.
    - Seed validated legacy history into Kysely metadata without invoking
      historical `up()` functions, leave `schema_migrations` read-only, and
      report actionable conflicts before schema mutation.
    - _Requirements: 4.2, 5.1, 5.2, 5.3, 7.2_

  - [ ] 2.3 Prove rollback and concurrent migration safety
    - Add tests that inject a failing TypeScript migration and start concurrent
      migration runners against the same database.
    - Verify that failed migrations roll back and remain unapplied, and that
      concurrent runners record each pending migration exactly once.
    - Adjust advisory-lock cleanup and migration-result error reporting until
      the tests pass.
    - _Requirements: 4.4, 4.5, 5.4, 7.3_

  - [ ] 2.4 Wire the shared migration runner into startup and the CLI
    - Refactor `apps/api/scripts/migrate.ts` to use the shared administrative
      database helper, Kysely factory, and migration runner.
    - Update `apps/api/src/index.ts` so startup calls the same runner before the
      server opens.
    - Add or update tests that prove the CLI-facing runner and startup-facing
      runner share one implementation.
    - _Requirements: 6.1, 6.2_

- [ ] 3. Move database-backed tests onto Kysely fixtures
  - [ ] 3.1 Refactor the shared test database helper
    - Update `apps/api/tests/helpers/db.ts` to create and destroy a Kysely
      database through `createDb()` and the shared migration runner.
    - Expose `db: Kysely<Database>` for new test code while retaining a temporary
      `sql` fixture accessor only for tests that still exercise unconverted
      repositories.
    - Use Kysely builders for normal fixtures and narrow Kysely `sql`
      expressions only for catalog inspection, reset, or database lifecycle
      operations.
    - Run the migration tests and existing database-backed suites.
    - _Requirements: 1.1, 2.1, 7.1, 7.3_

- [ ] 4. Convert the straightforward repositories
  - [ ] 4.1 Convert auth and settings repositories
    - Refactor `apps/api/src/modules/auth/auth.repository.ts` and
      `apps/api/src/modules/settings/settings.repository.ts` to use
      `DbExecutor`, typed Kysely builders, and deliberate identifier
      normalization.
    - Update affected route or service tests to use `db` fixtures and verify
      unchanged reads and writes.
    - _Requirements: 1.1, 1.2, 2.3, 3.1, 3.2, 3.4_

  - [ ] 4.2 Convert providers and Telegram repositories
    - Refactor `apps/api/src/modules/providers/providers.repository.ts`,
      `apps/api/src/modules/telegram/telegram.repository.ts`, and their call
      sites to accept the shared Kysely executor.
    - Preserve credential ordering, nullable values, chat lookups, and Telegram
      notification data shapes.
    - Update or add focused tests before removing the affected temporary SQL
      fixture use.
    - _Requirements: 1.1, 1.2, 2.3, 3.1, 3.2_

- [ ] 5. Convert game and recap read models
  - [ ] 5.1 Convert the games repository
    - Refactor `apps/api/src/modules/games/games.repository.ts` to typed Kysely
      builders for game reads, ignored games, ratings, and connection edges.
    - Add or update tests for ordering, nullable images, ratings, ignored-game
      behavior, and connection results.
    - _Requirements: 1.1, 1.2, 2.2, 2.3, 3.1, 3.2_

  - [ ] 5.2 Convert the recap repository
    - Refactor `apps/api/src/modules/recap/recap.repository.ts` with typed
      builders and explicit computed-row types.
    - Isolate and document only the narrow PostgreSQL expressions needed for
      date ranges, durations, and short-session filters.
    - Update recap service and route tests to prove unchanged aggregation and
      ordering.
    - _Requirements: 1.2, 1.3, 2.4, 3.1_

- [ ] 6. Centralize game upserts and convert polling
  - [ ] 6.1 Add a tested shared game upsert helper
    - Add failing tests for unlocked metadata refresh, `title_locked`,
      `capsule_url_locked`, missing incoming image data, and existing provider
      identity.
    - Create `apps/api/src/common/db/queries/games.ts` with a
      transaction-compatible Kysely upsert helper.
    - Make the focused helper tests pass before converting callers.
    - _Requirements: 3.2, 3.3, 3.5_

  - [ ] 6.2 Convert polling repository and orchestration
    - Refactor `apps/api/src/modules/polling/polling.repository.ts`,
      `apps/api/src/modules/polling/polling.ts`, and
      `apps/api/src/modules/polling/polling-accounts.ts` to use `DbExecutor`,
      Kysely transactions, and the shared game helper.
    - Preserve credential loading, poll-result writes, active-session lookup,
      session start/end behavior, and JSON payload storage.
    - Update `apps/api/tests/polling.test.ts`,
      `apps/api/tests/sql.persistence.test.ts`, and affected fixture code to
      use Kysely.
    - _Requirements: 1.1, 2.3, 3.1, 3.2, 3.3, 3.4, 3.5, 7.3_

- [ ] 7. Convert manual tracking and Playnite synchronization
  - [ ] 7.1 Convert manual tracking
    - Refactor `apps/api/src/modules/manual/manual.repository.ts` to Kysely and
      reuse the shared game helper.
    - Preserve manual credential creation, pending-input lifecycle, conversion
      into play sessions, deletion, ordering, and transaction scope.
    - Update `apps/api/tests/manualEntries.test.ts` and
      `apps/api/tests/manualRoutes.test.ts` to use Kysely fixtures.
    - _Requirements: 1.1, 3.1, 3.2, 3.3, 3.4, 3.5_

  - [ ] 7.2 Convert Playnite synchronization
    - Refactor `apps/api/src/modules/playnite/playnite.repository.ts` to Kysely
      and reuse the shared game helper.
    - Preserve session synchronization, game switching, credential behavior,
      metadata locks, and transaction boundaries.
    - Update `apps/api/tests/playniteRoutes.test.ts` and
      `apps/api/tests/sessionSync.test.ts` to use Kysely fixtures.
    - _Requirements: 1.1, 3.1, 3.2, 3.3, 3.4, 3.5_

- [ ] 8. Convert account queries
  - [ ] 8.1 Convert paginated session reads
    - Add or update tests for session filtering, game filters, date ranges,
      title matching, sort direction, sort field, pagination, and current
      session behavior.
    - Refactor the corresponding reads in
      `apps/api/src/modules/accounts/accounts.repository.ts` with typed Kysely
      builders and explicit aliased result types.
    - Keep only documented narrow expressions for duration and UTC date logic.
    - _Requirements: 1.2, 1.3, 2.4, 3.1_

  - [ ] 8.2 Convert account statistics and grouped reads
    - Add or update tests for account lists, grouped game summaries, daily and
      monthly statistics, platform totals, and short-session filtering.
    - Convert the remaining queries in
      `apps/api/src/modules/accounts/accounts.repository.ts`.
    - Move reusable PostgreSQL expressions to
      `apps/api/src/common/db/expressions.ts` where that reduces duplication.
    - _Requirements: 1.2, 1.3, 2.4, 3.1_

- [ ] 9. Convert admin queries
  - [ ] 9.1 Convert admin reads
    - Add or update admin route tests for recent sessions, filtered counts,
      platform statuses, credential lists, and game lookup behavior.
    - Refactor the read paths in
      `apps/api/src/modules/admin/admin.repository.ts` using typed builders and
      explicit result aliases.
    - Retain a documented narrow `sql` expression for the row-number window
      calculation where needed.
    - _Requirements: 1.2, 1.3, 2.4, 3.1, 3.4_

  - [ ] 9.2 Convert admin writes and metadata-lock operations
    - Add or update tests for credential state changes, session edits, game
      moves, image locking, and title locking.
    - Convert the remaining writes in
      `apps/api/src/modules/admin/admin.repository.ts`.
    - Verify subsequent polling and synchronization writes preserve locked
      metadata through the shared helper.
    - _Requirements: 1.2, 3.2, 3.4, 3.5_

- [ ] 10. Convert database scripts and remove the transition bridge
  - [ ] 10.1 Convert user and seed scripts
    - Refactor `apps/api/scripts/create-user.ts`,
      `apps/api/scripts/update-user-password.ts`, and
      `apps/api/scripts/seed.ts` to use the shared Kysely factory and close
      connections in `finally`.
    - Remove the stale `src/shared/db/queries` import from `seed.ts`, reuse the
      shared game helper where appropriate, and preserve seed transaction and
      sequence-alignment behavior.
    - Run type checking and exercise script entry points against a disposable
      test database where practical.
    - _Requirements: 1.1, 1.3, 1.4, 3.3, 6.1_

  - [ ] 10.2 Remove temporary SQL access and finish runtime wiring
    - Remove the compatibility bridge from
      `apps/api/src/common/db/postgres.ts`, deleting the module if it is no
      longer needed.
    - Remove temporary `sql` fixture access from
      `apps/api/tests/helpers/db.ts` and migrate any remaining tests to Kysely.
    - Update `apps/api/src/index.ts` and remaining call sites so application
      code uses only `getDb()` or an injected `DbExecutor`.
    - Search `apps/api/src`, `apps/api/scripts`, and `apps/api/tests` for direct
      Postgres.js imports and driver-level SQL use outside the shared database
      boundary.
    - _Requirements: 1.1, 1.4, 7.4_

- [ ] 11. Add enforcement, documentation, and final verification
  - [ ] 11.1 Add the database-boundary repository guard
    - Add tests for allowed and disallowed fixture trees, then create
      `apps/api/scripts/check-db-boundary.ts`.
    - Fail on `.sql` files under `apps/api/migrations`, invalid migration module
      names, direct `postgres` imports outside
      `apps/api/src/common/db/database.ts`, and unapproved unsafe execution.
    - Add the guard to `apps/api/package.json` and `bun run check`.
    - _Requirements: 1.4, 4.6, 7.4_

  - [ ] 11.2 Document the Kysely migration workflow
    - Update `README.md` with `bun run db:migrate`, startup migration behavior,
      automatic legacy adoption, immutable `NNN_slug.ts` ordering, and the rule
      that future migrations use TypeScript modules.
    - Verify the documented command invokes the same runner used at startup.
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [ ] 11.3 Run the full completion gate
    - Run `bun run check`, the complete API test suite against PostgreSQL, and
      the Docker build.
    - Confirm empty-database initialization, legacy adoption, migration
      rollback, concurrent execution, repository behavior, and the static guard
      all pass.
    - Inspect the final diff for direct Postgres.js access, standalone SQL
      migrations, undocumented Kysely `sql` fragments, and unrelated changes.
    - _Requirements: 5.4, 7.1, 7.2, 7.3, 7.4, 7.5_
