using System.Security.Claims;
using System.Diagnostics;
using Admin.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Admin.Features;

public sealed record RecentSession(long Id, string Game, DateTimeOffset StartedAt, DateTimeOffset? EndedAt)
{
    public bool Live => EndedAt is null;
    public double DurationSeconds => ((EndedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalSeconds;
}

public sealed record UserRecentSessions(long UserId, string Email, IReadOnlyList<RecentSession> Sessions);

public sealed record PlatformSummary(string Key, string Name, int Total, int Active)
{
    public bool Enabled => Active > 0;
}

public sealed record IntegrationSummary(long Id, string Email, string Platform, string? AccountId, bool Active);

public sealed record SessionSummary(
    long Id,
    string Email,
    string Platform,
    string? AccountId,
    string Game,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt)
{
    public bool Live => EndedAt is null;
    public double DurationSeconds => ((EndedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalSeconds;
}

public sealed record SessionSearchResult(IReadOnlyList<SessionSummary> Sessions, int Total, int Page);

public sealed record GameSummary(long Id, string Title, string? CapsuleUrl, bool TitleLocked, bool CapsuleUrlLocked);

public interface IAdminOperations
{
    Task<IReadOnlyList<UserRecentSessions>> GetRecentSessionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PlatformSummary>> GetPlatformSummariesAsync(CancellationToken ct = default);

    Task<int> SetPlatformEnabledAsync(ClaimsPrincipal principal, string platform, bool enabled,
        CancellationToken ct = default);

    Task<IReadOnlyList<IntegrationSummary>> GetIntegrationsAsync(CancellationToken ct = default);

    Task<bool> UpdateIntegrationAsync(ClaimsPrincipal principal, long id, bool apiKeyChanged, string? apiKey,
        bool accountIdChanged, string? accountId, CancellationToken ct = default);

    Task<int> TransferSessionsAsync(ClaimsPrincipal principal, IReadOnlyCollection<long> sessionIds,
        long targetIntegrationId, CancellationToken ct = default);

    Task<bool> UpdateGameAsync(ClaimsPrincipal principal, long id, string? title, bool titleChanged, string? capsuleUrl,
        bool capsuleUrlChanged, bool? titleLocked, bool? capsuleUrlLocked, CancellationToken ct = default);

    Task<IReadOnlyList<SessionSummary>> SearchSessionsAsync(string query, int page, CancellationToken ct = default);
    Task<SessionSearchResult> SearchSessionsPageAsync(string query, int page, string? status = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<GameSummary>> ListGamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<GameSummary>> SearchGamesAsync(string query, CancellationToken ct = default);
    Task MergeGamesAsync(ClaimsPrincipal principal, long sourceId, long targetId, CancellationToken ct = default);
}

public sealed partial class AdminOperations(
    IDbContextFactory<PlaydexDbContext> factory,
    AdminOperationGuard guard,
    ILogger<AdminOperations> logger) : IAdminOperations
{
    public async Task<IReadOnlyList<UserRecentSessions>> GetRecentSessionsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await (
                from session in db.PlaySessions.AsNoTracking()
                join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                join game in db.Games.AsNoTracking() on session.GameId equals game.Id
                orderby user.Email, session.StartedAt descending, session.Id descending
                select new
                {
                    user.Id, user.Email,
                    Session = new RecentSession(session.Id, game.Title, session.StartedAt, session.EndedAt)
                })
            .ToListAsync(ct);
        return rows.GroupBy(row => new { row.Id, row.Email })
            .Select(group =>
                new UserRecentSessions(group.Key.Id, group.Key.Email,
                    group.Select(row => row.Session).Take(3).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<PlatformSummary>> GetPlatformSummariesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await (
                from integration in db.Integrations.AsNoTracking()
                join platform in db.Platforms.AsNoTracking() on integration.PlatformId equals platform.Id
                group integration by new { platform.Key, platform.Name }
                into grouped
                orderby grouped.Key.Name
                select new PlatformSummary(grouped.Key.Key, grouped.Key.Name, grouped.Count(),
                    grouped.Count(row => row.Active)))
            .ToListAsync(ct);
    }

    public async Task<int> SetPlatformEnabledAsync(ClaimsPrincipal principal, string platform, bool enabled,
        CancellationToken ct = default)
    {
        if (!await guard.IsAdministratorAsync(principal, ct)) throw new UnauthorizedAccessException();
        await using var db = await factory.CreateDbContextAsync(ct);
        var platformRow = await db.Platforms.SingleOrDefaultAsync(row => row.Key == platform.Trim(), ct)
                          ?? throw new KeyNotFoundException("Platform not found.");
        var integrations = await db.Integrations.Where(row => row.PlatformId == platformRow.Id).ToListAsync(ct);
        foreach (var integration in integrations)
        {
            integration.Active = enabled;
            integration.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
#pragma warning disable CA1873 // Correlation id is required for every mutation log and cheap to compute
        LogPlatformMutation(logger, "set-platform-enabled", platformRow.Key, integrations.Count, enabled,
            CorrelationId());
#pragma warning restore CA1873
        return integrations.Count;
    }

    public async Task<IReadOnlyList<IntegrationSummary>> GetIntegrationsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await (from integration in db.Integrations.AsNoTracking()
            join user in db.Users.AsNoTracking() on integration.UserId equals user.Id
            join platform in db.Platforms.AsNoTracking() on integration.PlatformId equals (long?)platform.Id into
                platforms
            from platform in platforms.DefaultIfEmpty()
            orderby user.Email,
                integration.Type == IntegrationType.Api ? "api" : platform!.Key,
                integration.Id
            select new IntegrationSummary(integration.Id, user.Email,
                integration.Type == IntegrationType.Api ? "api" : platform!.Key,
                integration.AccountId, integration.Active)).ToListAsync(ct);
        return rows;
    }

    public async Task<bool> UpdateIntegrationAsync(ClaimsPrincipal principal, long id, bool apiKeyChanged,
        string? apiKey, bool accountIdChanged, string? accountId, CancellationToken ct = default)
    {
        if (!await guard.IsAdministratorAsync(principal, ct)) throw new UnauthorizedAccessException();
        if (!apiKeyChanged && !accountIdChanged)
            throw new ArgumentException("At least one credential field must be selected.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var integration = await db.Integrations.SingleOrDefaultAsync(row => row.Id == id, ct);
        if (integration is null) return false;
        if (apiKeyChanged) integration.ApiKey = NormalizeOptional(apiKey);
        if (accountIdChanged) integration.AccountId = NormalizeOptional(accountId);
        integration.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
#pragma warning disable CA1873 // Correlation id is required for every mutation log and cheap to compute
        LogIntegrationMutation(logger, "update-integration", id, CorrelationId());
#pragma warning restore CA1873
        return true;
    }

    public async Task<int> TransferSessionsAsync(ClaimsPrincipal principal, IReadOnlyCollection<long> sessionIds,
        long targetIntegrationId, CancellationToken ct = default)
    {
        if (!await guard.IsAdministratorAsync(principal, ct)) throw new UnauthorizedAccessException();
        if (sessionIds.Count == 0) throw new ArgumentException("At least one session is required.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var target = await db.Integrations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == targetIntegrationId, ct)
                     ?? throw new KeyNotFoundException("Target integration not found.");
        var sessions =
            db.PlaySessions.Where(row => sessionIds.Contains(row.Id) && row.IntegrationId != targetIntegrationId);
        var updatedAt = DateTimeOffset.UtcNow;
        var updated = target.PlatformId.HasValue
            ? await sessions.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.IntegrationId, target.Id)
                .SetProperty(row => row.UserId, target.UserId)
                .SetProperty(row => row.PlatformId, target.PlatformId.Value)
                .SetProperty(row => row.UpdatedAt, updatedAt), ct)
            : await sessions.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.IntegrationId, target.Id)
                .SetProperty(row => row.UserId, target.UserId)
                .SetProperty(row => row.UpdatedAt, updatedAt), ct);
#pragma warning disable CA1873 // Correlation id is required for every mutation log and cheap to compute
        LogSessionTransfer(logger, "transfer-sessions", targetIntegrationId, updated, CorrelationId());
#pragma warning restore CA1873
        return updated;
    }

    public async Task<bool> UpdateGameAsync(ClaimsPrincipal principal, long id, string? title, bool titleChanged,
        string? capsuleUrl, bool capsuleUrlChanged, bool? titleLocked, bool? capsuleUrlLocked,
        CancellationToken ct = default)
    {
        if (!await guard.IsAdministratorAsync(principal, ct)) throw new UnauthorizedAccessException();
        if (!titleChanged && !capsuleUrlChanged && !titleLocked.HasValue && !capsuleUrlLocked.HasValue)
            throw new ArgumentException("At least one game field is required.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var game = await db.Games.SingleOrDefaultAsync(row => row.Id == id, ct);
        if (game is null) return false;
        if (titleChanged)
        {
            var normalizedTitle = title?.Trim();
            if (string.IsNullOrEmpty(normalizedTitle)) throw new ArgumentException("Title is required.");
            game.Title = normalizedTitle;
            if (!titleLocked.HasValue) game.TitleLocked = true;
        }

        if (capsuleUrlChanged)
        {
            game.CapsuleUrl = string.IsNullOrWhiteSpace(capsuleUrl) ? null : capsuleUrl.Trim();
            if (!capsuleUrlLocked.HasValue) game.CapsuleUrlLocked = true;
        }

        if (titleLocked.HasValue) game.TitleLocked = titleLocked.Value;
        if (capsuleUrlLocked.HasValue) game.CapsuleUrlLocked = capsuleUrlLocked.Value;
        game.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
#pragma warning disable CA1873 // Correlation id is required for every mutation log and cheap to compute
        LogGameMutation(logger, "update-game", id, CorrelationId());
#pragma warning restore CA1873
        return true;
    }

    public async Task<IReadOnlyList<SessionSummary>> SearchSessionsAsync(string query, int page,
        CancellationToken ct = default)
    {
        var term = query.Trim();
        await using var db = await factory.CreateDbContextAsync(ct);
        var like = $"%{term}%";
        var rows = from session in db.PlaySessions.AsNoTracking()
            join user in db.Users.AsNoTracking() on session.UserId equals user.Id
            join game in db.Games.AsNoTracking() on session.GameId equals game.Id
            join platform in db.Platforms.AsNoTracking() on session.PlatformId equals platform.Id
            join integration in db.Integrations.AsNoTracking() on session.IntegrationId equals (long?)integration.Id
                into integrations
            from integration in integrations.DefaultIfEmpty()
            where EF.Functions.ILike(user.Email, like) || EF.Functions.ILike(game.Title, like)
                                                       || EF.Functions.ILike(platform.Key, like)
                                                       || (integration != null && integration.AccountId != null &&
                                                           EF.Functions.ILike(integration.AccountId, like))
            orderby session.StartedAt descending, session.Id descending
            select new SessionSummary(session.Id, user.Email, platform.Name,
                integration == null ? null : integration.AccountId, game.Title, session.StartedAt, session.EndedAt);
        return await rows.Skip(Math.Max(0, page) * 25).Take(25).ToListAsync(ct);
    }

    public async Task<SessionSearchResult> SearchSessionsPageAsync(string query, int page, string? status = null,
        CancellationToken ct = default)
    {
        var term = query.Trim();
        await using var db = await factory.CreateDbContextAsync(ct);
        var like = $"%{term}%";
        var rows = from session in db.PlaySessions.AsNoTracking()
            join user in db.Users.AsNoTracking() on session.UserId equals user.Id
            join game in db.Games.AsNoTracking() on session.GameId equals game.Id
            join platform in db.Platforms.AsNoTracking() on session.PlatformId equals platform.Id
            join integration in db.Integrations.AsNoTracking() on session.IntegrationId equals (long?)integration.Id
                into integrations
            from integration in integrations.DefaultIfEmpty()
            where EF.Functions.ILike(user.Email, like) || EF.Functions.ILike(game.Title, like) ||
                  EF.Functions.ILike(platform.Key, like)
                  || (integration != null && integration.AccountId != null &&
                      EF.Functions.ILike(integration.AccountId, like))
            select new
            {
                session.Id,
                user.Email,
                Platform = platform.Name,
                AccountId = integration == null ? null : integration.AccountId,
                Game = game.Title,
                session.StartedAt,
                session.EndedAt,
            };
        if (status is "live") rows = rows.Where(row => row.EndedAt == null);
        else if (status is "ended") rows = rows.Where(row => row.EndedAt != null);

        var total = await rows.CountAsync(ct);
        var sessions = await rows.OrderByDescending(row => row.StartedAt).ThenByDescending(row => row.Id)
            .Skip(Math.Max(0, page) * 25).Take(25)
            .Select(row => new SessionSummary(row.Id, row.Email, row.Platform, row.AccountId, row.Game, row.StartedAt,
                row.EndedAt))
            .ToListAsync(ct);
        return new SessionSearchResult(sessions, total, Math.Max(0, page));
    }

    public async Task<IReadOnlyList<GameSummary>> ListGamesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Games.AsNoTracking()
            .OrderByDescending(game => game.UpdatedAt).Take(20)
            .Select(game =>
                new GameSummary(game.Id, game.Title, game.CapsuleUrl, game.TitleLocked, game.CapsuleUrlLocked))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GameSummary>> SearchGamesAsync(string query, CancellationToken ct = default)
    {
        var term = query.Trim();
        if (term.Length == 0) throw new ArgumentException("A game search query is required.", nameof(query));
        await using var db = await factory.CreateDbContextAsync(ct);
        var exact = GameIdentifier.TryParse(term);
        var like = $"%{term}%";
        return await db.Games.AsNoTracking()
            .Where(game => EF.Functions.ILike(game.Title, like) ||
                           (game.CapsuleUrl != null && EF.Functions.ILike(game.CapsuleUrl, like)) ||
                           (exact.HasValue && game.Id == exact.Value))
            .OrderByDescending(game => game.UpdatedAt).Take(20)
            .Select(game =>
                new GameSummary(game.Id, game.Title, game.CapsuleUrl, game.TitleLocked, game.CapsuleUrlLocked))
            .ToListAsync(ct);
    }

    public async Task MergeGamesAsync(ClaimsPrincipal principal, long sourceId, long targetId,
        CancellationToken ct = default)
    {
        if (!await guard.IsAdministratorAsync(principal, ct)) throw new UnauthorizedAccessException();
        if (sourceId <= 0 || targetId <= 0 || sourceId == targetId)
            throw new ArgumentException("Source and target games must be distinct positive ids.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Games
            .FromSqlInterpolated($"select * from games where id in ({sourceId}, {targetId}) for update")
            .OrderBy(game => game.Id).ToListAsync(ct);
        var source = locked.SingleOrDefault(game => game.Id == sourceId) ??
                     throw new KeyNotFoundException("Source game not found.");
        var target = locked.SingleOrDefault(game => game.Id == targetId) ??
                     throw new KeyNotFoundException("Target game not found.");
        if (!target.TitleLocked && source.TitleLocked) target.Title = source.Title;
        if (!target.CapsuleUrlLocked && target.CapsuleUrl is null) target.CapsuleUrl = source.CapsuleUrl;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"update game_external_identities set game_id={targetId}, updated_at=now() where game_id={sourceId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"update play_sessions set game_id={targetId}, updated_at=now() where game_id={sourceId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"delete from ignored_games s where s.game_id={sourceId} and exists(select 1 from ignored_games t where t.user_id=s.user_id and t.game_id={targetId})",
            ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"update ignored_games set game_id={targetId}, updated_at=now() where game_id={sourceId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"delete from game_ratings s where s.game_id={sourceId} and exists(select 1 from game_ratings t where t.user_id=s.user_id and t.game_id={targetId})",
            ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"update game_ratings set game_id={targetId}, updated_at=now() where game_id={sourceId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
                                                       with neighbors as (select user_id, case when game_id={sourceId} then connected_game_id else game_id end neighbor from game_connections where game_id={sourceId} or connected_game_id={sourceId}),
                                                       removed as (delete from game_connections where game_id={sourceId} or connected_game_id={sourceId})
                                                       insert into game_connections(user_id,game_id,connected_game_id,created_at,updated_at)
                                                       select user_id,least({targetId},neighbor),greatest({targetId},neighbor),now(),now() from neighbors where neighbor<>{targetId} on conflict do nothing
                                                       """, ct);
        db.Games.Remove(source);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
#pragma warning disable CA1873 // Correlation id is required for every mutation log and cheap to compute
        LogGameMerge(logger, "merge-games", sourceId, targetId, CorrelationId());
#pragma warning restore CA1873
    }

    private static string CorrelationId() => Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message =
            "Admin mutation {Operation} platform {Platform} count {Count} enabled {Enabled} correlation {CorrelationId}")]
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
        Message = "Admin mutation {Operation} game {GameId} correlation {CorrelationId}")]
    private static partial void LogGameMutation(ILogger logger, string operation, long gameId, string correlationId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Admin mutation {Operation} source {SourceGameId} target {TargetGameId} correlation {CorrelationId}")]
    private static partial void LogGameMerge(ILogger logger, string operation, long sourceGameId, long targetGameId,
        string correlationId);
}

internal static class GameIdentifier
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const long Multiplier = 1299827;
    private const long Increment = 811;

    public static long? TryParse(string value)
    {
        if (long.TryParse(value, out var raw) && raw > 0) return raw;
        ulong decoded = 0;
        foreach (var character in value)
        {
            var index = Alphabet.IndexOf(character);
            if (index < 0) return null;
            decoded = decoded * 62 + (uint)index;
        }

        if (decoded < Increment || (decoded - (ulong)Increment) % (ulong)Multiplier != 0) return null;
        var result = (long)((decoded - (ulong)Increment) / (ulong)Multiplier);
        return result > 0 ? result : null;
    }
}
