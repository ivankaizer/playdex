# .NET Migration Completion Verification

## 2026-07-18 Final Razor Cutover Update

**Branch:** `codex/blazor-migration`
**HEAD before uncommitted cutover changes:** `04060a5`
**Frontend status:** **Runtime cut over; settled-route visual gate complete; full interactive-state parity evidence open**
**Broader backend status:** **Implementation cut over; exhaustive migration checklist still open**

This section supersedes the older Razor/frontend blockers recorded below.
Every React route now has a Blazor implementation, the production and
development delivery paths target Blazor, and React/Vite source, tests,
configuration, and dependencies have been removed.

Fresh current-working-tree evidence:

- settled-route visual parity: **48 passed, 0 failed**, across eight routes,
  three viewports, and two themes, with **0 console errors**;
- strict Release solution build: **0 warnings, 0 errors**;
- `Playdex.Api.Tests` against isolated PostgreSQL: **54 passed, 0 failed**;
- `Playdex.Web.Tests`: **284 passed, 0 failed**;
- live Bun-versus-.NET root/auth contracts: **5 passed, 0 failed**;
- frozen CSS install and Tailwind build: passed;
- Docker Compose builds for the .NET API and Blazor development UI: passed;
- React-free production Blazor image: built and smoke-tested; `/` and direct
  `/sessions` returned 200, and the fingerprinted runtime WASM was served as
  `application/wasm`.

The live matrix exposed populated-row EF projection failures that empty tests
had missed. Regression coverage was added and game details/search,
connections, providers, admin sessions, and credentials now use typed LINQ
projections. CORS `OPTIONS`/204 responses remain bodyless, and the admin date
output matches browser `toLocaleString()` formatting.

The frontend runtime migration is implemented, but the approved 1:1 parity
plan is not fully evidenced. The 48-cell run covered settled populated route
views; it did not live-capture every loading, failure, redirect, query, modal,
validation, saving, and mutation state for every route, viewport, and theme.
Focused state/request tests do not replace those browser comparisons. React was
removed after the settled-route gate rather than after the complete interactive
matrix. The broader `.NET` verification also remains open for wider
differential coverage beyond root/auth and deeper hosted polling/Telegram and
operational rollback evidence.

The final review also found that .NET startup registered an empty migration
catalog. The current working tree now registers `001_current_schema`, an
idempotent current-schema bootstrap for fresh PostgreSQL databases and adoption
of fully Kysely-migrated databases, plus a disposable-database regression test.
That test has not yet produced a release-gate result because the managed sandbox
blocked the test runner's loopback socket and the escalation quota was
exhausted. It must pass before the migration can be called deployable.

**Verified:** 2026-07-13
**Branch:** `codex/net`
**HEAD:** `9ad6d68`
**Scope:** Current working tree, including uncommitted changes
**Status:** **Not complete**

## Executive Summary

The .NET migration is substantially implemented, but it is not ready for production cutover. The solution builds and its default test command exits successfully, yet database-backed and differential contract verification did not execute. The current refactor also breaks the Bun API response contract, while CI, production image publishing, and developer documentation still target the Bun backend.

## 2026-07-18 PostgreSQL and Razor Follow-up

- **Branch:** `codex/blazor-migration`
- **Scope:** Current working tree, including uncommitted changes
- **Status:** **Not complete**

PostgreSQL 16 was brought back online and reported healthy. Two isolated databases were recreated without touching the normal `playdex` database: `playdex_dotnet_it` for .NET integration tests and `playdex_contract` for differential fixtures. All 13 Bun/Kysely migrations applied successfully to both clean databases.

Fresh executable evidence from the current working tree:

- `Playdex.Api.Tests`: **52 passed, 0 failed**, including database-backed ownership, ignored-session, connected-game aggregation, manual-mapping, Admin null-clearing, authentication, and migration regressions.
- `Playdex.Web.Tests`: **279 passed, 0 failed**.
- `Playdex.ContractTests`: **5 reported passed**, but this is not a live differential result because `BUN_API_BASE_URL` and `DOTNET_API_BASE_URL` were unset.
- The strict Release solution build completed after the current frontend and backend changes with **0 warnings and 0 errors**.

All React route families now have Blazor route implementations: `/`, `/dashboard`, `/sessions`, `/recap`, `/mapping`, `/settings`, `/games/{hash}`, and `/admin`. The new Game and Admin pages have focused state/request/markup coverage, and the full Blazor suite compiles and passes.

Backend gaps closed in this follow-up include:

