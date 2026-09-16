using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Playdex.Persistence;

namespace Api.Modules.Games;

public sealed record GameResolutionRequest(
    GameExternalIdentityType? Provider,
    string? ExternalId,
    string Title,
    string? CapsuleUrl);

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
        var identityKey = identityType?.ToKey();
        var lockKey = identityType is null
            ? $"new:{Guid.NewGuid():N}"
            : $"identity:{identityKey}:{externalId}";

        IDbContextTransaction? ownedTransaction = null;
        if (db.Database.CurrentTransaction is null)
        {
            ownedTransaction = await db.Database.BeginTransactionAsync(ct);
        }

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                ct);

            GameExternalIdentity? identity = null;
            Game? game = null;
            if (identityType is not null)
            {
                identity = await db.GameExternalIdentities
                    .FirstOrDefaultAsync(
                        row => row.Type == identityType.Value && row.ExternalId == externalId,
                        ct);
                if (identity is not null)
                {
                    game = await db.Games.SingleAsync(row => row.Id == identity.GameId, ct);
                }
            }

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
            }
            else
            {
                if (!game.TitleLocked)
                {
                    game.Title = title;
                }
                if (!game.CapsuleUrlLocked && request.CapsuleUrl is not null)
                {
                    game.CapsuleUrl = request.CapsuleUrl;
                }
                game.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);

            if (identityType is not null)
            {
                if (identity is null)
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
                else
                {
                    identity.Title = title;
                    identity.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync(ct);
            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(ct);
            }
            return new GameResolution(game.Id, identity?.Id, game.CapsuleUrl is null);
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
}
