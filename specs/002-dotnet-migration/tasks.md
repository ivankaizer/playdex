# Implementation Plan

- [x] 1. Establish the .NET solution and executable compatibility host
  - **Objective:** Create `apps/api-dotnet/Playdex.sln`, `Playdex.Api`, and `Playdex.Api.Tests` targeting `net10.0`; add a minimal `Program.cs` that binds port 3000, loads typed validated configuration from the existing environment variables, and exposes a `/api` root endpoint without changing the current API implementation. Add unit/integration tests proving required configuration fails clearly and the host starts with a valid test configuration.
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 6.1_

- [x] 2. Build the shared HTTP, JSON, and error-contract foundation
  - **Objective:** Add the API composition root, route-group registration, `System.Text.Json` configuration, correlation-ID middleware, and an `AppProblem` exception/result layer. Capture representative current Bun responses as fixtures, then test that validation, unauthenticated, forbidden, not-found, conflict, and unhandled errors return the same status/body contract while logs retain diagnostic details only.
  - _Requirements: 2.2, 2.3, 4.1, 4.2, 5.1, 6.2_

- [x] 3. Port database connectivity and map the existing schema read-only
  - **Objective:** Add a validated `NpgsqlDataSource`, EF Core/Npgsql context and entity mappings for every existing PostgreSQL table, preserving names, keys, relationships, nullable fields, lock flags, `bigint` handling, and UTC timestamps. Do not generate or apply an initial migration. Add PostgreSQL 16 integration tests proving the mapped model reads existing fixture data without modifying the legacy/Kysely migration-history tables.
  - _Requirements: 3.1, 3.2, 3.5, 6.1_

- [x] 4. Create the .NET migration-runner boundary
  - **Objective:** Implement ordered C# `NNN_slug` migration discovery, a `dotnet_schema_migrations` metadata table, advisory-lock acquisition, atomic migration recording, and a `dotnet run -- migrate` command. Keep `schema_migrations`, `kysely_migration`, and `kysely_migration_lock` read-only. Add integration tests for clean adoption, ordered execution, lock contention, unknown history, failures, and rollback metadata.
  - _Requirements: 3.1, 3.3, 3.4, 6.1, 6.3, 6.5_

- [ ] 5. Port identity, session, and authorization behavior
  - **Objective:** Implement bcrypt-compatible password verification, JWT issuance/validation, refresh/session persistence, current-user resolution, and endpoint authorization policies using the existing secrets, claims, TTL semantics, and error bodies. Port `/api/login`, `/api/refresh`, and `/api/me`; add real-PostgreSQL tests for valid, expired, malformed, revoked, and unauthorized token paths.
  - _Requirements: 2.1, 2.2, 2.3, 4.1, 4.2, 4.3, 6.2_

- [ ] 6. Port account, session, and settings feature slices
  - **Objective:** Implement repositories, services, contracts, and minimal API endpoint groups for accounts, user/account sessions, daily/current sessions, statistics, and settings. Preserve query filters, ordering, pagination, authorization checks, write transaction scope, and JSON response shapes. Add unit tests for business rules and HTTP/database integration tests for each success and failure path.
  - _Requirements: 2.1, 2.2, 2.3, 3.2, 3.5, 4.2, 6.2_

- [ ] 7. Port games, manual-entry, and Playnite ingestion slices
  - **Objective:** Implement game search/details, connections, ignored games, ratings, manual inputs/sessions/entries/mappings, and Playnite session endpoints. Recreate the current title/image lock, provider identity, upsert, and multi-table transaction behavior with parameterized Npgsql/EF operations. Add integration tests that cover lock behavior, duplicate/conflict handling, rollback on partial failure, and Playnite client contract compatibility.
  - _Requirements: 2.1, 2.2, 2.3, 3.2, 3.5, 4.2, 6.2_

- [ ] 8. Port provider, admin, recap, and operational feature slices
  - **Objective:** Implement providers/platform status/key endpoints, all admin sessions/credentials/game endpoints, recap endpoints, and the existing authenticated `/api/metrics` response contract. Preserve role checks, report-query semantics, provider status updates, and error shapes. Add endpoint and repository integration coverage for authorized and unauthorized flows.
  - _Requirements: 2.1, 2.2, 2.3, 4.2, 5.1, 6.2_

- [ ] 9. Implement hosted polling and Telegram integrations
  - **Objective:** Port Steam/PSN polling to a cancellation-aware `BackgroundService` with existing interval clamping and safe per-account/provider failure handling. Port Telegram notification and bot handling behind testable adapters, preserving disabled-token behavior and notification image output. Add fake-clock and adapter-based tests for cycles, failures, graceful cancellation, notifications, and database writes.
  - _Requirements: 2.1, 3.2, 3.5, 5.1, 5.3, 6.1_

- [ ] 10. Add CORS, OpenAPI, health, and structured-observability compatibility
  - **Objective:** Configure explicit CORS-origin parsing, JWT security metadata, OpenAPI generation and Swagger UI at the existing URLs, JSON structured logs, `/health/live`, and `/health/ready`. Ensure readiness includes configuration, migration, database, and hosted-service state while liveness remains dependency-free. Test no secret-bearing fields are emitted in logs and compare the generated OpenAPI document to the approved Bun baseline.
  - _Requirements: 1.3, 1.4, 2.2, 4.3, 5.1, 5.2, 5.3, 6.2_

- [ ] 11. Build the differential API-contract test suite
  - **Objective:** Create `Playdex.ContractTests` fixture data and runners that execute equivalent requests against the Bun reference and .NET candidate using isolated identical PostgreSQL databases. Normalize only approved volatile values (timestamps, tokens, correlation IDs), compare status, headers, and JSON bodies, and make new endpoint work fail until its fixture coverage passes.
  - _Requirements: 2.1, 2.2, 2.3, 3.2, 4.1, 4.2, 6.1, 6.2_

- [ ] 12. Wire the .NET API into local container development and automated verification
  - **Objective:** Add a multi-stage non-root Dockerfile, update `docker-compose.yml` to build the .NET API behind the existing `app` service/port/environment contract, and add scripts or CI configuration to restore, format/analyze, build, test against PostgreSQL 16, run differential contract tests, and generate OpenAPI comparison artifacts. Validate the web app and Playnite client can use the cutover container without base-URL or contract changes.
  - _Requirements: 1.2, 1.3, 2.2, 5.2, 5.4, 6.1, 6.2, 6.3, 6.4, 6.5_
