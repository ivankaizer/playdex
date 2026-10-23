using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.Privacy;

public sealed class PrivacyRepository(PlaydexDbContext db)
{
    public Task<User?> FindUser(long userId, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, ct);

    public async Task<AccountExportDto> Export(User user, DateTimeOffset now, CancellationToken ct)
    {
        var userId = user.Id;
        var settings = await db.UserSettings.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => row.IgnoreShortSessionsMinutes)
            .FirstOrDefaultAsync(ct);

        var channels = await db.NotificationChannels.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.CreatedAt)
            .Select(row => new ExportNotificationChannelDto(row.Transport, row.Destination))
            .ToListAsync(ct);

        var integrations = await (
                from integration in db.Integrations.AsNoTracking()
                where integration.UserId == userId
                join platform in db.Platforms on integration.PlatformId equals platform.Id into platforms
                from platform in platforms.DefaultIfEmpty()
                orderby integration.CreatedAt
                select new
                {
                    integration.Type,
                    Platform = platform == null ? null : platform.Key,
                    integration.AccountId,
                    HasApiKey = integration.ApiKey != null,
                    integration.Active,
                    integration.CreatedAt,
                })
            .ToListAsync(ct);

        var sessions = await (
                from session in db.PlaySessions.AsNoTracking()
                where session.UserId == userId
                join game in db.Games on session.GameId equals game.Id
                join platform in db.Platforms on session.PlatformId equals platform.Id
                orderby session.StartedAt
                select new { Game = game.Title, Platform = platform.Key, session.CaptureMethod, session.StartedAt, session.EndedAt })
            .ToListAsync(ct);

        var ratings = await (
                from rating in db.GameRatings.AsNoTracking()
                where rating.UserId == userId
                join game in db.Games on rating.GameId equals game.Id
                orderby game.Title
                select new ExportGameRatingDto(game.Title, rating.Rating))
            .ToListAsync(ct);

        var ignored = await (
                from row in db.IgnoredGames.AsNoTracking()
                where row.UserId == userId
                join game in db.Games on row.GameId equals game.Id
                orderby game.Title
                select new ExportGameDto(game.Title))
            .ToListAsync(ct);

        var connections = await (
                from row in db.GameConnections.AsNoTracking()
                where row.UserId == userId
                join game in db.Games on row.GameId equals game.Id
                join connected in db.Games on row.ConnectedGameId equals connected.Id
                orderby game.Title
                select new ExportGameConnectionDto(game.Title, connected.Title))
            .ToListAsync(ct);

        var batches = await db.SessionDraftBatches.AsNoTracking()
            .Where(batch => batch.UserId == userId)
            .OrderBy(batch => batch.CreatedAt)
            .Select(batch => new ExportDraftBatchDto(
                batch.CreatedAt,
                batch.Timezone,
                batch.ReferenceDate,
                batch.Rows.OrderBy(row => row.Position)
                    .Select(row => new ExportDraftRowDto(row.Title, row.Date, row.DurationMinutes, row.PlatformKey, row.Status))
                    .ToList()))
            .ToListAsync(ct);

        var generations = await db.AiGenerations.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.CreatedAt)
            .ToListAsync(ct);

        return new AccountExportDto(
            now,
            new ExportProfileDto(user.Email, user.CreatedAt),
            new ExportSettingsDto(settings),
            channels,
            integrations
                .Select(row => new ExportIntegrationDto(
                    row.Type.ToString().ToLowerInvariant(), row.Platform, row.AccountId, row.HasApiKey, row.Active, row.CreatedAt))
                .ToList(),
            sessions
                .Select(row => new ExportSessionDto(
                    row.Game, row.Platform, row.CaptureMethod.ToString().ToLowerInvariant(), row.StartedAt, row.EndedAt))
                .ToList(),
            ratings,
            ignored,
            connections,
            batches,
            generations
                .Select(row => new ExportAiGenerationDto(
                    row.CreatedAt,
                    row.Provider,
                    row.Model,
                    row.Status,
                    row.InputText,
                    row.ImageContentType,
                    row.Image is null ? null : Convert.ToBase64String(row.Image),
                    row.ResultJson))
                .ToList());
    }

    /// <summary>
    /// Permanently deletes the user. Play sessions and sync responses reference the user and their
    /// integrations with RESTRICT, so they go first; every other user-owned table cascades from users.
    /// </summary>
    public async Task<bool> DeleteUser(long userId, CancellationToken ct)
    {
        // Join the caller's transaction when there is one; otherwise make the three deletes atomic here.
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await db.PlaySessions.Where(session => session.UserId == userId).ExecuteDeleteAsync(ct);
        await db.PollResults
            .Where(poll => db.Integrations.Any(integration => integration.Id == poll.IntegrationId && integration.UserId == userId))
            .ExecuteDeleteAsync(ct);
        var deleted = await db.Users.Where(user => user.Id == userId).ExecuteDeleteAsync(ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return deleted > 0;
    }
}
