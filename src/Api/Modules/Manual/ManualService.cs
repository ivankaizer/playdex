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
    /// Playnite sessions come only from the Playnite integration, so the manual catalog and AI drafts leave it out.
    /// </summary>
    internal const string PlaynitePlatformKey = "playnite";
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
        var (startTime, endTime) = ValidateEntryTimes(request, DateTimeOffset.UtcNow);
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

    /// <summary>
    /// A historical entry starts when its date begins in the client's time zone (UTC when it sent none), lasts at
    /// most 24 hours and cannot be on a day that has not begun yet.
    /// </summary>
    internal static (DateTimeOffset Start, DateTimeOffset End) ValidateEntryTimes(
        ManualEntryCreateRequest request, DateTimeOffset now)
    {
        if (!DateOnly.TryParseExact(
                request.Date?.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var day))
            throw AppProblemException.Validation("date is required.");

        if (request.DurationMinutes <= 0)
            throw AppProblemException.Validation("durationMinutes must be a positive integer.");
        if (request.DurationMinutes > SessionLimits.MaxDurationMinutes)
            throw AppProblemException.Validation(
                $"durationMinutes must be at most {SessionLimits.MaxDurationMinutes} (24 hours).");

        var zone = LocalCalendar.FindTimeZone(request.TimeZone);
        if (day > SessionLimits.LatestDate(now, zone))
            throw AppProblemException.Validation("date cannot be in the future.");

        try
        {
            var start = LocalCalendar.StartOf(day, zone ?? TimeZoneInfo.Utc);
            return (start, start.AddMinutes(request.DurationMinutes));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw AppProblemException.Validation("date is required.");
        }
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
            .Where(platform => platform.Key != PlaynitePlatformKey)
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

    internal async Task<ResolvedManualGameSelection> ResolveAuthoritativeSelection(
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
                authoritative.CoverUrl,
                TitleIsAuthoritative: true));
    }

    internal async Task<long> ResolveGame(
        ResolvedManualGameSelection selection,
        CancellationToken ct) =>
        selection.GameId ?? (await gameResolver.Resolve(selection.IgdbResolution!, ct)).GameId;
}
