using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Playdex.Persistence;

namespace Api.Modules.Games;

/// <param name="TitleIsAuthoritative">
/// The title comes from a source the server trusts (a platform API, IGDB) and may rename an existing game. A title a
/// client sends only names a game the first time it is seen: games are shared by every user, so one user's capture
/// must not rename them for everybody.
/// </param>
public sealed record GameResolutionRequest(
    GameExternalIdentityType? Provider,
    string? ExternalId,
    string Title,
    string? CapsuleUrl,
    bool TitleIsAuthoritative = false);

public sealed record GameResolution(long GameId, long? GameExternalIdentityId, bool NeedsCapsule);

public interface IGameResolver
{
    Task<GameResolution> Resolve(GameResolutionRequest request, CancellationToken ct);
}

public static class GameExternalIdentityTypes
{
    public static GameExternalIdentityType? FromKey(string key) => key.Trim().ToLowerInvariant() switch
    {
        "steam" => GameExternalIdentityType.Steam,
        "psn" => GameExternalIdentityType.Psn,
        "xbox" => GameExternalIdentityType.Xbox,
        "igdb" => GameExternalIdentityType.Igdb,
        _ => null,
    };

    public static string ToKey(this GameExternalIdentityType type) => type switch
    {
        GameExternalIdentityType.Steam => "steam",
        GameExternalIdentityType.Psn => "psn",
        GameExternalIdentityType.Xbox => "xbox",
        GameExternalIdentityType.Igdb => "igdb",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}

public sealed class GameResolver(PlaydexDbContext db) : IGameResolver
{
    // Without an external id a game is found by title, ignoring case, spacing, ASCII punctuation and ©/®/™, so
    // "Half-Life 2" finds "HALF LIFE 2™". Other letters are kept: "Ведьмак 3" and "ペルソナ3" must not both become "3".
    // games_title_key_idx (migration 016) indexes the same expression.
    private static readonly string GamesByTitleSql = $"SELECT * FROM games WHERE {TitleKey("title")} = {TitleKey("{0}")}";
    private static readonly string LockTitleSql =
        $"SELECT pg_advisory_xact_lock(hashtextextended('title:' || {TitleKey("{0}")}, 0))";

    public async Task<GameResolution> Resolve(GameResolutionRequest request, CancellationToken ct)
    {
        var title = request.Title.Trim();
        if (title.Length == 0)
        {
            throw new ArgumentException("Game title is required.", nameof(request));
        }

        var externalId = string.IsNullOrWhiteSpace(request.ExternalId)
            ? null
            : request.ExternalId.Trim();
        var identityType = externalId is null ? null : request.Provider;

        // Most calls name a game that exists and is up to date. They neither write nor take the lock, so players of
        // the same game don't queue behind each other.
        var (known, knownIdentity) = await Find(identityType, externalId, title, tracked: false, ct);
        if (known is not null && !Update(known, knownIdentity, request, title, DateTimeOffset.UtcNow))
        {
            return Resolution(known, knownIdentity);
        }

        IDbContextTransaction? ownedTransaction = null;
        if (db.Database.CurrentTransaction is null)
        {
            ownedTransaction = await db.Database.BeginTransactionAsync(ct);
        }

        try
        {
            if (identityType is null)
            {
                await db.Database.ExecuteSqlRawAsync(LockTitleSql, [title], ct);
            }
            else
            {
                var lockKey = $"identity:{identityType.Value.ToKey()}:{externalId}";
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                    ct);
            }

            var (game, identity) = await Find(identityType, externalId, title, tracked: true, ct);
            var now = DateTimeOffset.UtcNow;
            if (game is null)
            {
                game = new Game
                {
                    Title = title,
                    CapsuleUrl = request.CapsuleUrl,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Games.Add(game);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                Update(game, identity, request, title, now);
            }

            if (identityType is not null && identity is null)
            {
                identity = new GameExternalIdentity
                {
                    GameId = game.Id,
                    Type = identityType.Value,
                    ExternalId = externalId!,
                    Title = title,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.GameExternalIdentities.Add(identity);
            }

            await db.SaveChangesAsync(ct);
            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(ct);
            }
            return Resolution(game, identity);
        }
        catch
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// The game an external id points to, with that identity; without an external id, the game with this title. An
    /// external id nobody has sent before finds nothing and gets a game of its own.
    /// </summary>
    private async Task<(Game? Game, GameExternalIdentity? Identity)> Find(
        GameExternalIdentityType? identityType,
        string? externalId,
        string title,
        bool tracked,
        CancellationToken ct)
    {
        if (identityType is null)
        {
            var byTitle = db.Games.FromSqlRaw(GamesByTitleSql, title);
            var game = await (tracked ? byTitle : byTitle.AsNoTracking())
                .OrderByDescending(row => row.Title == title)
                .ThenBy(row => row.Id)
                .FirstOrDefaultAsync(ct);
            return (game, null);
        }

        var byIdentity =
            from identity in db.GameExternalIdentities
            where identity.Type == identityType.Value && identity.ExternalId == externalId
            join game in db.Games on identity.GameId equals game.Id
            select new { Game = game, Identity = identity };
        var match = await (tracked ? byIdentity : byIdentity.AsNoTracking()).FirstOrDefaultAsync(ct);
        return (match?.Game, match?.Identity);
    }

    /// <summary>
    /// Brings an existing game in line with the request and tells whether anything changed. On an untracked copy it
    /// only tells whether a write is needed.
    /// </summary>
    private static bool Update(
        Game game,
        GameExternalIdentity? identity,
        GameResolutionRequest request,
        string title,
        DateTimeOffset now)
    {
        // A game found by title already has that title up to case and punctuation, and several sources may name it:
        // renaming it or swapping its poster would only make them take turns.
        var foundByTitle = identity is null;
        var renamesGame = !foundByTitle && request.TitleIsAuthoritative && !game.TitleLocked && game.Title != title;
        var renamesIdentity = !foundByTitle && request.TitleIsAuthoritative && identity!.Title != title;
        var setsCapsule = request.CapsuleUrl is not null
            && !game.CapsuleUrlLocked
            && game.CapsuleUrl != request.CapsuleUrl
            && (!foundByTitle || game.CapsuleUrl is null);

        if (renamesGame)
        {
            game.Title = title;
        }
        if (setsCapsule)
        {
            game.CapsuleUrl = request.CapsuleUrl;
        }
        if (renamesGame || setsCapsule)
        {
            game.UpdatedAt = now;
        }
        if (renamesIdentity)
        {
            identity!.Title = title;
            identity.UpdatedAt = now;
        }
        return renamesGame || setsCapsule || renamesIdentity;
    }

    private static GameResolution Resolution(Game game, GameExternalIdentity? identity) =>
        new(game.Id, identity?.Id, game.CapsuleUrl is null);

    private static string TitleKey(string operand) =>
        $"""btrim(regexp_replace(lower({operand}), '([^a-z0-9\u0080-\U0010ffff]|[©®™])+', ' ', 'g'))""";
}
