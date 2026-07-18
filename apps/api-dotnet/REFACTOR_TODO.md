# .NET API Refactor TODO

> Goal: clean, modern, readable, .NET 10 standards.

---

## Phase 1: Endpoints ← DONE

- [x] Extract shared `ClaimsPrincipalExtensions` (GetUserId + AssertAdminAsync)
- [x] Eliminate `ApiResponse<T>` wrapping → `TypedResults.Ok()` / `TypedResults.Problem()`
- [x] Convert POST/PUT handlers to `IResult result` + `ExecuteAsync` pattern
- [x] Delete `ApiResponse.cs`, `ErrorResponseFactory.cs`
- [x] Create skill: `dotnet-endpoint-patterns`

## Phase 2: Services ← DONE

- [x] Unify 4x NormalizeTitle → single canonical `GamesDomain.NormalizeTitle`
- [x] Delete dead code: `PlayniteService.NormalizeTitle`, dead `GamesDomain` methods
- [x] Add `ct.ThrowIfCancellationRequested()` in RecapService long loop
- [x] Create skill: `dotnet-service-patterns`

## Phase 3: Repositories ← DONE

- [x] Fix missing CancellationToken in all Dapper calls (`CommandDefinition`)
- [x] Validate `direction` parameter → ASC/DESC only (SQL injection fix)
- [x] Drop all 11 `IXxxRepository` interfaces (single implementations)

## Phase 4: Infrastructure ← DONE

- [x] Fix `ComputeFallbackId` — `SHA256.HashData()` instead of `string.GetHashCode`
- [x] `GameId.Encode` — throws `ArgumentOutOfRangeException` for `id <= 0`
- [ ] Per-credential exception handling in polling (not done — needs testing)

## Phase 5: Polish ← DONE

- [x] Add `correlationId` to error response envelope
- [x] Build: 0 errors
- [ ] Run full test suite (tests need updating for Phase 1+3 API changes)
