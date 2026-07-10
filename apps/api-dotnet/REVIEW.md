---
phase: dotnet-migration-review
reviewed: 2026-07-10T00:00:00Z
depth: deep
files_reviewed: 19
files_reviewed_list:
  - apps/api-dotnet/Playdex.Api/Auth/AuthEndpoints.cs
  - apps/api-dotnet/Playdex.Api/Accounts/AccountsEndpoints.cs
  - apps/api-dotnet/Playdex.Api/Accounts/AccountsDto.cs
  - apps/api-dotnet/Playdex.Api/Accounts/AccountsRepository.cs
  - apps/api-dotnet/Playdex.Api/Accounts/AccountsService.cs
  - apps/api-dotnet/Playdex.Api/Settings/SettingsEndpoints.cs
  - apps/api-dotnet/Playdex.Api/Settings/SettingsDto.cs
  - apps/api-dotnet/Playdex.Api/Settings/SettingsService.cs
  - apps/api-dotnet/Playdex.Api/Games/GamesEndpoints.cs
  - apps/api-dotnet/Playdex.Api/Games/GamesDto.cs
  - apps/api-dotnet/Playdex.Api/Games/GamesService.cs
  - apps/api-dotnet/Playdex.Api/Games/GamesRepository.cs
  - apps/api-dotnet/Playdex.Api/Manual/ManualEndpoints.cs
  - apps/api-dotnet/Playdex.Api/Manual/ManualDto.cs
  - apps/api-dotnet/Playdex.Api/Manual/ManualService.cs
  - apps/api-dotnet/Playdex.Api/Playnite/PlayniteEndpoints.cs
  - apps/api-dotnet/Playdex.Api/Providers/ProvidersEndpoints.cs
  - apps/api-dotnet/Playdex.Api/Admin/AdminEndpoints.cs
  - apps/api-dotnet/Playdex.Api/Admin/AdminDto.cs
  - apps/api-dotnet/Playdex.Api/Admin/AdminService.cs
  - apps/api-dotnet/Playdex.Api/Recap/RecapEndpoints.cs
  - apps/api-dotnet/Playdex.Api/Recap/RecapDto.cs
  - apps/api-dotnet/Playdex.Api/Recap/RecapService.cs
  - apps/api-dotnet/Playdex.Api/Metrics/MetricsEndpoints.cs
  - apps/api-dotnet/Playdex.Api/Metrics/MetricsDto.cs
  - apps/api-dotnet/Playdex.Api/Http/ApiResponse.cs
  - apps/api-dotnet/Playdex.Api/Http/AppProblem.cs
  - apps/api-dotnet/Playdex.Api/Http/ApiExceptionHandler.cs
  - apps/api-dotnet/Playdex.Api/Common/GameId.cs
  - apps/api-dotnet/Playdex.Api/Program.cs
findings:
  critical: 4
  warning: 2
  info: 0
  total: 6
status: issues_found
---

# Phase: .NET Migration API Compliance Audit

**Reviewed:** 2026-07-10
**Depth:** deep (cross-file analysis, import graph, call-chain tracing)
**Files Reviewed:** 26 sources + 11 TypeScript reference routers
**Status:** issues_found

## Summary

Audit of the .NET API (`apps/api-dotnet/`) against the TypeScript reference API (`apps/api/src/modules/`) for compliance with spec `002-dotnet-migration`. The audit checks response envelope compliance, route path matching, response shape matching, auth requirements, error handling, and game ID encoding across all 10 module areas.

**All endpoints correctly use `ApiResponse<T>.Ok()` envelopes and throw `AppProblem` for errors.** Route paths match the TypeScript reference exactly across all modules. Auth middleware (`RequireAuthorization()`) is present on all required endpoints, and Playnite correctly uses API-key auth (not JWT). Admin endpoints all verify admin status before proceeding.

**4 critical behavioral bugs found** that will cause client-facing breakage or incorrect data. **2 warnings** for behavioral differences that may cause subtle issues.

---

## Critical Issues

### CR-01: AccountStatsPeriodDto topGames uses wrong game ID — encodes session ID instead of game ID

