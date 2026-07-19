using System.Globalization;
using Playdex.Api.Shared.Common;
using Playdex.Api.Modules.Games;
using Playdex.Api.Shared.Http;
using Playdex.Api.Modules.Polling;

namespace Playdex.Api.Modules.Manual;

public sealed class ManualService(
    ManualRepository repository,
    IgdbClient igdbClient,
    PlaydexOptions options,
    IGames games)
{
    private const int MaxLocalSearchResults = 5;
    private const string ManualPlatformKey = "manual";

    /// <summary>
    /// List all manual game inputs for the current user, including any
    /// mapped-game information.
    /// </summary>
    public async Task<ManualInputsResponseDto> ListInputs(long userId, CancellationToken ct)
    {
        var rows = await repository.ListManualInputs(userId, ct);

        var inputs = rows.Select(row =>
        {
            ManualGameDto? mappedGame = null;
            if (row.GameId.HasValue && row.GameTitle is not null && row.Platform is not null)
            {
                mappedGame = new ManualGameDto(
                    GameId.Encode(row.GameId.Value),
                    row.GameTitle,
                    row.Platform,
                    row.CapsuleUrl);
            }

            return new ManualInputDto(
                row.Id,
                row.RawTitle,
                row.CanonicalTitle,
                row.MappedGameId.HasValue ? GameId.Encode(row.MappedGameId.Value) : null,
                row.MappedGameId.HasValue
                    ? row.CanonicalKey?.StartsWith("igdb:", StringComparison.Ordinal) == true
                        ? "igdb"
                        : "local"
                    : null,
                mappedGame);
        }).ToList();

        return new ManualInputsResponseDto(inputs);
    }

    /// <summary>
    /// Start a new manual tracking session for the given game title.
    /// Ends any active manual sessions first, then creates a new one.
    /// </summary>
    public async Task<ManualSessionStartResponseDto> StartSession(
        long userId, ManualSessionStartRequest request, CancellationToken ct)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw AppProblem.Validation("Title is required.");

        return await repository.ExecuteInTransaction(async () =>
        {
            var credentialId = await repository.GetOrCreateManualCredential(userId, ct);
            var canonicalTitle = games.NormalizeTitle(title);
            var manualInput = await repository.UpsertManualGameInput(userId, title, canonicalTitle, ct);
            var startedAt = DateTimeOffset.UtcNow;

            await repository.EndActiveSessions(credentialId, ct);
            var gameId = manualInput.MappedGameId ??
                await ResolveOrCreateGame(title, ManualPlatformKey, null, ct);
            var sessionId = await repository.CreatePlaySession(
                credentialId, gameId, startedAt, null, ct);

            return new ManualSessionStartResponseDto(sessionId);
        }, ct);
    }

    /// <summary>
    /// Stop all active manual tracking sessions for the current user.
    /// </summary>
    public async Task<ManualSessionStopResponseDto> StopSession(long userId, CancellationToken ct)
    {
        var credentialId = await repository.GetManualCredential(userId, ct);
        if (!credentialId.HasValue) return new ManualSessionStopResponseDto(0);
        var count = await repository.EndActiveSessions(credentialId.Value, ct);

        return new ManualSessionStopResponseDto(count);
    }

    /// <summary>
    /// Create a historical manual play session entry.
    /// When no gameId is provided, the service resolves or creates a game
    /// from the supplied title / platformKey / capsuleUrl fields.
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
            throw AppProblem.Validation("date is required.");
        var startTime = new DateTimeOffset(DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc));

        if (request.DurationMinutes <= 0)
            throw AppProblem.Validation("durationMinutes must be a positive integer.");

        var endTime = startTime.AddMinutes(request.DurationMinutes);
        return await repository.ExecuteInTransaction(async () =>
        {
            var credentialId = await repository.GetOrCreateManualCredential(userId, ct);
            long gameId;

            if (!string.IsNullOrWhiteSpace(request.GameId))
            {
                gameId = GameId.Parse(request.GameId);
                var game = await repository.GetGameEntity(gameId, ct);
                if (game is null)
                    throw AppProblem.Validation("Game not found for the specified gameId.");
            }
            else
            {
                var igdbId = request.IgdbId?.Trim();
                var title = request.Title?.Trim();
                var platformKey = request.PlatformKey?.Trim();
                if (string.IsNullOrEmpty(igdbId) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(platformKey))
                    throw AppProblem.Validation("igdbId, title, and platformKey are required.");

                gameId = await ResolveOrCreateIgdbGame(
                    igdbId,
                    title,
                    platformKey,
                    request.CapsuleUrl,
                    ct);
            }

            var sessionId = await repository.CreatePlaySession(
                credentialId, gameId, startTime, endTime, ct);
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
            throw AppProblem.Validation("query is required.");

        var igdbAvailable = !string.IsNullOrWhiteSpace(options.IgdbClientId) &&
            !string.IsNullOrWhiteSpace(options.IgdbClientSecret);
        var localTask = repository.SearchLocalGames(trimmed, MaxLocalSearchResults, ct);
        var igdbTask = igdbAvailable
            ? igdbClient.SearchGames(trimmed, options.IgdbClientId!, options.IgdbClientSecret!, ct)
            : Task.FromResult(new List<IgdbSearchResult>());
        await Task.WhenAll(localTask, igdbTask);

        var results = localTask.Result.Select(r => new ManualMappingSearchResultDto(
            Source: "local",
            GameId: GameId.Encode(r.Id),
            IgdbId: null,
            Title: r.Title,
            Platform: r.Platform,
            PlatformId: null,
            CapsuleUrl: r.CapsuleUrl
        )).Concat(igdbTask.Result.Select(result => new ManualMappingSearchResultDto(
            Source: "igdb",
            GameId: null,
            IgdbId: result.Id,
            Title: result.Title,
            Platform: result.Platform,
            PlatformId: result.PlatformId,
            CapsuleUrl: result.CoverUrl
        ))).ToList();

        return new ManualMappingSearchResponseDto(trimmed, igdbAvailable, results);
    }

    /// <summary>
    /// Map a manual game input to a specific game.
    /// Accepts either an explicit gameId or enough fields to resolve/create a game.
    /// </summary>
    public async Task<ManualMappingResponseDto> MapInput(
        long inputId, long userId, ManualMapInputRequest request, CancellationToken ct)
    {
        return await repository.ExecuteInTransaction(async () =>
        {
            long gameId;
            if (!string.IsNullOrWhiteSpace(request.GameId))
            {
                gameId = GameId.Parse(request.GameId);
                if (await repository.GetGameEntity(gameId, ct) is null)
                    throw AppProblem.Validation("Game not found for the specified gameId.");
            }
            else
            {
                var igdbId = request.IgdbId?.Trim();
                var title = request.Title?.Trim();
                if (string.IsNullOrEmpty(igdbId) || string.IsNullOrEmpty(title))
                    throw AppProblem.Validation("igdbId and title are required.");

                var platformKey = request.PlatformKey?.Trim();
                if (string.IsNullOrEmpty(platformKey) && request.PlatformId.HasValue)
                    platformKey = await repository.GetPlatformKeyByIgdbId(request.PlatformId.Value, ct);
                gameId = await ResolveOrCreateIgdbGame(
                    igdbId,
                    title,
                    platformKey ?? "unknown",
                    request.CapsuleUrl,
                    ct);
            }

            var updated = await repository.MapManualInput(userId, inputId, gameId, ct);
            if (updated is null) throw AppProblem.NotFound("Manual input not found.");
            return new ManualMappingResponseDto(updated.Id, GameId.Encode(gameId));
        }, ct);
    }

    private async Task<long> ResolveOrCreateGame(
        string title, string platformKey, string? capsuleUrl, CancellationToken ct)
    {
        var canonicalKey = $"{platformKey}:{games.NormalizeTitle(title)}";

        var platformId = await repository.GetPlatformIdByKey(platformKey, ct);
        if (platformId is null)
            throw AppProblem.Validation($"Platform '{platformKey}' not found.");

        return await repository.UpsertGame(title, canonicalKey, null, platformId.Value, capsuleUrl, ct);
    }

    private async Task<long> ResolveOrCreateIgdbGame(
        string igdbId,
        string title,
        string platformKey,
        string? capsuleUrl,
        CancellationToken ct)
    {
        var platformId = await repository.GetPlatformIdByKey(platformKey, ct)
            ?? await repository.GetPlatformIdByKey("unknown", ct);
        if (!platformId.HasValue) throw AppProblem.Validation($"Platform '{platformKey}' not found.");

        return await repository.UpsertGame(
            title,
            $"igdb:{igdbId}",
            igdbId,
            platformId.Value,
            capsuleUrl,
            ct);
    }

}
