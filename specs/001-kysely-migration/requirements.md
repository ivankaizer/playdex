# Requirements Document

## Introduction

Playdex currently uses raw SQL for PostgreSQL access and SQL migration files for
schema evolution. This change will migrate database access and migration
execution to Kysely while preserving the deployed schema, existing data,
application behavior, and operator workflow.

The migration must make Kysely the standard database boundary for application
queries and schema migrations. Narrow Kysely `sql` expressions may remain only
when a PostgreSQL feature cannot be expressed clearly with Kysely's typed
builders.

## Requirements

### Requirement 1: Kysely Database Boundary

**User Story:** As a maintainer, I want application database access to use
Kysely, so that queries are consistently structured and type checked.

#### Acceptance Criteria

1. WHEN an application module reads or writes PostgreSQL data THEN the system
   SHALL execute the operation through the shared Kysely database instance.
2. WHEN Kysely provides a typed query-builder operation for a database query
   THEN the implementation SHALL use that operation instead of a raw SQL query.
3. IF a required PostgreSQL operation cannot be expressed clearly with Kysely's
   typed builders THEN the implementation SHALL isolate the narrowest possible
   Kysely `sql` expression and document why it is required.
4. WHEN the migration is complete THEN the system SHALL contain no direct
   application-level database calls that bypass the shared Kysely boundary.

### Requirement 2: Typed Schema Contract

**User Story:** As a maintainer, I want the PostgreSQL schema represented in
TypeScript, so that database changes and application queries are checked
against a single explicit contract.

#### Acceptance Criteria

1. WHEN the Kysely database instance is created THEN the system SHALL bind it
   to a TypeScript database schema interface covering every application table.
2. WHEN a table contains generated, nullable, or update-specific columns THEN
   the schema contract SHALL represent those column behaviors accurately.
3. WHEN application code consumes database rows THEN the system SHALL preserve
   the existing runtime behavior for PostgreSQL values, including bigint-backed
   identifiers.
4. IF a query selects a computed or aliased value THEN the implementation SHALL
   define an explicit result type or typed alias for that value.

### Requirement 3: Query Behavior Preservation

**User Story:** As a Playdex user, I want the database migration to be
behaviorally transparent, so that existing product workflows continue to work.

#### Acceptance Criteria

1. WHEN an existing read query is migrated THEN the system SHALL return the
   same records, ordering, pagination behavior, and externally visible values
   as before.
2. WHEN an existing write query is migrated THEN the system SHALL preserve its
   insert, update, upsert, conflict-handling, and deletion semantics.
3. WHEN an existing operation relies on a transaction THEN the system SHALL
   execute the migrated statements within an equivalent Kysely transaction.
4. WHEN polling, manual tracking, Playnite synchronization, admin updates, or
   authentication flows access the database THEN the system SHALL preserve
   their existing behavior.
5. IF an existing query preserves locked game metadata during an upsert THEN
   the migrated query SHALL continue to preserve the locked values.

### Requirement 4: Kysely Migration Definitions

**User Story:** As an operator, I want schema migrations managed through
Kysely, so that schema evolution uses the same database tooling as the
application.

#### Acceptance Criteria

1. WHEN a new database is initialized THEN the system SHALL apply the complete
   schema history using ordered Kysely migration modules.
2. WHEN an existing Playdex database is upgraded THEN the system SHALL
   recognize migrations that were already applied by the legacy migration
   runner and SHALL NOT reapply their schema changes.
3. WHEN historical SQL migration files are converted THEN the Kysely migration
   modules SHALL preserve their original ordering and schema effects.
4. WHEN a pending migration is applied THEN the system SHALL record it through
   the Kysely migration workflow exactly once.
5. IF multiple application instances attempt to migrate the same database
   concurrently THEN the system SHALL prevent duplicate or conflicting schema
   changes.
6. WHEN the conversion is complete THEN future schema changes SHALL be added as
   Kysely migration modules rather than standalone SQL migration files.

### Requirement 5: Existing Database Compatibility

**User Story:** As a self-hosted operator, I want upgrades to preserve my
database, so that adopting Kysely does not require a reset or manual data
migration.

#### Acceptance Criteria

1. WHEN an existing supported Playdex database starts after the application
   upgrade THEN the system SHALL retain all existing application data.
2. WHEN the Kysely migration workflow adopts legacy migration history THEN the
   system SHALL complete without requiring operators to edit migration metadata
   manually.
3. IF legacy migration metadata is incomplete, inconsistent, or incompatible
   with the expected history THEN the system SHALL stop before applying schema
   changes and report an actionable error.
4. WHEN the migration runner fails THEN the system SHALL surface the failing
   migration and preserve a recoverable database state.

### Requirement 6: Developer and Operator Workflow

**User Story:** As a maintainer, I want a clear Kysely migration workflow, so
that local development, CI, and self-hosted upgrades use the same commands.

#### Acceptance Criteria

1. WHEN a developer runs the documented database migration command THEN the
   system SHALL execute the Kysely migration workflow.
2. WHEN the application starts in an environment that performs automatic
   migrations THEN the system SHALL use the same Kysely migration workflow as
   the documented command.
3. WHEN a developer creates a schema change THEN the repository SHALL provide
   a documented convention for naming, ordering, and implementing a Kysely
   migration module.
4. WHEN the migration is delivered THEN project documentation SHALL describe
   the Kysely-based database workflow and any operational upgrade notes.

### Requirement 7: Verification and Completion

**User Story:** As a maintainer, I want automated proof of compatibility, so
that the Kysely migration can be shipped without silent database regressions.

#### Acceptance Criteria

1. WHEN automated verification runs against an empty PostgreSQL database THEN
   the system SHALL build the expected schema exclusively through Kysely
   migration modules.
2. WHEN automated verification runs against a legacy Playdex database THEN the
   system SHALL adopt prior migration history and apply only pending Kysely
   migrations.
3. WHEN automated verification runs application tests THEN the system SHALL
   verify the preserved behavior of migrated reads, writes, upserts,
   transactions, and failure paths.
4. WHEN repository checks run THEN the system SHALL fail if disallowed direct
   raw SQL access or standalone SQL migration files are reintroduced.
5. WHEN the implementation is complete THEN the API type check and relevant
   database-backed test suites SHALL pass.

## Review Focus

The initial draft assumes that both historical SQL migrations and future
migrations move to Kysely modules. It also assumes that narrowly scoped Kysely
`sql` expressions remain acceptable for PostgreSQL-specific features that do
not have a clear typed-builder equivalent.