**File:** `apps/api-dotnet/Playdex.Api/Accounts/AccountsRepository.cs:130`
**Issue:** In `ComputeStats()`, the `topGames` array is built by grouping sessions by game title and constructing `AccountTopGameDto` values. The game ID field is populated with `GameId.Encode(g.First().SessionId)` — this encodes the numeric **session ID**, not the game ID. Since the session ID and game ID are unrelated numbers, the resulting encoded string represents an entirely different (and likely nonexistent) game. The correct value `g.First().GameId` is already an encoded game ID string (populated by `MapSession()` at line 207).

**Impact:** The `topGames` array in account stats responses (`/api/accounts/:id/stats`, `/api/stats`) returns incorrect `gameId` values. Frontend clients will fail to navigate to game details or display wrong game data for stats entries.

**Fix:**
```csharp
// AccountsRepository.cs:128-135 — BEFORE (buggy)
var topGames = sessions
    .GroupBy(s => s.Game)
    .Select(g => new AccountTopGameDto(
        GameId.Encode(g.First().SessionId),  // BUG: encodes session ID
        g.Key,
        g.Sum(MapDuration)))
    .OrderByDescending(g => g.TotalSeconds)
    .Take(5)
    .ToList();

// AFTER (fixed)
var topGames = sessions
    .GroupBy(s => s.Game)
    .Select(g => new AccountTopGameDto(
        g.First().GameId,  // FIX: already-encoded game ID string from MapSession
        g.Key,
        g.Sum(MapDuration)))
    .OrderByDescending(g => g.TotalSeconds)
    .Take(5)
    .ToList();
```

---

### CR-02: RecapResponseDto has flat `from`/`to` fields — should be nested under `range`

**File:** `apps/api-dotnet/Playdex.Api/Recap/RecapDto.cs:43-44`
**Issue:** The TypeScript `RecapResponseDto` contract defines:
```typescript
export type RecapResponseDto = {
  range: { from: string | null; to: string | null };
  totalSessions: number;
  // ...
};
```

The .NET `RecapResponseDto` defines `string? From` and `string? To` as flat top-level fields. After camelCase serialization, the JSON output is:
```json
{ "from": "...", "to": "...", "totalSessions": ..., ... }
```

But the TS contract expects:
```json
{ "range": { "from": "...", "to": "..." }, "totalSessions": ..., ... }
```

**Impact:** Frontend code accessing `response.data.range.from` will receive `undefined`, breaking the recap page entirely.

**Fix:** Add a nested range DTO and update `RecapResponseDto`:
```csharp
// Add new record
public sealed record RecapRangeDto(string? From, string? To);

// Update RecapResponseDto — replace string? From, string? To with:
public sealed record RecapResponseDto(
    RecapRangeDto? Range,
    int TotalSessions,
    // ... rest of fields unchanged
    IReadOnlyList<RecapGameStatDto> GamesTable);
```

And update `RecapService.GetRecap()` (line 432-471) to wrap from/to:
```csharp
return new RecapResponseDto(
    Range: (from is not null || to is not null) ? new RecapRangeDto(from, to) : null,
    TotalSessions: totalSessions,
    // ... rest unchanged
);
```

---

### CR-03: AdminCredentialDto field `AccountUser` serializes as `accountUser` — TS expects `user`

**File:** `apps/api-dotnet/Playdex.Api/Admin/AdminDto.cs:133`
**Issue:** The .NET DTO field `AccountUser` serializes to `accountUser` via camelCase policy. The TypeScript contract defines this field as `user`:

```typescript
// TS contract (apps/api/src/modules/admin/admin.dto.ts:57-64)
export type AdminCredentialDto = {
  // ...
  user: string | null;  // <-- expects "user"
  active: boolean;
};
```

This mismatch affects **both**:
1. **Response:** `GET /api/admin/credentials` and `PUT /api/admin/credentials/:id` return `accountUser` instead of `user`.
2. **Requests:** `PUT /api/admin/credentials/:id` — the TS client sends `{ user: "..." }` which won't bind to `AccountUser` because `PropertyNameCaseInsensitive = false` (Program.cs:65). The value is silently lost.

