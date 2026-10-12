using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Api.Infrastructure.Http;
using Playdex.Persistence;

namespace Api.Modules.Admin;

public sealed partial class AdminService(
    AdminRepository repository,
    TimeProvider clock,
    ILogger<AdminService> logger)
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public Task<AdminDashboardDto> ReadDashboard(string? query, string? platform, DateTimeOffset? dayStart,
        DateTimeOffset? dayEnd, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var start = dayStart ?? new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var end = dayEnd ?? start.AddDays(1);
        if (end <= start) throw AppProblemException.Validation("dayEnd must be after dayStart.");
        return repository.ReadDashboard(query ?? "", string.IsNullOrWhiteSpace(platform) ? null : platform,
            start, end, now, ct);
    }

    public Task<IReadOnlyList<AdminPlatformSummaryDto>> ListPlatforms(CancellationToken ct) =>
        repository.ListPlatformSummaries(ct);

    public async Task<AdminPlatformUpdateResultDto> SetPlatformEnabled(string platform, bool enabled,
        CancellationToken ct)
    {
        var result = await repository.SetPlatformEnabled(platform, enabled, ct)
                     ?? throw AppProblemException.NotFound("Platform not found.");
        var correlationId = CorrelationId();
        LogPlatformMutation(logger, "set-platform-enabled", result.Key, result.Count, enabled, correlationId);
        return new AdminPlatformUpdateResultDto(result.Count);
    }

    public Task<IReadOnlyList<AdminIntegrationDto>> ListIntegrations(CancellationToken ct) =>
        repository.ListIntegrations(ct);

    public async Task UpdateIntegration(long id, AdminUpdateIntegrationRequest request, CancellationToken ct)
    {
        if (!request.ApiKeyChanged && !request.AccountIdChanged)
            throw AppProblemException.Validation("At least one credential field must be selected.");
        var updated = await repository.UpdateIntegration(id, request.ApiKeyChanged, NormalizeOptional(request.ApiKey),
            request.AccountIdChanged, NormalizeOptional(request.AccountId), ct);
        if (!updated) throw AppProblemException.NotFound("Integration not found.");
        var correlationId = CorrelationId();
        LogIntegrationMutation(logger, "update-integration", id, correlationId);
    }

    public Task<AdminSessionPageDto> SearchSessions(string? query, int? page, string? status,
        DateTimeOffset? startedFrom, DateTimeOffset? startedBefore, CancellationToken ct) =>
        repository.SearchSessions(query ?? "", page ?? 0, status, startedFrom, startedBefore, ct);

    public async Task<AdminTransferSessionsResultDto> TransferSessions(AdminTransferSessionsRequest request,
        CancellationToken ct)
    {
        var sessionIds = request.SessionIds?.Distinct().ToArray() ?? [];
        if (sessionIds.Length == 0) throw AppProblemException.Validation("At least one session is required.");
        if (request.TargetIntegrationId is not { } targetId)
            throw AppProblemException.Validation("A target integration is required.");
        var target = await repository.FindTransferTarget(targetId, ct)
                     ?? throw AppProblemException.NotFound("Target integration not found.");
        if (await repository.AnyManualSessions(sessionIds, ct))
            throw AppProblemException.Validation(
                "Manual sessions are not linked to an integration and cannot be transferred. Deselect them and try again.");
        var updated = await repository.TransferSessions(sessionIds, target, ct);
        var correlationId = CorrelationId();
        LogSessionTransfer(logger, "transfer-sessions", targetId, updated, correlationId);
        return new AdminTransferSessionsResultDto(updated);
    }

    /// <summary>
    /// Delete the selected sessions, of any kind (polled, captured, manual, running); stats and recaps no longer count
    /// them. If the game of a deleted running polled session is still running, the next poll starts a new session.
    /// </summary>
    public async Task<AdminDeleteSessionsResultDto> DeleteSessions(AdminDeleteSessionsRequest request, CancellationToken ct)
    {
        var sessionIds = request.SessionIds?.Distinct().ToArray() ?? [];
        if (sessionIds.Length == 0) throw AppProblemException.Validation("At least one session is required.");
        var deleted = await repository.DeleteSessions(sessionIds, ct);
        var correlationId = CorrelationId();
        LogSessionDelete(logger, "delete-sessions", sessionIds.Length, deleted, correlationId);
        return new AdminDeleteSessionsResultDto(deleted);
    }

    public async Task<IReadOnlyList<AdminGameDto>> SearchGames(string? query, CancellationToken ct)
    {
        var term = query?.Trim() ?? "";
        return term.Length == 0 ? await repository.ListGames(ct) : await repository.SearchGames(term, ct);
    }

    public async Task UpdateGame(long id, AdminUpdateGameRequest request, CancellationToken ct)
    {
        if (!request.TitleChanged && !request.CapsuleUrlChanged && !request.TitleLocked.HasValue &&
            !request.CapsuleUrlLocked.HasValue)
            throw AppProblemException.Validation("At least one game field is required.");
        string? normalizedTitle = null;
        if (request.TitleChanged)
        {
            normalizedTitle = request.Title?.Trim();
            if (string.IsNullOrEmpty(normalizedTitle)) throw AppProblemException.Validation("Title is required.");
        }

        var game = await repository.FindGameForUpdate(id, ct)
                   ?? throw AppProblemException.NotFound("Game not found.");
        ApplyGameUpdate(game, request, normalizedTitle);
        game.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.SaveChanges(ct);
        var correlationId = CorrelationId();
        LogGameMutation(logger, "update-game", id, correlationId);
    }

    private static void ApplyGameUpdate(Game game, AdminUpdateGameRequest request, string? normalizedTitle)
    {
        if (request.TitleChanged)
        {
            game.Title = normalizedTitle!;
            if (!request.TitleLocked.HasValue) game.TitleLocked = true;
        }

        if (request.CapsuleUrlChanged)
        {
            game.CapsuleUrl = string.IsNullOrWhiteSpace(request.CapsuleUrl) ? null : request.CapsuleUrl.Trim();
            if (!request.CapsuleUrlLocked.HasValue) game.CapsuleUrlLocked = true;
        }

        if (request.TitleLocked.HasValue) game.TitleLocked = request.TitleLocked.Value;
        if (request.CapsuleUrlLocked.HasValue) game.CapsuleUrlLocked = request.CapsuleUrlLocked.Value;
    }

    public async Task MergeGames(AdminMergeGamesRequest request, CancellationToken ct)
    {
        var sourceId = request.SourceId ?? 0;
        var targetId = request.TargetId ?? 0;
        if (sourceId <= 0 || targetId <= 0 || sourceId == targetId)
            throw AppProblemException.Validation("Source and target games must be distinct positive ids.");
        var missing = await repository.MergeGames(sourceId, targetId, ct);
        if (missing is not null) throw AppProblemException.NotFound(missing);
        var correlationId = CorrelationId();
        LogGameMerge(logger, "merge-games", sourceId, targetId, correlationId);
    }

    public Task<AdminGenerationPageDto> SearchGenerations(string? query, string? status, int? page,
        DateTimeOffset? since, DateTimeOffset? until, CancellationToken ct) =>
        repository.SearchGenerations(query ?? "", status, page ?? 0, since, until, ct);

    public async Task<AdminGenerationDetailDto> GetGeneration(Guid id, CancellationToken ct)
    {
        var record = await repository.FindGeneration(id, ct)
                     ?? throw AppProblemException.NotFound("This generation has been deleted or expired.");
        return ToDetail(record);
    }

    public async Task<AdminGenerationImage> GetGenerationImage(Guid id, CancellationToken ct) =>
        await repository.FindGenerationImage(id, ct) ?? throw AppProblemException.NotFound("Screenshot not found.");

    public Task<bool> DeleteGeneration(Guid id, CancellationToken ct) => repository.DeleteGeneration(id, ct);

    public static AdminGenerationDetailDto ToDetail(AdminGenerationRecord record)
    {
        var generation = record.Generation;
        return new AdminGenerationDetailDto(
            generation.Id,
            record.Email,
            generation.RequestId,
            generation.CreatedAt,
            generation.CompletedAt,
            generation.Provider,
            generation.Model,
            generation.Timezone,
            generation.ReferenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            generation.InputText,
            generation.ImageContentType is not null,
            generation.Status,
            generation.Error,
            generation.ResultJson is null ? null : Pretty(generation.ResultJson),
            ParseRows(generation.ResultJson),
            ParseResponses(generation.ResponsesJson));
    }

    private static List<AdminGenerationResultRowDto> ParseRows(string? resultJson)
    {
        if (resultJson is null) return [];
        try
        {
            var snapshot = JsonSerializer.Deserialize<ResultSnapshot>(resultJson);
            return snapshot?.Rows?.Select(row => new AdminGenerationResultRowDto(row.Title ?? "", row.Date,
                row.DurationMinutes, row.PlatformKey ?? "")).ToList() ?? [];
        }
        catch (JsonException) { return []; }
    }

    private static List<AdminGenerationResponseDto> ParseResponses(string responsesJson)
    {
        try
        {
            var responses = JsonSerializer.Deserialize<List<ResponseBody>>(responsesJson) ?? [];
            return responses.Select(response =>
                new AdminGenerationResponseDto(response.HttpStatus, Pretty(response.Body ?? ""), response.Truncated))
                .ToList();
        }
        catch (JsonException) { return []; }
    }

    private static string Pretty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, PrettyJson);
        }
        catch (JsonException) { return json; }
    }

    private sealed record ResponseBody(int HttpStatus, string? Body, bool Truncated);

    private sealed record ResultRow(string? Title, string? Date, int? DurationMinutes, string? PlatformKey);

    private sealed record ResultSnapshot(IReadOnlyList<ResultRow>? Rows);

    private static string CorrelationId() => Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Admin mutation {Operation} platform {Platform} count {Count} enabled {Enabled} correlation {CorrelationId}")]
    private static partial void LogPlatformMutation(ILogger logger, string operation, string platform, int count,
        bool enabled, string correlationId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Admin mutation {Operation} integration {IntegrationId} correlation {CorrelationId}")]
    private static partial void LogIntegrationMutation(ILogger logger, string operation, long integrationId,
        string correlationId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Admin mutation {Operation} integration {IntegrationId} count {Count} correlation {CorrelationId}")]
    private static partial void LogSessionTransfer(ILogger logger, string operation, long integrationId, int count,
        string correlationId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Admin mutation {Operation} requested {Requested} deleted {Count} correlation {CorrelationId}")]
    private static partial void LogSessionDelete(ILogger logger, string operation, int requested, int count,
        string correlationId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Admin mutation {Operation} game {GameId} correlation {CorrelationId}")]
    private static partial void LogGameMutation(ILogger logger, string operation, long gameId, string correlationId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Admin mutation {Operation} source {SourceGameId} target {TargetGameId} correlation {CorrelationId}")]
    private static partial void LogGameMerge(ILogger logger, string operation, long sourceGameId, long targetGameId,
        string correlationId);
}
