using Api.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.Admin;

public sealed record AdminTransferTarget(long Id, long UserId, IntegrationType Type, long? PlatformId);

public sealed record AdminGenerationRecord(string Email, AiGeneration Generation);

public sealed class AdminRepository(PlaydexDbContext db)
{
    public const int SessionPageSize = 25;
    public const int GenerationPageSize = 25;
    public const int GameListLimit = 20;

    private static readonly string[] FailureStatuses = ["failure", "timeout", "cancelled"];

    public Task<bool> IsAdministrator(long userId, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.IsAdmin, ct);

    // ---- Dashboard -------------------------------------------------------------------------

    public async Task<AdminDashboardDto> ReadDashboard(string query, string? platform, DateTimeOffset dayStart,
        DateTimeOffset dayEnd, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddHours(-24);
        var live = db.PlaySessions.AsNoTracking().Where(row => row.EndedAt == null);
        var failures = db.AiGenerations.AsNoTracking().Where(row => FailureStatuses.Contains(row.Status));
        var counts = new AdminDashboardCountsDto(
            await live.CountAsync(ct),
            await live.Select(row => row.UserId).Distinct().CountAsync(ct),
            await db.PlaySessions.CountAsync(row => row.StartedAt >= dayStart && row.StartedAt < dayEnd, ct),
            await failures.CountAsync(row => row.CreatedAt >= since && row.CreatedAt <= now, ct));
        var rows = from session in db.PlaySessions.AsNoTracking()
            join user in db.Users.AsNoTracking() on session.UserId equals user.Id
            join game in db.Games.AsNoTracking() on session.GameId equals game.Id
            join playedPlatform in db.Platforms.AsNoTracking() on session.PlatformId equals playedPlatform.Id
            join integration in db.Integrations.AsNoTracking() on session.IntegrationId equals (long?)integration.Id
                into accounts
            from integration in accounts.DefaultIfEmpty()
            select new
            {
                session.Id,
                user.Email,
                Game = game.Title,
                game.CapsuleUrl,
                PlatformKey = playedPlatform.Key,
                Platform = playedPlatform.Name,
                AccountId = integration == null ? null : integration.AccountId,
                Source = session.CaptureMethod,
                session.StartedAt,
                session.EndedAt,
                LastPolledAt = session.CaptureMethod == CaptureMethod.Polling && session.IntegrationId != null
                    ? db.PollResults.Where(poll => poll.IntegrationId == session.IntegrationId)
                        .Select(poll => (DateTimeOffset?)poll.PolledAt).Max()
                    : null,
            };
        var liveRows = rows.Where(row => row.EndedAt == null);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var like = $"%{query.Trim()}%";
            liveRows = liveRows.Where(row => EF.Functions.ILike(row.Email, like)
                                             || EF.Functions.ILike(row.Game, like)
                                             || (row.AccountId != null && EF.Functions.ILike(row.AccountId, like)));
        }

        if (!string.IsNullOrWhiteSpace(platform)) liveRows = liveRows.Where(row => row.PlatformKey == platform);
        var liveSessions = await liveRows.OrderBy(row => row.StartedAt).ThenBy(row => row.Id)
            .Select(row => new
            {
                row.Id, row.Email, row.Game, row.CapsuleUrl, row.PlatformKey, row.Platform, row.AccountId,
                row.Source, row.StartedAt, row.EndedAt, row.LastPolledAt,
            })
            .ToListAsync(ct);
        var ended = await rows.Where(row => row.EndedAt != null)
            .OrderByDescending(row => row.EndedAt).ThenByDescending(row => row.Id).Take(10)
            .Select(row => new
            {
                row.Id, row.Email, row.Game, row.CapsuleUrl, row.PlatformKey, row.Platform, row.AccountId,
                row.Source, row.StartedAt, row.EndedAt, row.LastPolledAt,
            })
            .ToListAsync(ct);
        var generations = await (from generation in failures
                join user in db.Users.AsNoTracking() on generation.UserId equals user.Id
                orderby generation.CreatedAt descending, generation.Id descending
                select new AdminDashboardGenerationDto(generation.Id, user.Email, generation.Model, generation.Status,
                    generation.CreatedAt))
            .Take(10).ToListAsync(ct);
        var platforms = await db.Platforms.AsNoTracking().OrderBy(row => row.Name)
            .Select(row => new AdminDashboardPlatformDto(row.Key, row.Name)).ToListAsync(ct);