**Impact:** The `user`/`accountUser` field is invisible to the frontend. Admin UI cannot display or update credential account user values.

**Fix:** Apply `[JsonPropertyName("user")]` to both request and response DTOs:
```csharp
// AdminDto.cs — AdminCredentialDto
public sealed record AdminCredentialDto(
    long Id,
    string UserEmail,
    string Platform,
    string? ApiKey,
    [property: System.Text.Json.Serialization.JsonPropertyName("user")]
    string? AccountUser,
    bool Active);

// AdminCredentialUpdateRequest
public sealed record AdminCredentialUpdateRequest(
    string? ApiKey,
    [property: System.Text.Json.Serialization.JsonPropertyName("user")]
    string? AccountUser);
```

Also update the TS types or rename the DTO field directly to `User` for clarity.

---

### CR-04: Session deletion returns 200 with `sessionId: 0` instead of 404 when session not found

**File:** `apps/api-dotnet/Playdex.Api/Accounts/AccountsService.cs:30-34`
**Issue:** When `DELETE /api/sessions/:sessionId` targets a session that doesn't exist or doesn't belong to the authenticated user, the repository's `ExecuteScalarAsync<long>` returns `0` (Dapper default for no rows). The service wraps this into `AccountSessionDeleteResponseDto(0)` and returns HTTP 200. The TypeScript implementation returns 404:

```typescript
// TS: accounts.handlers.ts:194-209
if (!result.deleted) {
  throw new AppError("Session not found.", 404);
}
```

**Impact:** Client cannot distinguish "session successfully deleted" from "session did not exist." This can cause UI state bugs (e.g., showing a success toast when the session was never found).

**Fix:**
```csharp
// AccountsService.cs:30-34 — BEFORE
public async Task<AccountSessionDeleteResponseDto> DeleteSession(
    long sessionId, long userId, CancellationToken ct)
{
    var deleted = await repository.DeleteSession(sessionId, userId, ct);
    return new AccountSessionDeleteResponseDto(deleted);
}

// AFTER
public async Task<AccountSessionDeleteResponseDto> DeleteSession(
    long sessionId, long userId, CancellationToken ct)
{
    var deleted = await repository.DeleteSession(sessionId, userId, ct);
    if (deleted == 0)
        throw AppProblem.NotFound("Session not found.");
    return new AccountSessionDeleteResponseDto(deleted);
}
```

---

## Warnings

### WR-01: Settings `ignoreShortSessionsMinutes` upper bound differs from TypeScript

**File:** `apps/api-dotnet/Playdex.Api/Settings/SettingsService.cs:14`
**Issue:** The .NET service validates `minutes > 1440` (upper bound of 24 hours), throwing `AppProblem.Validation`. The TypeScript handler (`settings.handlers.ts:14-21`) only checks for non-negative integers with no upper bound. A client that currently stores values like `10080` (1 week) would receive a 400 after migration despite being valid before.

**Impact:** Previously accepted settings values may be rejected after cutover, requiring user retries or migration of stored values.

**Fix:** Either remove the upper bound to match TS behavior, or document this as an intentional tightening and add migration guidance (e.g., cap stored values at 1440 during a data migration). If the bound is kept, the error message should include the max value for clarity.

---

### WR-02: Admin credential update cannot clear `apiKey` or `accountUser` to null via COALESCE

**File:** `apps/api-dotnet/Playdex.Api/Admin/AdminRepository.cs:148-149`
**Issue:** The UPDATE statement uses `COALESCE(@ApiKey, api_key)` and `COALESCE(@AccountUser, account_id)`, which preserves existing values when null is passed. The TypeScript handler supports explicitly setting these fields to null (via `parseNullableString`). With COALESCE, a client request like `{ "user": null }` intending to clear the account user has no effect.

**Impact:** Admins cannot clear credential API keys or account users through the admin panel after migration — the old values persist. This is a partial update regression.

**Fix:** The request DTO should include explicit change-tracking flags (as the TS handler does with `apiKeyChanged`/`userChanged`) or the repository should use a different strategy. Minimal fix:

