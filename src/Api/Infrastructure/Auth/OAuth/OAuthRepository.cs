using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Infrastructure.Auth.OAuth;

public sealed class OAuthRepository(PlaydexDbContext db)
{
    public async Task AddCode(OAuthAuthorizationCode code, CancellationToken ct)
    {
        // Codes live five minutes; unexchanged ones are cleared as new ones are issued.
        await db.OAuthAuthorizationCodes
            .Where(row => row.ExpiresAt < code.CreatedAt.AddDays(-1) && row.GrantId == null)
            .ExecuteDeleteAsync(ct);
        db.OAuthAuthorizationCodes.Add(code);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Runs <paramref name="action"/> in a transaction and commits what it saved, whatever it returns.</summary>
    public async Task<T> InTransaction<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public Task<OAuthAuthorizationCode?> LockCode(byte[] hash, CancellationToken ct) =>
        db.OAuthAuthorizationCodes
            .FromSqlInterpolated($"select * from oauth_authorization_codes where code_hash = {hash} for update")
            .SingleOrDefaultAsync(ct);

    public Task<OAuthRefreshToken?> LockRefreshToken(byte[] hash, CancellationToken ct) =>
        db.OAuthRefreshTokens
            .FromSqlInterpolated($"select * from oauth_refresh_tokens where token_hash = {hash} for update")
            .SingleOrDefaultAsync(ct);

    public Task<OAuthGrant?> FindGrant(Guid id, CancellationToken ct) =>
        db.OAuthGrants.SingleOrDefaultAsync(grant => grant.Id == id, ct);

    public async Task<int?> TokenVersion(long userId, CancellationToken ct) =>
        await db.Users.Where(user => user.Id == userId).Select(user => (int?)user.TokenVersion).SingleOrDefaultAsync(ct);

    public Task RevokeGrant(Guid id, DateTimeOffset now, CancellationToken ct) =>
        db.OAuthGrants.Where(grant => grant.Id == id && grant.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(grant => grant.RevokedAt, now), ct);

    public void Add(OAuthGrant grant) => db.OAuthGrants.Add(grant);

    public void Add(OAuthRefreshToken token) => db.OAuthRefreshTokens.Add(token);

    /// <summary>The user's connections that still stand, most recently used first.</summary>
    public Task<List<OAuthGrant>> ActiveGrants(long userId, CancellationToken ct) =>
        (from grant in db.OAuthGrants.AsNoTracking()
         join user in db.Users.AsNoTracking() on grant.UserId equals user.Id
         where grant.UserId == userId && grant.RevokedAt == null && grant.TokenVersion == user.TokenVersion
         orderby grant.LastUsedAt descending, grant.CreatedAt descending
         select grant).ToListAsync(ct);

    public Task<int> RevokeGrant(long userId, Guid id, DateTimeOffset now, CancellationToken ct) =>
        db.OAuthGrants.Where(grant => grant.Id == id && grant.UserId == userId && grant.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(grant => grant.RevokedAt, now), ct);

    /// <summary>Whether an access token's grant still stands: not revoked, and the user has not signed out everywhere since.</summary>
    public Task<bool> IsGrantActive(Guid grantId, long userId, CancellationToken ct) =>
        (from grant in db.OAuthGrants.AsNoTracking()
         join user in db.Users.AsNoTracking() on grant.UserId equals user.Id
         where grant.Id == grantId && grant.UserId == userId && grant.RevokedAt == null
               && grant.TokenVersion == user.TokenVersion
         select grant.Id).AnyAsync(ct);
}