- user-scoped Manual mapping writes with a database-backed cross-user regression;
- string-preserving IGDB IDs, IGDB search/mapping support, and `igdb:<id>` canonical keys;
- short-session ignored flags in session responses and exclusion from stats;
- manual mapped game/platform/source projection;
- connected-game filter traversal and lossless aggregate merging;
- provider secret masking and settings-range parity;
- Admin presence-aware field updates, including explicit `null` clearing;
- the direct safe `Microsoft.OpenApi` dependency override.

The migration remains incomplete because live Bun-versus-.NET differential requests, the full visual viewport/theme/state matrix for the six newly migrated routes, Docker/CI production delivery cutover, rollback rehearsal, and React/Vite removal have not passed. React remains in place until those gates are green.

## Follow-up After PostgreSQL Upgrade

- **Re-verified:** 2026-07-13
- **Branch:** `codex/blazor-migration`
- **HEAD before the final uncommitted UI correction:** `04060a5`
**Updated status:** **Not complete**

PostgreSQL 16 is now healthy and the previously gated database checks were rerun against an isolated `playdex_dotnet_it` database created from the 13 Bun/Kysely migrations. The full `Playdex.Api.Tests` suite reported **33 passed, 0 failed**. The test fixture now treats the legacy `schema_migrations` table as optional and read-only: the final catalog check returned `<missing>|13`, proving the suite did not create the legacy table and preserved all Kysely history rows. The existing `NU1903` advisory for `Microsoft.OpenApi` remains.

The Home and Dashboard Razor work also advanced after the database became available:

- The Dashboard UTC date-boundary and quoted EF projection-alias failures were corrected in the current working tree.
- React/Bun and Blazor/.NET rendered the same isolated `playdex_visual` data at phone, tablet, and desktop widths in light and dark themes.
- Empty and populated/current-session Dashboard DOM matched at all six viewport/theme cells. The manual-session modal also matched at all six cells, including title validation, enabled Start state, and Cancel behavior.
- Dark modal captures were pixel-identical at phone and desktop sizes; tablet had a maximum channel delta of 2. Light captures had fewer than `0.08%` of pixels over a five-level channel delta and no pixel over a 20-level delta.
- Fresh Blazor web tests reported **176 passed, 0 failed**; the Tailwind CSS build passed. The final scoped Blazor build completed with zero warnings and zero errors before the browser comparison.

A later repeat build inside the restricted task sandbox did not reach compilation: after five minutes, MSBuild reported `MSB4216` because it could not create or connect to the required .NET/arm64 WebAssembly task host, followed by `MSB4027`. The previously successful build artifact was the one exercised in the final browser comparison. This environment-level repeat failure still needs a privileged rerun before release gating; it is not recorded as a passing build.

This closes the earlier scoped Dashboard API blockers, but it does **not** finish the migration. Razor route families for `/sessions`, `/recap`, `/mapping`, `/settings`, `/games/{hash}`, and `/admin` are still absent. Dashboard aggregate-error and manual-session error/saving visual states remain uncaptured. The broader API-contract, differential coverage, NuGet advisory, CI, production-image cutover, runbook, and rollback gates below also remain open. React must remain in the repository until those routes and states pass the parity matrix.

## Fresh Verification Evidence

The following command was run against the current working tree:

```bash
DOTNET_CLI_HOME=.tmp/dotnet dotnet test apps/api-dotnet/Playdex.sln --no-restore
```

Reported result:

- `Playdex.Api.Tests`: 18 passed, 0 failed.
- `Playdex.ContractTests`: 5 passed, 0 failed.
- The command exited successfully.
- NuGet emitted `NU1903` for the high-severity `GHSA-v5pm-xwqc-g5wc` vulnerability affecting transitive `Microsoft.OpenApi` 2.0.0.

This result is not a complete parity gate. `PLAYDEX_INTEGRATION_DATABASE_URL`, `PLAYDEX_CONTRACT_DATABASE_URL`, `BUN_API_BASE_URL`, and `DOTNET_API_BASE_URL` were unset. Nine of the 23 reported tests therefore returned before making assertions:

- All five differential contract tests.
- Both HTTP integration tests.
- The existing-database mapping test.
- The database-backed migration-runner test.

Only 14 test cases performed substantive assertions during this run. The environment-gated tests currently report as passed rather than explicitly skipped.

## Verified Implementation Progress

- The .NET 10 solution, API project, test project, and contract-test project exist under [`apps/api-dotnet/`](../../apps/api-dotnet/).
- A multi-stage, non-root .NET image exists in [`apps/api-dotnet/Dockerfile`](../../apps/api-dotnet/Dockerfile).
- Local Docker Compose targets the .NET API on the existing port and service name.
- The Bun route and method surface is broadly registered in the .NET API.
- Basic bcrypt login, JWT issuance and validation, health endpoints, OpenAPI endpoints, CORS, structured console logging, polling, and Telegram hosted services have implementations.

