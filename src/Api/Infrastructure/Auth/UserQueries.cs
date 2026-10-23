using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Infrastructure.Auth;

public static class UserQueries
{
    /// <summary>The user with this email in any case; users_email_lower_unique allows at most one.</summary>
    public static IQueryable<User> WithEmail(this DbSet<User> users, string email) =>
        users.FromSqlInterpolated($"SELECT * FROM users WHERE lower(email) = lower({email})");
}
