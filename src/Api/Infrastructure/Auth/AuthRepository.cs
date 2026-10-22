using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Infrastructure.Auth;

public class AuthRepository(PlaydexDbContext db)
{
    public virtual Task<User?> FindByEmail(string email, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

    public virtual Task<User?> FindById(long userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public virtual Task<RefreshToken?> FindRefreshToken(byte[] tokenHash, CancellationToken cancellationToken = default) =>
        db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    /// <summary>Stores the first token of a sign-in, and drops the user's expired tokens.</summary>
    public virtual async Task AddRefreshToken(RefreshToken token, CancellationToken cancellationToken = default)
    {
        await DeleteExpiredRefreshTokens(token.UserId, token.CreatedAt, cancellationToken);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Marks the token as used and stores its successor, both or neither. False when the token was used already,
    /// including by a request racing this one.
    /// </summary>
    public virtual async Task<bool> RotateRefreshToken(
        long tokenId,
        RefreshToken successor,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var rotated = await db.RefreshTokens
            .Where(token => token.Id == tokenId && token.RotatedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RotatedAt, successor.CreatedAt), cancellationToken);
        if (rotated == 0)
        {
            return false;
        }

        await DeleteExpiredRefreshTokens(successor.UserId, successor.CreatedAt, cancellationToken);
        db.RefreshTokens.Add(successor);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return true;
    }

    public virtual Task RevokeRefreshTokenFamily(Guid familyId, CancellationToken cancellationToken = default) =>
        db.RefreshTokens.Where(token => token.FamilyId == familyId).ExecuteDeleteAsync(cancellationToken);

    private Task<int> DeleteExpiredRefreshTokens(long userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(token => token.UserId == userId && token.ExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);
}