These items demonstrate substantial migration progress, but they do not satisfy the full behavior-parity, verification, and cutover requirements.

## Completion Blockers

### 1. API Contract Parity Is Broken

The Bun API still returns the compatibility envelope `{ data, error }` from [`apps/api/src/app/http/responses.ts`](../../apps/api/src/app/http/responses.ts). The current .NET endpoints return raw DTOs through `TypedResults.Ok`, and [`ApiExceptionHandler.cs`](../../apps/api-dotnet/Playdex.Api/Http/ApiExceptionHandler.cs) returns `{ status, detail, correlationId }`.

The dormant contract tests still expect envelope paths such as `$.data.accessToken`. Activating them against the current implementations would expose this mismatch. No approved compatibility change documenting removal of the envelope was found.

Unhandled exceptions also serialize `exception.Message`, which can expose internal implementation details and violates the migration security requirements.

### 2. Automated Parity Coverage Is Incomplete

The HTTP and differential suites cover only `/api`, `/api/login`, and `/api/me`. There is no automated HTTP parity coverage for refresh, accounts, settings, games, manual entry, Playnite ingestion, providers, admin, recap, metrics, health, OpenAPI, CORS, or hosted-service behavior.

Migration-runner coverage does not prove every scenario required by task 4, including migration failure rollback and rollback-metadata behavior.

The repository's [`REFACTOR_TODO.md`](../../apps/api-dotnet/REFACTOR_TODO.md) also records the full test suite and per-credential polling exception handling as unfinished.

### 3. Known Behavioral Issues Remain

- Deleting a missing session uses `FirstAsync` in [`AccountsRepository.cs`](../../apps/api-dotnet/Playdex.Api/Accounts/AccountsRepository.cs). An empty `DELETE ... RETURNING` result throws before the service can return the intended 404.
- The settings service retains an incompatible 1,440-minute upper bound.
- Admin credential updates use `COALESCE`, so an explicit `null` cannot clear an existing API key or account user.
- Polling and Telegram implementations lack the required fake-clock, adapter, cancellation, notification-image, and database-write verification.
- OpenAPI security metadata, hosted-service readiness state, secret-log tests, and Bun-baseline OpenAPI comparison are unverified or missing.

### 4. CI and Production Cutover Are Not Complete

- [`.github/workflows/test.yaml`](../../.github/workflows/test.yaml) runs Bun API and web checks only. It does not restore, build, analyze, or test the .NET solution and does not run differential contracts.
- [`.github/workflows/build.yaml`](../../.github/workflows/build.yaml) still builds and publishes `apps/api`, the Bun backend.
- [`README.md`](../../README.md) still describes Playdex as Bun/Elysia, requires Bun for backend development, and documents Kysely as the active migration system.
- No completed deployment, database-migration, cutover, rollback runbook, or non-production rollback rehearsal artifact was found.

### 5. Migration Tracking Remains Open

Tasks 5 through 12 remain unchecked in [`tasks.md`](tasks.md). Some implementation exists for each task, but none has the complete behavior and verification evidence required by its objective.

At verification time, the working tree also contained 47 tracked changes and 26 untracked entries. The current state is active development rather than a clean release candidate.

## Required Completion Gates

Before the migration can be marked complete:

1. Restore Bun-compatible response and error contracts, or document and approve the breaking change and migrate every client.
2. Fix the known behavioral and internal-error disclosure issues.
3. Make missing integration prerequisites explicit failures or real skips, then run the PostgreSQL-backed suite.
4. Run both APIs against isolated equivalent databases and expand differential coverage across the complete public API.
5. Add tests for polling, Telegram, health, CORS, OpenAPI, logging, migration failure, and rollback behavior.
6. Resolve the high-severity NuGet advisory.
7. Add .NET restore, analysis, build, integration, contract, and OpenAPI gates to CI.
8. Switch production image publishing to `apps/api-dotnet` only after parity passes.
9. Update developer, deployment, migration, cutover, and rollback documentation and rehearse rollback in a production-like environment.
10. Re-run this completion audit from a clean release-candidate working tree and close the remaining implementation-plan tasks.

## Verdict

**The .NET migration is not finished.** The implementation is well underway, but behavior parity, meaningful automated verification, security cleanup, CI integration, production cutover, and rollback validation remain incomplete.