```csharp
// AdminRepository.cs — use explicit flags
public async Task<AdminCredentialRow?> UpdateCredential(
    long credentialId, bool changeApiKey, string? apiKey,
    bool changeAccountUser, string? accountUser, CancellationToken ct)
{
    // Build SET clause dynamically based on change flags
    // Only apply COALESCE when the field was not explicitly present in the request
}
```

Alternatively, switch to a param-based approach where `null` means "no change" and absence means "set to null," but this requires serialization-level changes.

---

## Verified-Passing Areas

The following areas were thoroughly checked and found compliant:

### 1. Response Envelope Compliance
Every endpoint across all 10 modules returns `ApiResponse<T>.Ok(data)` for success and throws `AppProblem` for errors. No endpoint returns raw `IResult` or plain objects. The `ApiExceptionHandler` correctly serializes errors to `{ data: null, error: { error: "..." } }`. ✓

### 2. Route Path Matching
All .NET routes match their TypeScript counterparts exactly (paths, methods, parameter names):

| Module | .NET Route | TS Route | Match |
|---|---|---|---|
| Auth | POST `/api/login` | POST `/api/login` | ✓ |
| Auth | POST `/api/refresh` | POST `/api/refresh` | ✓ |
| Auth | GET `/api/me` | GET `/api/me` | ✓ |
| Accounts | GET `/api/accounts` | GET `/api/accounts` | ✓ |
| Accounts | GET `/api/sessions` | GET `/api/sessions` | ✓ |
| Accounts | GET `/api/sessions/today` | GET `/api/sessions/today` | ✓ |
| Accounts | GET `/api/sessions/current` | GET `/api/sessions/current` | ✓ |
| Accounts | DELETE `/api/sessions/{sessionId}` | DELETE `/api/sessions/:sessionId` | ✓ |
| Accounts | GET `/api/stats` | GET `/api/stats` | ✓ |
| Accounts (sub) | GET `/api/accounts/{accountId}/sessions` | GET `/api/accounts/:accountId/sessions` | ✓ |
| Accounts (sub) | GET `/api/accounts/{accountId}/sessions/today` | GET `/api/accounts/:accountId/sessions/today` | ✓ |
| Accounts (sub) | GET `/api/accounts/{accountId}/sessions/current` | GET `/api/accounts/:accountId/sessions/current` | ✓ |
| Accounts (sub) | GET `/api/accounts/{accountId}/stats` | GET `/api/accounts/:accountId/stats` | ✓ |
| Settings | GET `/api/settings` | GET `/api/settings` | ✓ |
| Settings | PUT `/api/settings` | PUT `/api/settings` | ✓ |
| Games | GET `/api/games/search` | GET `/api/games/search` | ✓ |
| Games | GET `/api/games/{gameId}` | GET `/api/games/:gameId` | ✓ |
| Games | GET `/api/games/connections` | GET `/api/games/connections` | ✓ |
| Games | POST `/api/games/connections` | POST `/api/games/connections` | ✓ |
| Games | DELETE `/api/games/connections` | DELETE `/api/games/connections` | ✓ |
| Games | GET `/api/games/ignored` | GET `/api/games/ignored` | ✓ |
| Games | POST `/api/games/ignored` | POST `/api/games/ignored` | ✓ |
| Games | DELETE `/api/games/ignored` | DELETE `/api/games/ignored` | ✓ |
| Games | GET `/api/games/rating` | GET `/api/games/rating` | ✓ |
| Games | POST `/api/games/rating` | POST `/api/games/rating` | ✓ |
| Games | DELETE `/api/games/rating` | DELETE `/api/games/rating` | ✓ |
| Manual | GET `/api/manual/inputs` | GET `/api/manual/inputs` | ✓ |
| Manual | POST `/api/manual/sessions/start` | POST `/api/manual/sessions/start` | ✓ |
| Manual | POST `/api/manual/sessions/stop` | POST `/api/manual/sessions/stop` | ✓ |
| Manual | GET `/api/manual/search` | GET `/api/manual/search` | ✓ |
| Manual | POST `/api/manual/entries` | POST `/api/manual/entries` | ✓ |
| Manual | POST `/api/manual/inputs/{inputId}/map` | POST `/api/manual/inputs/:inputId/map` | ✓ |
| Playnite | POST `/api/playnite/sessions` | POST `/api/playnite/sessions` | ✓ |
| Playnite | POST `/api/playnite/sessions/start` | POST `/api/playnite/sessions/start` | ✓ |
| Playnite | POST `/api/playnite/sessions/ping` | POST `/api/playnite/sessions/ping` | ✓ |
| Playnite | POST `/api/playnite/sessions/end` | POST `/api/playnite/sessions/end` | ✓ |
| Providers | GET `/api/providers` | GET `/api/providers` | ✓ |
| Providers | PUT `/api/providers/status` | PUT `/api/providers/status` | ✓ |
| Providers | POST `/api/playnite/key` | POST `/api/playnite/key` | ✓ |
| Admin | GET `/api/admin/sessions/recent` | GET `/api/admin/sessions/recent` | ✓ |
| Admin | GET `/api/admin/sessions` | GET `/api/admin/sessions` | ✓ |
| Admin | PUT `/api/admin/sessions/move` | PUT `/api/admin/sessions/move` | ✓ |
| Admin | GET `/api/admin/providers/platforms` | GET `/api/admin/providers/platforms` | ✓ |
| Admin | PUT `/api/admin/providers/platforms/status` | PUT `/api/admin/providers/platforms/status` | ✓ |
| Admin | GET `/api/admin/credentials` | GET `/api/admin/credentials` | ✓ |
| Admin | PUT `/api/admin/credentials/{credentialId}` | PUT `/api/admin/credentials/:credentialId` | ✓ |
| Admin | GET `/api/admin/games/search` | GET `/api/admin/games/search` | ✓ |
| Admin | PUT `/api/admin/games` | PUT `/api/admin/games` | ✓ |
| Admin | PUT `/api/admin/games/{gameId}` | PUT `/api/admin/games/:gameId` | ✓ |
| Recap | GET `/api/recap/years` | GET `/api/recap/years` | ✓ |
| Recap | GET `/api/recap` | GET `/api/recap` | ✓ |
| Metrics | GET `/api/metrics` | GET `/api/metrics` | ✓ |

