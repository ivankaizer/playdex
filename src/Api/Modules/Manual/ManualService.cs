using System.Globalization;
using Api.Infrastructure.Common;
using Api.Modules.Games;
using Api.Infrastructure.Http;
using Api.Modules.Polling;
using Api.Modules.Sessions;
using Microsoft.Extensions.Options;
using Playdex.Persistence;

namespace Api.Modules.Manual;

public sealed class ManualService(
    ManualRepository repository,
    IgdbClient igdbClient,
    IOptions<IgdbOptions> igdbOptions,
    IGameResolver gameResolver,
    SessionLifecycle sessions)
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

            await sessions.LockManualTimer(userId, ct);
            if (resolvedSelection.GameId.HasValue)
            {
                RequireExistingGame(await repository.GetGameEntity(resolvedSelection.GameId.Value, ct));
            }
            var gameId = await ResolveGame(resolvedSelection, ct);
            var platformId = await repository.GetPlatformIdByKey(platformKey, ct)
                ?? throw AppProblemException.Validation("Platform not found.");
            var sessionId = await sessions.StartManualTimer(userId, gameId, platformId, startedAt, ct);

            return new ManualSessionStartResponseDto(sessionId);
        }, ct);
    }

    /// <summary>
    /// Stop all active manual tracking sessions for the current user.
    /// </summary>
    public Task<ManualSessionStopResponseDto> StopSession(long userId, CancellationToken ct) =>
        repository.ExecuteInTransaction(async () =>
        {
            await sessions.LockManualTimer(userId, ct);
            return new ManualSessionStopResponseDto(
                await sessions.StopManualTimers(userId, DateTimeOffset.UtcNow, ct));
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
        var entry = ValidateEntryTimes(request, DateTimeOffset.UtcNow);
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
            var (startTime, endTime) = await PlaceEntry(userId, platformId, entry, ct);
            var sessionId = await repository.CreatePlaySession(
                userId, gameId, platformId, startTime, endTime, ct);
            return new ManualEntryCreateResponseDto(sessionId);
        }, ct);
    }

    /// <summary>
    /// When a dated entry (or accepted draft row) is played. It has no time of day, so it takes the first gap of its
    /// day on that platform, from midnight, that its whole length fits in: an empty day starts it at midnight, and a
    /// second entry follows the first instead of overlapping it. A day with no such gap is refused with 409. Holds the
    /// user's session lock, so call it inside the transaction that creates the session.
    /// </summary>
    internal async Task<(DateTimeOffset Start, DateTimeOffset End)> PlaceEntry(
        long userId, long platformId, ManualEntryDay entry, CancellationToken ct)
    {
        await repository.AcquireUserStartLock(userId, ct);
        var now = DateTimeOffset.UtcNow;
        var taken = await repository.ListPlatformSessions(userId, platformId, entry.DayStart, entry.DayEnd, now, ct);
        var start = SessionOverlaps.FirstFreeStart(taken, entry.DayStart, entry.DayEnd, entry.Duration, now);
        if (start is null)
        {
            var date = entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var length = SessionOverlaps.FormatDuration(entry.Duration);
            throw AppProblemException.Conflict(taken.Count == 0
                ? $"{date} is shorter than {length} in your time zone."
                : $"{date} has no {length} free on {taken[0].Platform}: an entry that long would overlap the "
                    + "sessions already logged there.");
        }
        return (start.Value, start.Value + entry.Duration);
    }

    /// <summary>
    /// Search for possible game mappings in the local database and IGDB.
    /// </summary>
    public async Task<ManualMappingSearchResponseDto> SearchMapping(
        string query, CancellationToken ct)
    {
        var trimmed = SearchText.Bounded(query);
        if (trimmed.Length == 0)
            throw AppProblemException.Validation("query is required.");

        var localTask = SearchLocal(trimmed, ct);
        var igdbTask = SearchIgdb(trimmed, ct);
        await Task.WhenAll(localTask, igdbTask);
        var igdb = igdbTask.Result;

        var results = localTask.Result.Concat(igdb.Results.Select(IgdbResult)).ToList();

        return new ManualMappingSearchResponseDto(trimmed, igdb.Available, results);
    }

    internal async Task<IReadOnlyList<ManualMappingSearchResultDto>> SearchLocal(string query, CancellationToken ct) =>
        (await repository.SearchLocalGames(query, MaxLocalSearchResults, ct)).Select(r => new ManualMappingSearchResultDto(
            Source: "local",
            GameId: GameId.Encode(r.Id),
            IgdbId: null,
            Title: r.Title,
            Platform: r.Platform,
            PlatformId: null,
            CapsuleUrl: r.CapsuleUrl)).ToList();

    /// <summary>
    /// IGDB only, no database access, so it may run in parallel with other searches. Failures report IGDB as
    /// unavailable; cancellation of <paramref name="ct"/> propagates.
    /// </summary>
    internal Task<IgdbSearchAttempt> SearchIgdb(string query, CancellationToken ct)
    {
        var opts = igdbOptions.Value;
        var igdbConfigured = !string.IsNullOrWhiteSpace(opts.ClientId) &&
            !string.IsNullOrWhiteSpace(opts.ClientSecret);
        return igdbConfigured
            ? SearchIgdbSafely(() => igdbClient.SearchGames(query, ct), ct)
            : Task.FromResult(new IgdbSearchAttempt(false, []));
    }

    internal static ManualMappingSearchResultDto IgdbResult(IgdbSearchResult result) => new(
        Source: "igdb",
        GameId: null,
        IgdbId: result.Id,
        Title: result.Title,
        Platform: result.Platform,
        PlatformId: result.PlatformId,
        CapsuleUrl: result.CoverUrl);

    /// <summary>
    /// A historical entry is played on its date in the client's time zone (UTC when it sent none), lasts at most 24
    /// hours and cannot be on a day that has not begun yet. <see cref="PlaceEntry"/> picks its start.
    /// </summary>
    internal static ManualEntryDay ValidateEntryTimes(
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
            return ManualEntryDay.Of(day, zone, TimeSpan.FromMinutes(request.DurationMinutes));
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
