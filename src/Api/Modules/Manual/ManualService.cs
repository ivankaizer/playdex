using System.Globalization;
using Api.Infrastructure.Common;
using Api.Modules.Games;
using Api.Infrastructure.Http;
using Api.Modules.Polling;
using Microsoft.Extensions.Options;
using Playdex.Persistence;

namespace Api.Modules.Manual;

public sealed class ManualService(
    ManualRepository repository,
    IgdbClient igdbClient,
    IOptions<IgdbOptions> igdbOptions,
    IGameResolver gameResolver)
{
    private const int MaxLocalSearchResults = 5;
    /// <summary>
    /// Start a new manual tracking session for a saved game or verified IGDB selection.
    /// Ends any active manual sessions first, then creates a new one.
    /// </summary>
    public async Task<ManualSessionStartResponseDto> StartSession(
        long userId, ManualSessionStartRequest request, CancellationToken ct)
    {
        var selection = ValidateGameSelection(
            request.GameId,
            request.IgdbId);
        var platformKey = RequirePlatform(request.PlatformKey);
        var resolvedSelection = await ResolveAuthoritativeSelection(selection, ct);

        return await repository.ExecuteInTransaction(async () =>
        {
            var startedAt = DateTimeOffset.UtcNow;

            await repository.AcquireUserStartLock(userId, ct);
            if (resolvedSelection.GameId.HasValue)
            {
                RequireExistingGame(await repository.GetGameEntity(resolvedSelection.GameId.Value, ct));
            }
            await repository.EndActiveSessions(userId, ct);
            var gameId = await ResolveGame(resolvedSelection, ct);
            var platformId = await repository.GetPlatformIdByKey(platformKey, ct)
                ?? throw AppProblemException.Validation("Platform not found.");
            var sessionId = await repository.CreatePlaySession(
                userId, gameId, platformId, startedAt, null, ct);

            return new ManualSessionStartResponseDto(sessionId);
        }, ct);
    }

    /// <summary>
    /// Stop all active manual tracking sessions for the current user.
    /// </summary>
    public Task<ManualSessionStopResponseDto> StopSession(long userId, CancellationToken ct) =>
        repository.ExecuteInTransaction(async () =>
        {
            await repository.AcquireUserStartLock(userId, ct);
            return new ManualSessionStopResponseDto(
                await repository.EndActiveSessions(userId, ct));
        }, ct);

    public async Task<ManualPlatformsResponseDto> ListPlatforms(CancellationToken ct) =>
        BuildPlatformCatalog(await repository.ListPlatforms(ct));

    public async Task<ManualActiveSessionResponseDto> GetCurrentSession(
        long userId,
        CancellationToken ct) =>
        ToActiveSessionResponse(await repository.LoadActiveSession(userId, ct));

    /// <summary>
    /// Create a historical manual play session entry.
    /// When no gameId is provided, the service verifies and resolves the IGDB selection.
    /// </summary>
    public async Task<ManualEntryCreateResponseDto> CreateEntry(
        long userId, ManualEntryCreateRequest request, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(
                request.Date?.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
            throw AppProblemException.Validation("date is required.");
        var startTime = new DateTimeOffset(DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc));

        if (request.DurationMinutes <= 0)
            throw AppProblemException.Validation("durationMinutes must be a positive integer.");

        var endTime = startTime.AddMinutes(request.DurationMinutes);
        var selection = ValidateGameSelection(
            request.GameId,
            request.IgdbId);
        var platformKey = RequirePlatform(request.PlatformKey);
        var resolvedSelection = await ResolveAuthoritativeSelection(selection, ct);
        return await repository.ExecuteInTransaction(async () =>
        {
            if (resolvedSelection.GameId.HasValue)
            {
                RequireExistingGame(await repository.GetGameEntity(resolvedSelection.GameId.Value, ct));
            }

            var gameId = await ResolveGame(resolvedSelection, ct);
            var platformId = await repository.GetPlatformIdByKey(platformKey, ct)
                ?? throw AppProblemException.Validation("Platform not found.");
            var sessionId = await repository.CreatePlaySession(
                userId, gameId, platformId, startTime, endTime, ct);
            return new ManualEntryCreateResponseDto(sessionId);
        }, ct);
    }

    /// <summary>
    /// Search for possible game mappings in the local database and IGDB.
    /// </summary>
    public async Task<ManualMappingSearchResponseDto> SearchMapping(
        string query, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
            throw AppProblemException.Validation("query is required.");

        var opts = igdbOptions.Value;
        var igdbConfigured = !string.IsNullOrWhiteSpace(opts.ClientId) &&
            !string.IsNullOrWhiteSpace(opts.ClientSecret);
        var localTask = repository.SearchLocalGames(trimmed, MaxLocalSearchResults, ct);
        var igdbTask = igdbConfigured
            ? SearchIgdbSafely(() => igdbClient.SearchGames(trimmed, ct), ct)
            : Task.FromResult(new IgdbSearchAttempt(false, []));
        await Task.WhenAll(localTask, igdbTask);
        var igdb = igdbTask.Result;

        var results = localTask.Result.Select(r => new ManualMappingSearchResultDto(
            Source: "local",
            GameId: GameId.Encode(r.Id),
            IgdbId: null,
            Title: r.Title,
            Platform: r.Platform,
            PlatformId: null,
            CapsuleUrl: r.CapsuleUrl
        )).Concat(igdb.Results.Select(result => new ManualMappingSearchResultDto(
            Source: "igdb",
            GameId: null,
            IgdbId: result.Id,
            Title: result.Title,
            Platform: result.Platform,
            PlatformId: result.PlatformId,
            CapsuleUrl: result.CoverUrl
        ))).ToList();

        return new ManualMappingSearchResponseDto(trimmed, igdb.Available, results);
    }

    internal static ValidatedManualGameSelection ValidateGameSelection(
        string? gameId,
        string? igdbId)
    {
        if (!string.IsNullOrWhiteSpace(gameId))
        {
            return new ValidatedManualGameSelection(GameId.Parse(gameId), null);
        }

        var normalizedIgdbId = igdbId?.Trim();
        if (string.IsNullOrEmpty(normalizedIgdbId))
        {
            throw AppProblemException.Validation("Select a saved game or an IGDB result.");
        }

        return new ValidatedManualGameSelection(null, normalizedIgdbId);
    }

    internal static ManualPlatformsResponseDto BuildPlatformCatalog(
        IEnumerable<Playdex.Persistence.Platform> platforms) =>
        new(platforms
            .Where(platform => platform.Key != "playnite")
            .OrderBy(platform => platform.Name)
            .ThenBy(platform => platform.Key)
            .Select(platform => new ManualPlatformDto(platform.Key, platform.Name))
            .ToArray());

    internal static ManualActiveSessionResponseDto ToActiveSessionResponse(
        ManualActiveSessionRow? row) =>
        new(row is null
            ? null
            : new ManualActiveSessionDto(
                row.SessionId,
                GameId.Encode(row.GameId),
                row.Title,
                row.PlatformKey,
                row.PlatformName,
                row.StartedAt,
                row.CapsuleUrl));

    internal static Game RequireExistingGame(Game? game) =>
        game ?? throw AppProblemException.Validation("Game not found for the specified gameId.");

    internal static async Task<IgdbSearchAttempt> SearchIgdbSafely(
        Func<Task<List<IgdbSearchResult>>> search,
        CancellationToken cancellationToken)
    {
        try
        {
            return new IgdbSearchAttempt(true, await search());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new IgdbSearchAttempt(false, []);
        }
    }

    private static string RequirePlatform(string? platformKey)
    {
        var normalized = platformKey?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            throw AppProblemException.Validation("platformKey is required.");
        return normalized;
    }

    private async Task<ResolvedManualGameSelection> ResolveAuthoritativeSelection(
        ValidatedManualGameSelection selection,
        CancellationToken ct)
    {
        if (selection.GameId.HasValue)
            return new ResolvedManualGameSelection(selection.GameId, null);

        var authoritative = await igdbClient.GetGameById(selection.IgdbId!, ct);
        if (authoritative is null)
            throw AppProblemException.Validation("IGDB game not found.");

        return new ResolvedManualGameSelection(
            null,
            new GameResolutionRequest(
                GameExternalIdentityTypes.FromKey("igdb"),
                authoritative.Id,
                authoritative.Title,
                authoritative.CoverUrl));
    }

    private async Task<long> ResolveGame(
        ResolvedManualGameSelection selection,
        CancellationToken ct) =>
        selection.GameId ?? (await gameResolver.Resolve(selection.IgdbResolution!, ct)).GameId;
}