### 3. Auth Requirements
- All account, settings, games, manual, providers, admin, recap, and metrics endpoints call `RequireAuthorization()`. ✓
- Playnite endpoints do NOT use `RequireAuthorization()` and extract API keys from `Authorization: Bearer <key>` header manually (`PlayniteEndpoints.cs:104-122`). ✓
- Admin endpoints all verify admin status via `AssertAdmin()` before performing operations. ✓
- JWT authentication correctly validates `"type": "access"` claim (`AuthEndpoints.cs:34-40`). ✓

### 4. Game ID Encoding
- **Games module:** `GamesRepository` correctly uses `GameId.Encode()` for all response DTOs (search results, game detail, connections). `GamesService` correctly uses `GameId.Parse()` for all request parsing. ✓
- **Accounts module:** `MapSession()` correctly encodes session game IDs (`AccountsRepository.cs:207`). ✓ (Except CR-01 bug in topGames)
- **Admin module:** `AdminService` correctly encodes game IDs via `GameId.Encode()` in `SearchGames` and `UpdateGame`. ✓
- **Recap module:** `RecapService` correctly encodes all game IDs via `GameId.Encode()` in group totals, top games, longest sessions, and binged games. ✓
- **Manual module:** `ManualService` correctly encodes game IDs in `ListInputs`, `SearchMapping`, and `MapInput`; correctly decodes via `GameId.Parse()` in `CreateEntry` and `MapInput`. ✓
- **Playnite module:** No game IDs exposed in response DTOs (session IDs only). ✓

