using Microsoft.EntityFrameworkCore;
using Playdex.Api.Data;

namespace Playdex.Api.Auth;

public interface IAuthRepository
{
    Task<User?> FindByEmail(string email, CancellationToken cancellationToken = default);
    Task<User?> FindById(long userId, CancellationToken cancellationToken = default);
}

public sealed class AuthRepository(PlaydexDbContext db) : IAuthRepository
{
    public Task<User?> FindByEmail(string email, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

    public Task<User?> FindById(long userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
}