        return new AdminDashboardDto(now, counts, platforms,
            liveSessions.Select(row => new AdminDashboardSessionDto(row.Id, row.Email, row.Game, row.CapsuleUrl,
                row.PlatformKey, row.Platform, row.AccountId, SourceName(row.Source), row.StartedAt, row.EndedAt,
                row.LastPolledAt)).ToList(),
            ended.Select(row => new AdminDashboardSessionDto(row.Id, row.Email, row.Game, row.CapsuleUrl,
                row.PlatformKey, row.Platform, row.AccountId, SourceName(row.Source), row.StartedAt, row.EndedAt,
                row.LastPolledAt)).ToList(),
            generations);
    }

    public static string SourceName(CaptureMethod method) => method switch
    {
        CaptureMethod.Polling => "polling",
        CaptureMethod.Api => "api",
        _ => "manual",
    };

    // ---- Platforms & integrations ----------------------------------------------------------

    public async Task<IReadOnlyList<AdminPlatformSummaryDto>> ListPlatformSummaries(CancellationToken ct) =>
        await (from integration in db.Integrations.AsNoTracking()
                join platform in db.Platforms.AsNoTracking() on integration.PlatformId equals platform.Id
                group integration by new { platform.Key, platform.Name }
                into grouped
                orderby grouped.Key.Name
                select new AdminPlatformSummaryDto(grouped.Key.Key, grouped.Key.Name, grouped.Count(),
                    grouped.Count(row => row.Active)))
            .ToListAsync(ct);

    /// <returns>The number of integrations updated, or null when the platform does not exist.</returns>
    public async Task<(string Key, int Count)?> SetPlatformEnabled(string platform, bool enabled, CancellationToken ct)
    {
        var key = platform.Trim();
        var platformRow = await db.Platforms.SingleOrDefaultAsync(row => row.Key == key, ct);
        if (platformRow is null) return null;
        var integrations = await db.Integrations.Where(row => row.PlatformId == platformRow.Id).ToListAsync(ct);
        foreach (var integration in integrations)
        {
            integration.Active = enabled;
            integration.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return (platformRow.Key, integrations.Count);
    }

    public async Task<IReadOnlyList<AdminIntegrationDto>> ListIntegrations(CancellationToken ct) =>
        await (from integration in db.Integrations.AsNoTracking()
                join user in db.Users.AsNoTracking() on integration.UserId equals user.Id
                join platform in db.Platforms.AsNoTracking() on integration.PlatformId equals (long?)platform.Id
                    into platforms
                from platform in platforms.DefaultIfEmpty()
                orderby user.Email,
                    integration.Type == IntegrationType.Api ? "api" : platform!.Key,
                    integration.Id
                select new AdminIntegrationDto(integration.Id, user.Email,
                    integration.Type == IntegrationType.Api ? "api" : platform!.Key,
                    integration.AccountId, integration.Active, integration.ApiKey))
            .ToListAsync(ct);

    public async Task<bool> UpdateIntegration(long id, bool apiKeyChanged, string? apiKey, bool accountIdChanged,
        string? accountId, CancellationToken ct)
    {
        var integration = await db.Integrations.SingleOrDefaultAsync(row => row.Id == id, ct);
        if (integration is null) return false;
        if (apiKeyChanged) integration.ApiKey = apiKey;
        if (accountIdChanged) integration.AccountId = accountId;
        integration.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Sessions ---------------------------------------------------------------------------

    public async Task<AdminSessionPageDto> SearchSessions(string query, int page, string? status,
        DateTimeOffset? startedFrom, DateTimeOffset? startedBefore, CancellationToken ct)
    {
        var like = $"%{query.Trim()}%";
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
        if (startedFrom is not null) rows = rows.Where(row => row.StartedAt >= startedFrom);
        if (startedBefore is not null) rows = rows.Where(row => row.StartedAt < startedBefore);

        page = Math.Max(0, page);
        var total = await rows.CountAsync(ct);
        var sessions = await rows.OrderByDescending(row => row.StartedAt).ThenByDescending(row => row.Id)
            .Skip(page * SessionPageSize).Take(SessionPageSize)
            .Select(row => new AdminSessionDto(row.Id, row.Email, row.Platform, row.AccountId, row.Game,
                row.StartedAt, row.EndedAt))
            .ToListAsync(ct);
        return new AdminSessionPageDto(sessions, total, page, SessionPageSize);
    }

    public Task<AdminTransferTarget?> FindTransferTarget(long integrationId, CancellationToken ct) =>
        db.Integrations.AsNoTracking().Where(row => row.Id == integrationId)
            .Select(row => new AdminTransferTarget(row.Id, row.UserId, row.Type, row.PlatformId))
            .SingleOrDefaultAsync(ct);

    public Task<bool> AnyManualSessions(IReadOnlyCollection<long> sessionIds, CancellationToken ct) =>
        db.PlaySessions.AsNoTracking()
            .AnyAsync(row => sessionIds.Contains(row.Id) && row.CaptureMethod == CaptureMethod.Manual, ct);

    public async Task<int> TransferSessions(IReadOnlyCollection<long> sessionIds, AdminTransferTarget target,
        CancellationToken ct)
    {
        // Integration-backed sessions only: a manual session has no integration and must stay that way.
        var captureMethod = target.Type == IntegrationType.Api ? CaptureMethod.Api : CaptureMethod.Polling;
        var sessions = db.PlaySessions.Where(row => sessionIds.Contains(row.Id)
                                                    && row.IntegrationId != target.Id
                                                    && row.CaptureMethod != CaptureMethod.Manual);
        var updatedAt = DateTimeOffset.UtcNow;
        return target.PlatformId.HasValue
            ? await sessions.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.IntegrationId, target.Id)
                .SetProperty(row => row.UserId, target.UserId)
                .SetProperty(row => row.PlatformId, target.PlatformId.Value)
                .SetProperty(row => row.CaptureMethod, captureMethod)
                .SetProperty(row => row.UpdatedAt, updatedAt), ct)
            : await sessions.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.IntegrationId, target.Id)
                .SetProperty(row => row.UserId, target.UserId)
                .SetProperty(row => row.CaptureMethod, captureMethod)
                .SetProperty(row => row.UpdatedAt, updatedAt), ct);
    }

    // ---- Games --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<AdminGameDto>> ListGames(CancellationToken ct) =>
        await db.Games.AsNoTracking()
            .OrderByDescending(game => game.UpdatedAt).Take(GameListLimit)
            .Select(game => new AdminGameDto(game.Id, game.Title, game.CapsuleUrl, game.TitleLocked,
                game.CapsuleUrlLocked))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AdminGameDto>> SearchGames(string term, CancellationToken ct)
    {
        var exact = ParseGameIdentifier(term);
        var like = $"%{term}%";
        return await db.Games.AsNoTracking()
            .Where(game => EF.Functions.ILike(game.Title, like) ||
                           (game.CapsuleUrl != null && EF.Functions.ILike(game.CapsuleUrl, like)) ||
                           (exact.HasValue && game.Id == exact.Value))
            .OrderByDescending(game => game.UpdatedAt).Take(GameListLimit)
            .Select(game => new AdminGameDto(game.Id, game.Title, game.CapsuleUrl, game.TitleLocked,
                game.CapsuleUrlLocked))
            .ToListAsync(ct);
    }

    public static long? ParseGameIdentifier(string value)
    {
        if (long.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var raw))
            return raw > 0 ? raw : null;
        return GameId.Decode(value);
    }

    public Task<Game?> FindGameForUpdate(long id, CancellationToken ct) =>
        db.Games.SingleOrDefaultAsync(row => row.Id == id, ct);

    public Task SaveChanges(CancellationToken ct) => db.SaveChangesAsync(ct);

    /// <summary>Merges <paramref name="sourceId"/> into <paramref name="targetId"/> atomically.</summary>
    /// <returns>Null on success, otherwise a description of which game was missing.</returns>
    public async Task<string?> MergeGames(long sourceId, long targetId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Games
            .FromSqlInterpolated($"select * from games where id in ({sourceId}, {targetId}) for update")
            .OrderBy(game => game.Id).ToListAsync(ct);
        var source = locked.SingleOrDefault(game => game.Id == sourceId);
        if (source is null) return "Source game not found.";
        var target = locked.SingleOrDefault(game => game.Id == targetId);
        if (target is null) return "Target game not found.";
        if (!target.TitleLocked && source.TitleLocked) target.Title = source.Title;
        if (!target.CapsuleUrlLocked && target.CapsuleUrl is null) target.CapsuleUrl = source.CapsuleUrl;
        var now = DateTimeOffset.UtcNow;
        target.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await db.GameExternalIdentities.Where(row => row.GameId == sourceId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.GameId, targetId).SetProperty(row => row.UpdatedAt, now), ct);
        await db.PlaySessions.Where(row => row.GameId == sourceId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.GameId, targetId).SetProperty(row => row.UpdatedAt, now), ct);
        // A user who already has a target row keeps it; their source row would collide on (user_id, game_id).
        await db.IgnoredGames
            .Where(s => s.GameId == sourceId && db.IgnoredGames.Any(t => t.UserId == s.UserId && t.GameId == targetId))
            .ExecuteDeleteAsync(ct);
        await db.IgnoredGames.Where(row => row.GameId == sourceId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.GameId, targetId).SetProperty(row => row.UpdatedAt, now), ct);
        await db.GameRatings
            .Where(s => s.GameId == sourceId && db.GameRatings.Any(t => t.UserId == s.UserId && t.GameId == targetId))
            .ExecuteDeleteAsync(ct);
        await db.GameRatings.Where(row => row.GameId == sourceId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.GameId, targetId).SetProperty(row => row.UpdatedAt, now), ct);
        // Data-modifying CTE with ON CONFLICT has no LINQ equivalent.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            with neighbors as (select user_id, case when game_id={sourceId} then connected_game_id else game_id end neighbor from game_connections where game_id={sourceId} or connected_game_id={sourceId}),
            removed as (delete from game_connections where game_id={sourceId} or connected_game_id={sourceId})
            insert into game_connections(user_id,game_id,connected_game_id,created_at,updated_at)
            select user_id,least({targetId},neighbor),greatest({targetId},neighbor),now(),now() from neighbors where neighbor<>{targetId} on conflict do nothing
            """, ct);
        // AI draft rows reference games by their public (encoded) id, both in game_id and in the stored
        // selection snapshot; repoint both so pending drafts do not point at the deleted source game.
        var sourceHash = GameId.Encode(sourceId);
        var targetHash = GameId.Encode(targetId);
        var sourceRaw = sourceId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string?[] sourceGameIds = [sourceHash, sourceRaw];
        await db.SessionDraftRows.Where(row => sourceGameIds.Contains(row.GameId))
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.GameId, targetHash), ct);
        // selection_json is mapped as a string, so jsonb_set has no LINQ equivalent.
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            update session_draft_rows
            set selection_json = jsonb_set(selection_json, array['GameId'], to_jsonb({{targetHash}}::text))
            where jsonb_typeof(selection_json) = 'object' and selection_json->>'GameId' in ({{sourceHash}}, {{sourceRaw}})
            """, ct);
        db.Games.Remove(source);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    // ---- AI generations ---------------------------------------------------------------------

    public async Task<AdminGenerationPageDto> SearchGenerations(string query, string? status, int page,
        DateTimeOffset? since, DateTimeOffset? until, CancellationToken ct)
    {
        page = Math.Clamp(page, 0, 100_000);
        var like = $"%{query.Trim()}%";
        var rows = from generation in db.AiGenerations.AsNoTracking()
            join user in db.Users.AsNoTracking() on generation.UserId equals user.Id
            where EF.Functions.ILike(user.Email, like) || EF.Functions.ILike(generation.InputText!, like)
            select new { generation, user.Email };
        if (status == "errors") rows = rows.Where(row => FailureStatuses.Contains(row.generation.Status));
        else if (!string.IsNullOrWhiteSpace(status)) rows = rows.Where(row => row.generation.Status == status);
        if (since is not null) rows = rows.Where(row => row.generation.CreatedAt >= since);
        if (until is not null) rows = rows.Where(row => row.generation.CreatedAt <= until);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(row => row.generation.CreatedAt)
            .ThenByDescending(row => row.generation.Id)
            .Skip(page * GenerationPageSize).Take(GenerationPageSize)
            .Select(row => new AdminGenerationSummaryDto(row.generation.Id, row.Email, row.generation.CreatedAt,
                row.generation.Provider, row.generation.Model, row.generation.Status,
                row.generation.InputText == null
                    ? null
                    : row.generation.InputText.Substring(0, Math.Min(row.generation.InputText.Length, 100)),
                row.generation.ImageContentType != null))
            .ToListAsync(ct);
        return new AdminGenerationPageDto(items, total, page, GenerationPageSize);
    }

    public Task<AdminGenerationRecord?> FindGeneration(Guid id, CancellationToken ct) =>
        (from generation in db.AiGenerations.AsNoTracking()
            join user in db.Users.AsNoTracking() on generation.UserId equals user.Id
            where generation.Id == id
            select new AdminGenerationRecord(user.Email, new AiGeneration
            {
                Id = generation.Id, UserId = generation.UserId, RequestId = generation.RequestId,
                CreatedAt = generation.CreatedAt, CompletedAt = generation.CompletedAt,
                Provider = generation.Provider, Model = generation.Model, Timezone = generation.Timezone,
                ReferenceDate = generation.ReferenceDate, InputText = generation.InputText,
                ImageContentType = generation.ImageContentType, Status = generation.Status,
                Error = generation.Error, ResultJson = generation.ResultJson, ResponsesJson = generation.ResponsesJson,
            })).SingleOrDefaultAsync(ct);

    public Task<AdminGenerationImage?> FindGenerationImage(Guid id, CancellationToken ct) =>
        db.AiGenerations.AsNoTracking().Where(row => row.Id == id && row.Image != null
                                                     && (row.ImageContentType == "image/png" ||
                                                         row.ImageContentType == "image/jpeg" ||
                                                         row.ImageContentType == "image/webp"))
            .Select(row => new AdminGenerationImage(row.Image!, row.ImageContentType!))
            .SingleOrDefaultAsync(ct);

    public Task<int> DeleteSessions(IReadOnlyCollection<long> sessionIds, CancellationToken ct) =>
        db.PlaySessions.Where(row => sessionIds.Contains(row.Id)).ExecuteDeleteAsync(ct);

    public async Task<bool> DeleteGeneration(Guid id, CancellationToken ct) =>
        await db.AiGenerations.Where(row => row.Id == id).ExecuteDeleteAsync(ct) > 0;
}