### 5. Error Handling
- `AppProblem` factory methods map correctly: `Validation` → 400, `Unauthorized` → 401, `Forbidden` → 403, `NotFound` → 404, `Conflict` → 409. ✓
- `ApiExceptionHandler` produces `{ data: null, error: { error: "..." } }` for all exception types. ✓
- Route-not-found fallback throws `AppProblem.NotFound()` (`Program.cs:141`). ✓

### 6. Response Shapes (excluding CR-02 and CR-03)
All other DTO field names and types match between .NET camelCase and TypeScript:

| DTO | Fields | Match |
|---|---|---|
| AccountSummaryDto | `id`, `name` | ✓ |
| AccountSessionDto | `sessionId`, `startedAt`, `endedAt`, `gameId`, `platform`, `game`, `capsuleUrl`, `ignored`, `mappedPlatform`, `mappedSource` | ✓ |
| AccountSessionsResponseDto | `accountId`, `sessions`, `total`, `limit`, `offset` | ✓ |
| AccountStatsPeriodDto | `totalSeconds`, `sessionCount`, `topGameTitle`, `platforms[]`, `topGames[]` | ✓ |
| GameSearchResultDto | `gameId`, `title`, `platform`, `capsuleUrl` | ✓ |
| GameSearchResponseDto | `query`, `results` | ✓ |
| GameResponseDto | `id`, `title`, `platform`, `capsuleUrl` | ✓ |
| GameConnectionDto | `gameId`, `title`, `platform`, `capsuleUrl`, `connectedGameId`, `connectedTitle`, `connectedPlatform`, `connectedCapsuleUrl` | ✓ |
| GameIgnoreResponseDto | `gameId`, `ignored` | ✓ |
| GameRatingResponseDto | `gameId`, `rating` | ✓ |
| UserSettingsResponseDto | `ignoreShortSessionsMinutes` | ✓ |
| PlayniteSessionResponseDto | `sessionId` | ✓ |
| PlayniteSessionStartResponseDto | `sessionId` | ✓ |
| PlayniteSessionEndResponseDto | `endedSessionCount` | ✓ |
| PlayniteSessionPingResponseDto | `sessionId`, `started`, `endedSessionCount` | ✓ |
| ProvidersResponseDto | `providers` | ✓ |
| ProviderStatusDto | `id`, `platform`, `accountId`, `status`, `apiKey` | ✓ |
| PlayniteKeyResponseDto | `apiKey` | ✓ |
| AdminRecentSessionsResponseDto | `users[].userId`, `users[].email`, `users[].sessions[]` | ✓ |
| AdminSessionsListResponseDto | `query`, `sessions`, `total`, `limit`, `offset` | ✓ |
| AdminSessionMoveResponseDto | `movedSessions`, `targetCredentialId` | ✓ |
| AdminPlatformStatusDto | `platform`, `totalCredentials`, `activeCredentials`, `enabled` | ✓ |
| AdminPlatformStatusUpdateResponseDto | `platform`, `enabled`, `updatedCredentials` | ✓ |
| AdminGameDto | `id`, `title`, `titleLocked`, `platform`, `imageUrl`, `imageLocked` | ✓ |
| AdminGameSearchResponseDto | `query`, `games` | ✓ |
| ManualInputsResponseDto | `inputs` | ✓ |
| ManualSessionStartResponseDto | `sessionId` | ✓ |
| ManualSessionStopResponseDto | `endedSessionCount` | ✓ |
| ManualMappingSearchResponseDto | `query`, `igdbAvailable`, `results` | ✓ |
| ManualMappingResponseDto | `inputId`, `mappedGameId` | ✓ |
| ManualEntryCreateResponseDto | `sessionId` | ✓ |
| RecapYearsResponseDto | `years` | ✓ |
| RecapGameStatDto | `gameId`, `title`, `capsuleUrl`, `totalSeconds`, `sessionCount`, `averageSessionSeconds`, `isAbandoned` | ✓ |
| MetricsSnapshotDto | `pollSuccessTotal`, `pollFailureTotal`, `pollSuccessByPlatform`, `pollFailureByPlatform`, `lastPollSuccessAt`, `lastPollFailureAt`, `lastCycleDurationMs` | ✓ |

---

_Reviewed: 2026-07-10T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
