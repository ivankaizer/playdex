using System.Globalization;
using System.Text.RegularExpressions;
using Playdex.Api.Common;
using Playdex.Api.Http;

namespace Playdex.Api.Manual;

public sealed partial class ManualService(IManualRepository repository)
{
    private const int MaxSearchResults = 20;
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
                row.MappedGameId.HasValue ? "manual" : null,
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

        var credentialId = await repository.GetOrCreateManualCredential(userId, ct);
        var canonicalTitle = NormalizeTitle(title);

        // Upsert the manual game input (tracks the raw vs canonical title)
        await repository.UpsertManualGameInput(userId, title, canonicalTitle, ct);

        // End any currently-active manual sessions
        await repository.EndActiveSessions(credentialId, ct);

        // Find or create the game entity
        var gameId = await ResolveOrCreateGame(title, ManualPlatformKey, null, ct);

        // Create a new active session (started_at = now, ended_at = null)
        var sessionId = await repository.CreatePlaySession(
            credentialId, gameId, DateTimeOffset.UtcNow, null, ct);

        return new ManualSessionStartResponseDto(sessionId);
    }

    /// <summary>
    /// Stop all active manual tracking sessions for the current user.
    /// </summary>
    public async Task<ManualSessionStopResponseDto> StopSession(long userId, CancellationToken ct)
    {
        var credentialId = await repository.GetOrCreateManualCredential(userId, ct);
        var count = await repository.EndActiveSessions(credentialId, ct);

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
        // Parse the date
        if (!DateTimeOffset.TryParse(request.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
            throw AppProblem.Validation("Invalid date format.");

        if (request.DurationMinutes <= 0)
            throw AppProblem.Validation("Duration must be positive.");

        var endTime = startTime.AddMinutes(request.DurationMinutes);
        var credentialId = await repository.GetOrCreateManualCredential(userId, ct);

        long gameId;

        if (request.GameId is not null)
        {
            // Use explicitly-provided game
            gameId = GameId.Parse(request.GameId);
            var game = await repository.GetGameEntity(gameId, ct);
            if (game is null)
                throw AppProblem.Validation("Game not found for the specified gameId.");
        }
        else
        {
            // Resolve or create a game from the input fields
            gameId = await ResolveOrCreateGame(
                request.Title ?? "Unknown Game",
                request.PlatformKey ?? ManualPlatformKey,
                request.CapsuleUrl,
                ct);
        }

        // Create the historical session
        var sessionId = await repository.CreatePlaySession(
            credentialId, gameId, startTime, endTime, ct);

        return new ManualEntryCreateResponseDto(sessionId);
    }

    /// <summary>
    /// Search for possible game mappings (local database).
    /// IGDB integration is not yet available.
    /// </summary>
    public async Task<ManualMappingSearchResponseDto> SearchMapping(
        string query, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
            return new ManualMappingSearchResponseDto(query, false, []);

        var localResults = await repository.SearchLocalGames(trimmed, MaxSearchResults, ct);

        var results = localResults.Select(r => new ManualMappingSearchResultDto(
            Source: "local",
            GameId: GameId.Encode(r.Id),
            IgdbId: null,
            Title: r.Title,
            Platform: r.Platform,
            PlatformId: null,
            CapsuleUrl: r.CapsuleUrl
        )).ToList();

        return new ManualMappingSearchResponseDto(query, IgdbAvailable: false, results);
    }

    /// <summary>
    /// Map a manual game input to a specific game.
    /// Accepts either an explicit gameId or enough fields to resolve/create a game.
    /// </summary>
    public async Task<ManualMappingResponseDto> MapInput(
        long inputId, long userId, ManualMapInputRequest request, CancellationToken ct)
    {
        long? gameId;

        if (request.GameId is not null)
        {
            // Map to an existing game by ID
            gameId = GameId.Parse(request.GameId);
            var game = await repository.GetGameEntity(gameId.Value, ct);
            if (game is null)
                throw AppProblem.Validation("Game not found for the specified gameId.");
        }
        else if (request.IgdbId.HasValue)
        {
            // IGDB integration not available yet — leave unmapped
            gameId = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.Title))
        {
            // Resolve or create a game from the provided fields
            var platformKey = request.PlatformKey ?? ManualPlatformKey;
            gameId = await ResolveOrCreateGame(request.Title, platformKey, request.CapsuleUrl, ct);
        }
        else
        {
            // No mapping criteria provided — clear any existing mapping
            gameId = null;
        }

        await repository.MapManualInput(inputId, gameId, ct);

        return new ManualMappingResponseDto(inputId, gameId.HasValue ? GameId.Encode(gameId.Value) : null);
    }

    // ── Private helpers ─────────────────────────────────────────

    /// <summary>
    /// Normalize a game title into a stable canonical key for deduplication.
    /// </summary>
    private static string NormalizeTitle(string title)
    {
        var collapsed = WhitespaceRegex().Replace(title.Trim(), " ");
        return collapsed.ToLowerInvariant();
    }

    /// <summary>
    /// Resolve an existing game by its canonical key, or create a new one
    /// with the manual platform.
    /// </summary>
    private async Task<long> ResolveOrCreateGame(
        string title, string platformKey, string? capsuleUrl, CancellationToken ct)
    {
        var canonicalKey = NormalizeTitle(title);

        var platformId = await repository.GetPlatformIdByKey(platformKey, ct);
        if (platformId is null)
            throw AppProblem.Validation($"Platform '{platformKey}' not found.");

        return await repository.UpsertGame(title, canonicalKey, platformId.Value, capsuleUrl, ct);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
